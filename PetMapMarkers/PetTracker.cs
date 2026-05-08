using System;
using System.Collections.Generic;
using PetAI;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PetMapMarkers;

public class PetTracker
{
    public readonly int IntervalMs;

    private readonly ICoreServerAPI sapi;
    private readonly Dictionary<long, TrackedPet> tracked = [];

    // TODO: support reloading config without restarting server (configlib json api?)
    private readonly ModConfig cfg;

    public PetTracker(ICoreServerAPI sapi, ModConfig cfg)
    {
        this.sapi = sapi;
        this.cfg = cfg;
        IntervalMs = (int)(cfg.UpdateIntervalSeconds * 1000);

        sapi.Event.OnEntityLoaded += OnEntityLoaded;
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
            var snapshot = new List<TrackedPet>(tracked.Values);
            foreach (var pet in snapshot)
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
                }
                else
                {
                    pet.IsLoaded = false;
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
        }
        catch (Exception e)
        {
            LogError($"exception in OnEntityLoaded: {e}");
            throw;
        }
    }

    private void OnEntityDespawn(Entity entity, EntityDespawnData data)
    {
        if (!tracked.TryGetValue(entity.EntityId, out var pet))
            return;
        try
        {
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

            // petai taming variant swap - remove from tracking and hope that another entity will spawn soon
            // TODO: but this would cause waypoint duplication + icon and color loss...
            // (unless it maintains entity id somehow)
            // TODO: needs test
            if (data.Reason == EnumDespawnReason.Expire)
            {
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

    private void TryAddPetFromEntity(Entity entity)
    {
        var tameable = entity.GetBehavior<EntityBehaviorTameable>();
        if (tameable == null)
            return;

        if (tameable.DomesticationLevel == DomesticationLevel.WILD)
            return;

        bool isTaming = tameable.DomesticationLevel == DomesticationLevel.TAMING;
        if (isTaming && !cfg.TrackTamingPets)
            return;

        string petName = entity.GetName();
        bool isDowned = IsDowned(entity);

        if (!tracked.TryGetValue(entity.EntityId, out var pet))
        {
            pet = new TrackedPet()
            {
                EntityId = entity.EntityId,
                OwnerUid = tameable.OwnerId,
                Name = petName,
                WasDowned = isDowned,
                IsLoaded = true,
            };

            tracked[entity.EntityId] = pet;
            LogNotification(
                $"Tracking new pet id={pet.EntityId} creature='{entity.Code.Path}' owner='{pet.OwnerUid}' name='{pet.Name}' down={pet.WasDowned}"
            );
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
    }

    private void HandlePetDowned(TrackedPet pet)
    {
        pet.SavedColor ??= cfg.DefaultColor;
        LogTrace($"Pet downed id={pet.EntityId} name={pet.Name} owner={pet.OwnerId}");
        // TODO: notify clients to update waypoint color/pinned
    }

    private void HandlePetHealed(TrackedPet pet)
    {
        LogTrace($"Pet healed id={pet.EntityId} name={pet.Name} owner={pet.OwnerId}");
        // TODO: notify clients to restore waypoint color/pinned using pet.SavedColor/pet.SavedPinned
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
    public bool WasDowned;
    public bool IsLoaded;
    public int? SavedColor;
    public bool? SavedPinned;
}
