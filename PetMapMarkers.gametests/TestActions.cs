using System;
using System.Collections.Generic;
using System.Linq;
using PetAI;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PetMapMarkers.GameTests;

internal class TestActions(ICoreServerAPI sapi, IServerPlayer player)
{
    internal PetTracker Tracker => sapi.ModLoader.GetModSystem<PetMapMarkersModSystem>().Tracker;
    internal IServerPlayer Player => player;
    internal ICoreServerAPI SApi => sapi;

    internal Entity? TryGetTrackedPetByName(string name)
    {
        return Tracker.GetTrackedEntities().FirstOrDefault(e => e.GetName() == name);
    }

    internal Entity GetTrackedPetByName(string name)
    {
        return TryGetTrackedPetByName(name) ?? throw new Exception($"Pet '{name}' not found");
    }

    internal Entity? TryGetEntityByName(string name)
    {
        return sapi.World.LoadedEntities.Values.FirstOrDefault(e => e.GetName() == name);
    }

    internal Entity GetEntityByName(string name)
    {
        return TryGetEntityByName(name) ?? throw new Exception($"Entity '{name}' not found");
    }

    public IReadOnlySet<long> GetWatchedEntityIds()
    {
        return Tracker.WatchedIds;
    }

    public IReadOnlySet<long> GetTameCandidateIds()
    {
        return Tracker.TameCandidateIds;
    }

    internal void Rename(Entity entity, string newName)
    {
        var nameTag = entity.GetBehavior<EntityBehaviorNameTag>();
        if (nameTag == null)
        {
            // TODO: is this enough? client still shows old name...
            sapi.LogTest($"No NameTag for {entity.LogTitle()}, adding one");
            nameTag = new EntityBehaviorNameTag(entity);
            entity.AddBehavior(nameTag);
        }
        nameTag.SetName(newName);
    }

    internal void DownPet(string name)
    {
        sapi.LogTest($"Attempt to down pet '{name}'");
        var entity = GetTrackedPetByName(name);
        var src = new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Injury,
        };
        entity.ReceiveDamage(src, 9999f);
    }

    internal void HealPet(string name)
    {
        sapi.LogTest($"Attempt to heal pet '{name}'");
        var entity = GetTrackedPetByName(name);
        entity.Revive();
    }

    internal bool IsDowned(string name)
    {
        sapi.LogTest($"Verify if pet '{name}' is downed");
        var entity = GetTrackedPetByName(name);
        return PetTracker.IsDowned(entity);
    }

    internal void TamePartially(Entity entity, string ownerUid, float progress)
    {
        sapi.LogTest(
            $"Partially taming pet '{entity.LogTitle()}' to owner {ownerUid} with progress {progress:P0}"
        );
        var tameable =
            entity.GetBehavior<EntityBehaviorTameable>()
            ?? throw new Exception($"Entity {entity.LogTitle()} is not tameable");
        tameable.DomesticationLevel = DomesticationLevel.TAMING;
        tameable.OwnerId = ownerUid;
        tameable.DomesticationProgress = progress;
    }

    internal void TameFully(Entity entity, string ownerUid)
    {
        sapi.LogTest($"Fully taming pet '{entity.LogTitle()}' to owner {ownerUid}");
        var tameable =
            entity.GetBehavior<EntityBehaviorTameable>()
            ?? throw new Exception($"Entity {entity.LogTitle()} is not tameable");
        tameable.DomesticationLevel = DomesticationLevel.DOMESTICATED;
        tameable.OwnerId = ownerUid;
        tameable.Obedience = 1f;
    }

    internal void Abandon(Entity entity)
    {
        sapi.LogTest($"Abandoning pet '{entity.LogTitle()}'");
        var tameable =
            entity.GetBehavior<EntityBehaviorTameable>()
            ?? throw new Exception($"Entity {entity.LogTitle()} is not tameable");
        tameable.DomesticationLevel = DomesticationLevel.WILD;
        tameable.OwnerId = null;
        tameable.DomesticationProgress = 0f;
        tameable.Obedience = 0f;
    }

    internal Entity Spawn(string code, int x, int y, int z, string? name = null)
    {
        sapi.LogTest($"Spawning entity '{code}' at {x} {y} {z} with name '{name}'");
        var entityType =
            sapi.World.GetEntityType(new AssetLocation(code))
            ?? throw new Exception($"Failed to load entity type for code '{code}'");
        var entity =
            sapi.World.ClassRegistry.CreateEntity(entityType)
            ?? throw new Exception($"Failed to create entity for code '{code}'");
        entity.Pos.SetPos(new BlockPos(x, y, z).ToGlobalPosition(sapi));
        sapi.World.SpawnEntity(entity);
        if (name != null)
            Rename(entity, name);
        sapi.LogTest($"Spawned entity {entity.LogTitle()}");
        return entity;
    }

    private Waypoint? FindWaypoint(long entityId)
    {
        var layer =
            WaypointUtil.GetWaypointLayer(sapi)
            ?? throw new Exception("Waypoint layer not available");
        return WaypointUtil.FindWaypointByGuid(layer, PetTracker.WaypointUidFor(entityId));
    }

    internal WaypointForEntity GetWaypointFor(Entity entity)
    {
        var wp = FindWaypoint(entity.EntityId);
        return new WaypointForEntity(sapi, entity.GetName(), wp);
    }

    internal WaypointForEntity GetWaypointFor(long entityId, string name)
    {
        var wp = FindWaypoint(entityId);
        return new WaypointForEntity(sapi, name, wp);
    }

    internal WaypointForEntity GetWaypointFor(string name)
    {
        var entity = TryGetEntityByName(name);
        return entity != null ? GetWaypointFor(entity) : new WaypointForEntity(sapi, name, null);
    }

    internal void Teleport(Entity entity, int x, int y, int z)
    {
        sapi.LogTest($"Teleporting {entity.LogTitle()} to {x} {y} {z}");
        entity.TeleportTo(new BlockPos(x, y, z).ToGlobalPosition(sapi));
    }

    internal void TeleportPlayer(int x, int y, int z)
    {
        Teleport(player.Entity, x, y, z);
    }

    /// <summary>
    /// Mark chunk to be naturally unloaded (preserving dirty chunk data like entities to disk,
    /// before despawning them).
    /// Do NOT use WorldManager.UnloadChunkColumn() - it bypasses the save and disposes the chunk
    /// in-memory without writing entities to disk.
    /// </summary>
    internal void ExpediteChunkUnload(int chunkX, int chunkZ)
    {
        // See VintagestoryLib\Vintagestory.Server\ServerSystemUnloadChunks.cs.
        var i =
            sapi.WorldManager.GetMapChunk(chunkX, chunkZ)
            ?? throw new Exception($"Map chunk ({chunkX},{chunkZ}) not found");
        var mapChunk =
            i as Vintagestory.Server.ServerMapChunk
            ?? throw new Exception($"Map chunk ({chunkX},{chunkZ}) is not a ServerMapChunk");
        // When <=1, IsOld() returns true for the sake of purposes in
        // ServerSystemUnloadChunks.FindUnloadableChunkColumnCandidates()
        mapChunk.UnloadGeneration = 0;
        sapi.LogTest(
            $"Expedited chunk unload for ({chunkX},{chunkZ}), starting recurring listener"
        );
        /// A single one-shot set is unreliable.
        /// 1. ServerSystemBlockSimulation.OnSeparateThreadTick runs on the chunk thread and resets
        /// UnloadGeneration to 5 via MapChunk.MarkFresh().
        /// 2. InChunkIndex3d (used by BlockSimulation) lags behind ServerPos by one main-thread tick.
        /// => There is a window where chunk thread keeps marking  fresh even if player is away.
        long handle = 0;
        handle = sapi.Event.RegisterGameTickListener(
            _ =>
            {
                if (sapi.WorldManager.GetMapChunk(chunkX, chunkZ) == null)
                {
                    sapi.Event.UnregisterGameTickListener(handle);
                    sapi.LogTest($"Chunk ({chunkX},{chunkZ}) unloaded, stopping expedite listener");
                    return;
                }
                mapChunk.UnloadGeneration = 0;
            },
            200
        );
    }

    internal class WaypointForEntity(ICoreServerAPI sapi, string name, Waypoint? waypoint)
    {
        public Waypoint Waypoint => waypoint ?? throw new Exception("waypoint does not exist");
        public bool Exists => waypoint != null;
        public bool Missing => waypoint == null;
        public int Color => Waypoint.Color & 0x00FFFFFF;
        public bool Pinned => Waypoint.Pinned;

        public WaypointForEntity Logged()
        {
            if (waypoint == null)
            {
                sapi.LogTest($"No waypoint for {name}");
                return this;
            }
            Vec3i pos = waypoint.Position.AsBlockPos.ToLocalPosition(sapi);
            sapi.LogTest(
                $"Waypoint for {name}: title='{waypoint.Title}' XZ={pos.X},{pos.Z} guid={waypoint.Guid} color=#{Color:X6} pinned={waypoint.Pinned}"
            );
            return this;
        }
    }
}
