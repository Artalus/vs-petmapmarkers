using System;
using System.Collections.Generic;
using System.Linq;
using PetAI;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace PetMapMarkers;

public class PetTracker
{
    public readonly int IntervalMs;

    public IReadOnlySet<long> WatchedIds => watchers;
    public IReadOnlySet<long> TameCandidateIds => tameCandidates;

    private readonly ICoreServerAPI sapi;
    private readonly Dictionary<long, TrackedPet> tracked = [];
    private readonly HashSet<string> dirtyOwners = [];
    private readonly HashSet<long> tameCandidates = [];
    private WaypointMapLayer? layer = null;
    private readonly HashSet<long> watchers = [];

    // transient:
    // - filled synchronously when a tracked pup expires
    // - drained on the immediately-following SpawnEntity call
    private readonly Queue<PetMemento> pendingGrowups = [];

    // keyed by adult entity ID; filled once adult arrives, drained in TryAddPetFromEntity
    private readonly Dictionary<long, PetMemento> growupByEntityId = [];

    // keyed by entity ID at time of caging; filled when pet is caged, drained when pet is released.
    private readonly Dictionary<long, PetMemento> caged = [];

    // TODO: support reloading config without restarting server (configlib json api?)
    private readonly ModConfig cfg;

    // The original entity ID is stamped into WatchedAttributes[...] when pet gets tracked.
    // AnimalCages serializes the full entity into the cage item before calling `Die`, so it should
    // survive cage serialization and can be matched back on release.

    private const string originalIdKey = "petmarker:originalId";

    public PetTracker(ICoreServerAPI sapi, ModConfig cfg)
    {
        this.sapi = sapi;
        this.cfg = cfg;
        IntervalMs = (int)(cfg.UpdateIntervalSeconds * 1000);

        sapi.Event.OnEntityLoaded += OnEntityLoaded;
        // despite what doc says, loaded event does not fire upon something new spawning
        sapi.Event.OnEntitySpawn += OnEntitySpawn;
        sapi.Event.OnEntityDespawn += OnEntityDespawn;

        sapi.Event.RegisterGameTickListener(OnTick, IntervalMs);
        LogNotification("PetTracker started; tracking " + IntervalMs + "ms ticks");

        if (cfg.FullScanMinutes > 0)
        {
            var fullMs = cfg.FullScanMinutes * 60 * 1000;
            sapi.Event.RegisterGameTickListener(OnFullScanTick, fullMs);
            LogNotification(
                $"Full-scan registered every {cfg.FullScanMinutes} minutes ({fullMs}ms)"
            );
        }
    }

    private void OnTick(float dt)
    {
        try
        {
            // layers become active even later than Start()
            layer ??= WaypointUtil.GetWaypointLayer(sapi);
            // toarray avoids directly mutating collection while iterating
            foreach (var tamedId in tameCandidates.ToArray())
            {
                if (
                    sapi.World.LoadedEntities.TryGetValue(tamedId, out var entity)
                    && entity != null
                )
                {
                    if (TryAddPetFromEntity(entity))
                    {
                        tameCandidates.Remove(tamedId);
                    }
                }
            }

            foreach (var pet in tracked.Values)
            {
                if (
                    sapi.World.LoadedEntities.TryGetValue(pet.EntityId, out var entity)
                    && entity != null
                )
                {
                    pet.IsLoaded = true;
                    bool isDowned = IsDowned(entity);

                    if (pet.WasDowned && !isDowned)
                        HandlePetHealed(pet);
                    else if (!pet.WasDowned && isDowned)
                        HandlePetDowned(pet);
                    pet.WasDowned = isDowned;

                    // update mutable fields
                    pet.LastKnownPosition = entity.Pos.XYZ;
                    pet.Name = entity.GetName();
                }
                else
                {
                    if (pet.IsLoaded)
                        LogError(
                            $"Pet id={pet.EntityId} not found in loaded entities; did OnEntityDespawn not fire?!"
                        );
                    pet.IsLoaded = false;
                }
                if (layer != null)
                {
                    SyncWaypoints();
                    WaypointUtil.ResendWaypointsToAll(layer, sapi, dirtyOwners);
                    dirtyOwners.Clear();
                }
                else
                {
                    LogError("Waypoint layer is null in OnTick, how >:(");
                }
            }
        }
        catch (Exception e)
        {
            LogError($"exception in PetTracker OnTick: {e}");
            throw;
        }
    }

    // TODO: add /command to trigger it on demand
    private void OnFullScanTick(float dt)
    {
        try
        {
            foreach (var entity in sapi.World.LoadedEntities.Values)
            {
                if (entity == null)
                    continue;
                TryAddPetFromEntity(entity);
            }
        }
        catch (Exception e)
        {
            LogError($"exception in PetTracker OnFullScanTick: {e}");
            throw;
        }
    }

    private void OnEntityLoaded(Entity entity)
    {
        try
        {
            TryAddPetFromEntity(entity);
            RegisterDomesticationWatcher(entity);
        }
        catch (Exception e)
        {
            LogError($"exception in OnEntityLoaded: {e}");
            throw;
        }
    }

    private void OnEntitySpawn(Entity entity)
    {
        try
        {
            // BecomeAdult calls Die(Expire) then SpawnEntity synchronously.
            // Claim memento here before TryAddPetFromEntity runs so it is available when the adult gets tracked later
            // TODO: fragile, need to match pup to spawning entity
            if (pendingGrowups.Count > 0 && entity.HasBehavior<EntityBehaviorTameable>())
                growupByEntityId[entity.EntityId] = pendingGrowups.Dequeue();

            TryAddPetFromEntity(entity);
            RegisterDomesticationWatcher(entity);
        }
        catch (Exception e)
        {
            LogError($"exception in OnEntitySpawn: {e}");
            throw;
        }
    }

    private void OnEntityDespawn(Entity entity, EntityDespawnData data)
    {
        try
        {
            if (!tracked.TryGetValue(entity.EntityId, out var pet))
                return;

            LogTrace($"Despawned pet id={entity.EntityId} name={pet.Name} reason={data.Reason}");

            if (
                data.Reason
                is EnumDespawnReason.OutOfRange
                    or EnumDespawnReason.Unload
                    or EnumDespawnReason.Disconnect
            )
            {
                // TODO: take a closer look at what despawn reason is
                // expected temporary despawn, keep in tracking with IsLoaded=false and hope for a future respawn
                LogTrace(
                    $"Pet temporarily despawned id={entity.EntityId} name={pet.Name} reason={data.Reason}"
                );
                pet.IsLoaded = false;
            }

            // AnimalCages does this
            if (data.Reason == EnumDespawnReason.PickedUp)
                HandlePetCaged(pet, entity);

            // petai taming variant swap - remove from tracking and hope that another entity will spawn soon
            // TODO: but this would cause waypoint duplication + icon and color loss...
            // (unless it maintains entity id somehow)
            // TODO: needs test
            if (data.Reason == EnumDespawnReason.Expire)
            {
                // BecomeAdult calls Die(Expire) then SpawnEntity synchronously.
                if (entity.GetBehavior<EntityBehaviorGrow>() != null)
                    PrepareGrowupMemento(pet);
                tracked.Remove(entity.EntityId);
                LogTrace($"Expired - forget pet");
            }
            // TODO: implement & test waypoint removal if entity is fully gone? or keep as memorial?
        }
        catch (Exception e)
        {
            LogError($"exception in OnEntityDespawn: {e}");
            throw;
        }
    }

    private void RegisterDomesticationWatcher(Entity entity)
    {
        // avoid watching over drifters and players and locust nests
        if (!entity.HasBehavior<EntityBehaviorTameable>())
            return;
        var title = $"{entity.EntityId} {entity.Code.Path} {entity.GetName()}";

        // avoid registering the listener more than once per entity (both OnEntityLoaded
        // and OnEntitySpawn call this; guard against double-registration on reloads)
        if (!watchers.Add(entity.EntityId))
        {
            LogTrace($"Skipping duplicate domestication watcher for tameable {title}");
            return;
        }
        LogTrace($"Watching over tameable {title}");

        const string petaiDomesticationStatusKey = "domesticationstatus";
        var watched = entity.WatchedAttributes;
        watched.RegisterModifiedListener(
            petaiDomesticationStatusKey,
            () =>
            {
                try
                {
                    bool alreadyTracked = tracked.ContainsKey(entity.EntityId);
                    bool isCandidate = tameCandidates.Contains(entity.EntityId);
                    var tameable = entity.GetBehavior<EntityBehaviorTameable>();
                    if (alreadyTracked && string.IsNullOrEmpty(tameable?.OwnerId))
                    {
                        LogTrace($"Tracked pet {title} lost owner - forget pet");
                        if (tameable == null)
                            LogError(
                                "Domestication status was removed - but this should never happen with PetAI?!"
                            );
                        var pet = tracked[entity.EntityId];
                        bool r = tracked.Remove(entity.EntityId);
                        if (!r)
                            LogError("Attempted to remove pet that was not tracked?!");
                        if (layer != null)
                        {
                            var wp = WaypointUtil.FindWaypointByGuid(layer, pet.WaypointUid);
                            if (wp != null)
                            {
                                layer.Waypoints.Remove(wp);
                                dirtyOwners.Add(pet.OwnerUid);
                            }
                            else
                                LogError("Waypoint not found in domesticationstatus listener");
                        }
                        else
                            LogError(
                                "Waypoint layer is null in domesticationstatus listener, waypoint not deleted after abandon"
                            );
                        return;
                    }
                    if (!alreadyTracked && !isCandidate && tameable != null)
                    {
                        // PetAI updates the underlying ITreeAttribute quite a few times per tick upon taming.
                        // Just mark the pet and let OnTick to handle it when the tree stabilizes.
                        LogTrace(
                            $"Pet {title} not tracked yet and domestication status is {tameable.DomesticationStatus} - mark to track"
                        );
                        tameCandidates.Add(entity.EntityId);
                    }
                }
                catch (Exception e)
                {
                    LogError(
                        $"exception in domesticationstatus listener for entity {entity.EntityId}: {e}"
                    );
                    throw;
                }
            }
        );
    }

    /// <summary>
    /// Returns true if the entity is successfully tracked
    /// </summary>
    private bool TryAddPetFromEntity(Entity entity)
    {
        var tameable = entity.GetBehavior<EntityBehaviorTameable>();
        if (tameable == null)
            return false;

        if (tameable.DomesticationLevel == DomesticationLevel.WILD)
            return false;

        bool isTaming = tameable.DomesticationLevel == DomesticationLevel.TAMING;
        if (isTaming && !cfg.TrackTamingPets)
            return false;

        string owner = tameable.OwnerId;
        // owner might be null if we happened upon pet in process of initialization
        if (string.IsNullOrEmpty(owner))
            return false;

        string petName = entity.GetName();
        bool isDowned = IsDowned(entity);

        if (!tracked.TryGetValue(entity.EntityId, out var pet))
        {
            pet = new TrackedPet()
            {
                EntityId = entity.EntityId,
                OwnerUid = owner,
                WaypointUid = WaypointUidFor(entity),
                Name = petName,
                WasDowned = isDowned,
                IsLoaded = true,
                LastKnownPosition = entity.Pos.XYZ,
            };

            long origId = entity.WatchedAttributes.GetLong(originalIdKey, 0);
            if (growupByEntityId.TryGetValue(entity.EntityId, out var memento))
            {
                growupByEntityId.Remove(entity.EntityId);
                pet.SavedColor = memento.SavedColor;
                pet.SavedPinned = memento.SavedPinned;
                LogNotification(
                    $"Tracking grown/swapped pet id={pet.EntityId} creature='{entity.Code.Path}' - new guid={pet.WaypointUid}, deleting old={memento.OldWaypointGuid}"
                );
            }
            else if (origId != 0 && caged.TryGetValue(origId, out var cagingMemento))
            {
                caged.Remove(origId);
                pet.SavedColor = cagingMemento.SavedColor;
                pet.SavedPinned = cagingMemento.SavedPinned;
                // Remove old (black) cage waypoint so SyncWaypoints creates a fresh one with
                // restored color under the new entity's guid.
                if (layer != null)
                {
                    var oldWp = WaypointUtil.FindWaypointByGuid(
                        layer,
                        cagingMemento.OldWaypointGuid
                    );
                    if (oldWp != null)
                    {
                        layer.Waypoints.Remove(oldWp);
                        dirtyOwners.Add(owner);
                    }
                }
                LogNotification(
                    $"Released-from-cage pet id={pet.EntityId} name='{pet.Name}' - new guid={pet.WaypointUid}, removed old={cagingMemento.OldWaypointGuid}"
                );
            }
            else
            {
                LogNotification(
                    $"Tracking new pet id={pet.EntityId} creature='{entity.Code.Path}' owner='{pet.OwnerUid}' name='{pet.Name}' down={pet.WasDowned}"
                );
            }

            // TODO: game save and reload probably will mess this up
            // Stamp current entity ID so it survives cage serialization and can be matched on release.
            entity.WatchedAttributes.SetLong(originalIdKey, entity.EntityId);
            tracked[entity.EntityId] = pet;
        }
        else
        {
            pet.IsLoaded = true;
            if (pet.WasDowned && !isDowned)
                HandlePetHealed(pet);
            else if (!pet.WasDowned && isDowned)
                HandlePetDowned(pet);
            pet.WasDowned = isDowned;
        }
        return true;
    }

    private void PrepareGrowupMemento(TrackedPet pet)
    {
        int? savedColor = null;
        bool? savedPinned = null;
        if (layer != null)
        {
            var wp = WaypointUtil.FindWaypointByGuid(layer, pet.WaypointUid);
            if (wp != null)
            {
                // when downed, SavedColor/SavedPinned hold the pre-downed values; prefer those
                savedColor = pet.SavedColor ?? wp.Color;
                savedPinned = pet.SavedPinned ?? wp.Pinned;
                layer.Waypoints.Remove(wp);
                dirtyOwners.Add(pet.OwnerUid);
                LogTrace($"Removed old waypoint guid={pet.WaypointUid} for growing pet");
            }
        }
        var memento = new PetMemento
        {
            SavedColor = savedColor,
            SavedPinned = savedPinned,
            OldWaypointGuid = pet.WaypointUid,
        };
        // Adult may have already spawned before this Expire despawn event fired.
        // Search tameCandidates for an entity with the same owner — that is the adult.
        long adultId = FindGrowupAdultInCandidates(pet.OwnerUid);
        if (adultId != 0)
        {
            growupByEntityId[adultId] = memento;
            LogTrace(
                $"Associated grow-up memento with already-spawned adult id={adultId} for pet '{pet.Name}'"
            );
        }
        else
        {
            // Fallback: adult has not spawned yet; OnEntitySpawn will claim it.
            pendingGrowups.Enqueue(memento);
            LogTrace($"Queued grow-up memento for pet id={pet.EntityId} name='{pet.Name}'");
        }
    }

    private long FindGrowupAdultInCandidates(string ownerUid)
    {
        foreach (var candidateId in tameCandidates)
        {
            if (sapi.World.LoadedEntities.TryGetValue(candidateId, out var candidate))
            {
                var status = candidate.GetBehavior<EntityBehaviorTameable>();
                if (status?.OwnerId == ownerUid)
                    return candidateId;
            }
        }
        return 0;
    }

    private void HandlePetCaged(TrackedPet pet, Entity entity)
    {
        int restorationColor = ModConfig.ColorStringToArgb(cfg.DefaultColor);
        bool restorationPinned = false;
        if (layer != null)
        {
            var wp = WaypointUtil.FindWaypointByGuid(layer, pet.WaypointUid);

            if (wp != null)
            {
                // When downed, SavedColor/SavedPinned hold the pre-downed values; prefer those
                restorationColor = pet.SavedColor ?? wp.Color;
                restorationPinned = pet.SavedPinned ?? wp.Pinned;
                // should become a half-transparent black
                wp.Color = 0;
                wp.Pinned = false;
                dirtyOwners.Add(pet.OwnerUid);
            }
            else
                LogError($"Waypoint not found when caging pet id={pet.EntityId} name='{pet.Name}'");
        }
        else
            LogError("Waypoint layer is null in HandlePetCaged, cannot save waypoint values");

        var memento = new PetMemento
        {
            OldWaypointGuid = pet.WaypointUid,
            SavedColor = restorationColor,
            SavedPinned = restorationPinned,
        };
        caged[entity.EntityId] = memento;
        tracked.Remove(pet.EntityId);
        LogTrace(
            $"Pet caged id={pet.EntityId} code='{entity.Code.Path}' name='{pet.Name}' waypoint dimmed"
        );
    }

    private void HandlePetDowned(TrackedPet pet)
    {
        try
        {
            var layer = WaypointUtil.GetWaypointLayer(sapi);
            var wp = WaypointUtil.FindWaypointByGuid(layer, pet.WaypointUid);

            if (wp != null)
            {
                pet.SavedColor = wp.Color;
                pet.SavedPinned = wp.Pinned;
                wp.Color = ModConfig.ColorStringToArgb(cfg.DownedColor);
                wp.Pinned = true;
                dirtyOwners.Add(pet.OwnerUid);
            }

            LogTrace($"Pet downed id={pet.EntityId} name='{pet.Name}' owner={pet.OwnerUid}");
        }
        catch (Exception e)
        {
            LogError($"exception in OnPetDowned: {e}");
            throw;
        }
    }

    private void HandlePetHealed(TrackedPet pet)
    {
        try
        {
            var layer = WaypointUtil.GetWaypointLayer(sapi);
            var wp = WaypointUtil.FindWaypointByGuid(layer, pet.WaypointUid);

            if (wp != null)
            {
                wp.Color = pet.SavedColor ?? ModConfig.ColorStringToArgb(cfg.DefaultColor);
                wp.Pinned = pet.SavedPinned ?? false;
                pet.SavedColor = null;
                pet.SavedPinned = null;
                dirtyOwners.Add(pet.OwnerUid);
            }

            LogTrace($"Pet healed id={pet.EntityId} name='{pet.Name}' owner={pet.OwnerUid}");
        }
        catch (Exception e)
        {
            LogError($"exception in OnPetHealed: {e}");
            throw;
        }
    }

    private void SyncWaypoints()
    {
        // layer can be null if the map has not fully initialized upon our first tick
        if (layer == null)
            return;

        foreach (var pet in tracked.Values)
        {
            if (!pet.IsLoaded)
                continue;

            var wp = WaypointUtil.FindWaypointByGuid(layer, pet.WaypointUid);
            var pos = pet.LastKnownPosition ?? new Vec3d(0, 0, 0);
            var title = pet.Name;
            if (wp == null)
            { // waypoint missing - create it
                // SavedColor/SavedPinned may carry inherited color from grow-up
                var color = pet.SavedColor ?? ModConfig.ColorStringToArgb(cfg.DefaultColor);
                var pinned = pet.SavedPinned ?? false;
                if (pet.WasDowned)
                {
                    // keep SavedColor/SavedPinned as restoration target for OnPetHealed
                    color = ModConfig.ColorStringToArgb(cfg.DownedColor);
                    pinned = true;
                }
                else
                {
                    pet.SavedColor = null;
                    pet.SavedPinned = null;
                }

                var newWp = new Waypoint()
                {
                    Guid = pet.WaypointUid,
                    Title = title,
                    Position = pos,
                    Icon = cfg.DefaultIcon,
                    Pinned = pinned,
                    OwningPlayerUid = pet.OwnerUid,
                    Color = color,
                };

                // NOTE: .AddWaypoint() requires a player entity to exist in world.
                // Add directly to list since this code *will* run upon starting server with this
                // mod for the very first time, when no players are present.
                layer.Waypoints.Add(newWp);
                LogTrace($"Created waypoint for {pet.Name} (guid={newWp.Guid})");
            }
            else
            { // waypoint exists - update it
                if (wp.Position != pos)
                {
                    wp.Position = pos;
                    dirtyOwners.Add(pet.OwnerUid);
                }
                if (wp.Title != title)
                {
                    wp.Title = title;
                    dirtyOwners.Add(pet.OwnerUid);
                }
                // color and pin dirtying is handled in OnDowned/Healed callbacks
            }
        }
    }

    private void LogNotification(string message)
    {
        sapi.World.Logger.Notification("[PetMapMarkers] " + message);
    }

    private void LogTrace(string message)
    {
        sapi.World.Logger.VerboseDebug("[PetMapMarkers][Trace] " + message);
    }

    private void LogError(string message)
    {
        sapi.World.Logger.Error("[PetMapMarkers][ERROR] " + message);
    }

    public static bool IsDowned(Entity entity)
    {
        var b = entity.GetBehavior<EntityBehaviorMortallyWoundable>();
        if (b == null)
            return false;
        return b.HealthState != EnumEntityHealthState.Normal;
    }

    public static string WaypointUidFor(long id)
    {
        return $"petmarker-{id}";
    }

    public static string WaypointUidFor(Entity entity)
    {
        return WaypointUidFor(entity.EntityId);
    }

    public IEnumerable<Entity> GetTrackedEntities()
    {
        foreach (var pet in tracked.Values)
        {
            if (
                sapi.World.LoadedEntities.TryGetValue(pet.EntityId, out var entity)
                && entity != null
            )
                yield return entity;
        }
    }
}

public class TrackedPet
{
    public required long EntityId;
    public required string OwnerUid;
    public required string Name;
    public required string WaypointUid;
    public bool WasDowned;
    public bool IsLoaded;
    public int? SavedColor;
    public bool? SavedPinned;
    public Vec3d? LastKnownPosition;
}

/// <summary>
/// Remember state of a pup that has expired and possibly was replaced by adult,
/// or a pet that was caged and possibly will be replaced by the same pet upon release.
/// </summary>
class PetMemento
{
    public int? SavedColor;
    public bool? SavedPinned;
    public required string OldWaypointGuid;
}
