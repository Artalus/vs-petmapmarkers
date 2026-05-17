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
using VinTest;

namespace PetMapMarkers.GameTests;

[GameTestSuite]
public class PetMarkerTestCases(
    ICoreServerAPI sapi,
    IServerPlayer player,
    int stepMs,
    int chunkloadMs
)
{
    static class SavedEntities
    {
        internal const string Volchitsa = "Volchitsa";
        internal const string Pup = "Wolfdog (pup)";
        internal const string Dog = "NearbyHuntingDog";
        internal const string Corgi = "FarAwayCorgi";
    }

    readonly TestActions actions = new(sapi, player);

    [GameTest]
    public IEnumerable<TestStep> NearbyPetsAreTracked()
    {
        var chain = new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Wait(stepMs); // wait for initial tracker update
        foreach (
            var petName in new[] { SavedEntities.Pup, SavedEntities.Volchitsa, SavedEntities.Dog }
        )
            chain
                .Assert(
                    $"* {petName} should be tracked",
                    () => actions.Tracker.GetTrackedEntities().Any(e => e?.GetName() == petName)
                )
                .Assert(
                    $"* {petName} should have waypoint~~",
                    () => actions.GetWaypointFor(petName).Logged().Exists
                )
                .Assert(
                    "  ~~with green color",
                    () => actions.GetWaypointFor(petName).Color == 0x00FF00
                );
        return chain;
    }

    [GameTest]
    public IEnumerable<TestStep> FarawayPetGetsTrackedUponCloseup()
    {
        const string target = SavedEntities.Corgi;
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Assert(
                $"{target} is not loaded yet",
                () => actions.TryGetTrackedPetByName(target) == null
            )
            .Do(() => actions.TeleportPlayer(-1000, 10, 50))
            .Wait(chunkloadMs)
            .Assert($"{target} is loaded now", () => actions.GetTrackedPetByName(target) != null)
            .Assert(
                $"{target} should have waypoint after teleporting to it",
                () => actions.GetWaypointFor(target).Logged().Exists
            )
            .Do(() => actions.TeleportPlayer(0, 10, 0))
            .Wait(chunkloadMs)
            .Assert(
                $"{target} should still have waypoint after teleporting back",
                () => actions.GetWaypointFor(target).Exists
            );
    }

    [GameTest]
    public IEnumerable<TestStep> DownAndHealAffectWaypoints()
    {
        const string target = SavedEntities.Volchitsa;
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() => actions.DownPet(target))
            .Wait(stepMs)
            .Assert("pet should be downed", () => actions.IsDowned(target))
            .Assert(
                "downed waypoint should be pinned",
                () => actions.GetWaypointFor(target).Logged().Pinned == true
            )
            .Assert(
                "downed waypoint color should change to red",
                () => actions.GetWaypointFor(target).Color == 0xFF0000
            )
            .Do(() => actions.HealPet(target))
            .Wait(stepMs)
            .Assert("pet should be healed", () => !actions.IsDowned(target))
            .Assert(
                "healed waypoint color should restore to default",
                () => actions.GetWaypointFor(target).Color == 0x00FF00
            )
            .Assert(
                "healed waypoint should not be pinned",
                () => actions.GetWaypointFor(target).Waypoint!.Pinned == false
            );
    }

    [GameTest]
    public IEnumerable<TestStep> ChangedPinAndColorRestoreAfterHeal()
    {
        const string targetName = SavedEntities.Volchitsa;
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() =>
            {
                var wp = actions.GetWaypointFor(targetName).Waypoint;
                wp.Pinned = true;
                wp.Color = 0xFF00FF;
                actions.DownPet(targetName);
            })
            .Wait(stepMs)
            .Assert(
                "downed waypoint should stay pinned",
                () => actions.GetWaypointFor(targetName).Logged().Pinned == true
            )
            .Assert(
                "downed waypoint color should change to red",
                () => actions.GetWaypointFor(targetName).Color == 0xFF0000
            )
            .Do(() => actions.HealPet(targetName))
            .Wait(stepMs)
            .Assert("pet should be healed", () => !actions.IsDowned(targetName))
            .Assert(
                "healed waypoint should restore to overridden color",
                () => actions.GetWaypointFor(targetName).Logged().Color == 0xFF00FF
            )
            .Assert(
                "healed waypoint should stay pinned as manually overridden",
                () => actions.GetWaypointFor(targetName).Pinned == true
            );
    }

    [GameTest]
    public IEnumerable<TestStep> WaypointFollowsMovingPet()
    {
        const string targetName = SavedEntities.Dog;
        Entity target = null!;
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() =>
            {
                target = actions.GetTrackedPetByName(targetName);
                actions.Teleport(target, 0, 10, 0);
            })
            .Wait(stepMs)
            .Assert(
                "waypoint should follow pet near 0,0 after reset",
                () =>
                    actions
                        .GetWaypointFor(targetName)
                        .Logged()
                        .Waypoint.Position.AsBlockPos.IsNearbyXZ(actions.SApi, 0, 0)
            )
            .Do(() => actions.Teleport(target, 100, 10, 100))
            .Wait(stepMs)
            .Assert(
                "waypoint should follow pet near 100,100 after move",
                () =>
                    actions
                        .GetWaypointFor(targetName)
                        .Logged()
                        .Waypoint.Position.AsBlockPos.IsNearbyXZ(actions.SApi, 100, 100)
            );
    }

    [GameTest]
    public IEnumerable<TestStep> WaypointTracksPetName()
    {
        const string originalName = SavedEntities.Volchitsa;
        const string renamedName = "VolchitsaPrime";
        Entity target = null!;
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() =>
            {
                target = actions.GetTrackedPetByName(originalName);
                actions.Rename(target, renamedName);
            })
            .Wait(stepMs)
            .Assert(
                "waypoint title should follow renamed pet",
                () => actions.GetWaypointFor(target).Logged().Waypoint?.Title == renamedName
            )
            .Do(() => actions.GetWaypointFor(target).Waypoint!.Title = "meh")
            .Wait(stepMs)
            .Assert(
                "tracker should restore title from pet name",
                () => actions.GetWaypointFor(target).Logged().Waypoint?.Title == renamedName
            )
            .Do(() => target.GetBehavior<EntityBehaviorNameTag>()?.SetName(originalName));
    }

    [GameTest]
    public IEnumerable<TestStep> TamingAndAbandoningUpdateWaypoint()
    {
        const string wildName = "German shepherd (male)";
        Entity target = null!;
        return new TestChain()
            .EnsurePlayerAround(actions, x: 20, z: -100, wait: chunkloadMs)
            .Do(() => target = actions.GetEntityByName(wildName))
            .Assert(
                "wild pet has no waypoint",
                () => actions.GetWaypointFor(target).Logged().Missing
            )
            .Do(() => actions.TameFully(target, player.PlayerUID))
            .Wait(stepMs)
            .Assert(
                "waypoint should appear after tame",
                () => actions.GetWaypointFor(target).Logged().Exists
            )
            .Do(() => actions.Abandon(target))
            .Wait(stepMs)
            .Assert(
                "waypoint should disappear after abandon",
                () => actions.GetWaypointFor(target).Logged().Missing
            )
            .Do(() => actions.TamePartially(target, player.PlayerUID, progress: 0.25f))
            .Wait(stepMs)
            .Assert(
                "waypoint should reappear after retame partial",
                () => actions.GetWaypointFor(target).Logged().Exists
            )
            .Do(() => target.GetBehavior<EntityBehaviorTameable>()!.DomesticationProgress += 0.5f)
            .Wait(stepMs)
            .Assert(
                "waypoint should still exist after taming bump",
                () => actions.GetWaypointFor(target).Exists
            );
        // TODO: test gradual taming->domesticated progression?
        // would need to trigger EntityBehaviorTameable.OnInteract and reset cooldowns
    }

    [GameTest]
    public IEnumerable<TestStep> WaypointChangesOnTamingVariantSwap()
    {
        Entity wolf = null!;
        long oldEntityId = 0;

        const string target = "SwapTestWolf";
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() =>
            {
                wolf = actions.Spawn("wolf-eurasian-baby-male", 1, 5, 1, name: target);
                oldEntityId = wolf.EntityId;
            })
            .Wait(stepMs)
            .Do(() =>
            {
                sapi.LogTest("Trigger variant swap by magic bone");
                var magicboneItem =
                    sapi.World.GetItem(new AssetLocation("petai:magicbone"))
                    ?? throw new Exception("Failed to load magicbone item");
                var slot = new DummySlot(new ItemStack(magicboneItem));
                // magicbone forces spawnTameVariant(), see EntityBehaviorTameable.
                // This despawns wolfbaby (EnumDespawnReason.Expire) and spawns a wolftaming:dog-wolf-pup
                wolf.OnInteract(player.Entity, slot, Vec3d.Zero, EnumInteractMode.Interact);
            })
            .Wait(stepMs)
            .Assert(
                "tamed pet should have new EntityId after variant swap",
                () => actions.GetTrackedPetByName(target).EntityId != oldEntityId
            )
            .Assert(
                "waypoint should exist for new entity after variant swap",
                () => actions.GetWaypointFor(target).Logged().Exists
            )
            .Assert(
                "old waypoint should not exist after variant swap",
                () => actions.GetWaypointFor(oldEntityId, $"old {target} id").Logged().Missing
            );
    }

    [GameTest]
    public IEnumerable<TestStep> WatchingOnlyForTameableEntities()
    {
        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Assert(
                "all tameable entities should have a watcher",
                () =>
                {
                    var watchedIds = actions.GetWatchedEntityIds();
                    var tameableLoaded = sapi
                        .World.LoadedEntities.Values.Where(e =>
                            e.HasBehavior<EntityBehaviorTameable>()
                        )
                        .ToList();
                    sapi.LogTest($"Tameable: {tameableLoaded.Count}, watched: {watchedIds.Count}");
                    return tameableLoaded.Count > 0
                        && tameableLoaded.All(e => watchedIds.Contains(e.EntityId));
                }
            )
            .Assert(
                "player should not have a watcher",
                () => !actions.GetWatchedEntityIds().Contains(player.Entity.EntityId)
            );
    }

    [GameTest]
    public IEnumerable<TestStep> WatchersRegisteredOnlyOnce()
    {
        Entity wolf = null!;
        long wolfId = 0;
        const string petName = "WatcherTestWolf";
        bool WolfLoaded() => sapi.World.LoadedEntities.ContainsKey(wolfId);

        // By default entities/chunks seem to take about ~15 seconds to despawn.
        // With ExpediteChunkUnload the chunk should age out within couple ~3s cycles.
        // Allow 10s to be safe.
        int despawnDelayMs = 10_000;

        // Ensure wolf spawns in the middle of the chunk to ensure unloading.
        int faraway = sapi.WorldManager.ChunkSize * 20 + 2;
        var bp = new BlockPos(faraway, 5, faraway).ToGlobalPosition(sapi);
        int chunkX = bp.X / sapi.WorldManager.ChunkSize;
        int chunkZ = bp.Z / sapi.WorldManager.ChunkSize;

        return new TestChain()
            .EnsurePlayerAround(actions, x: faraway, z: faraway, wait: chunkloadMs)
            .Do(() =>
            {
                wolf = actions.Spawn("wolf-eurasian-baby-male", faraway, 5, faraway, name: petName);
                wolfId = wolf.EntityId;
            })
            .Wait(stepMs)
            .Assert(
                "freshly spawned wild wolf should immediately have a watcher",
                () => actions.GetWatchedEntityIds().Contains(wolfId)
            )
            // reload the wolf: teleport away so the chunk unloads, then back.
            .Do(() => actions.TeleportPlayer(0, 10, 0))
            // this magic speeds test up from ~20 to ~8s
            .Do(() => actions.ExpediteChunkUnload(chunkX, chunkZ))
            .AssertEventually(
                "wolf despawns after teleporting away",
                maxMs: despawnDelayMs,
                breakWhen: () => !WolfLoaded()
            )
            // Wait for the chunk thread to flush dirty unloaded chunks to DB, before
            // triggering reload. ServerSystemUnloadChunks.SaveDirtyUnloadedChunks runs
            // every 200ms on the chunk thread.
            .Wait(500)
            .EnsurePlayerAround(actions, x: faraway, z: faraway, wait: chunkloadMs)
            .AssertEventually(
                "wolf loads after teleporting back",
                maxMs: chunkloadMs,
                breakWhen: WolfLoaded
            )
            // Tame and then abandon to verify the watcher only fires once per modification.
            // If the watcher was double-registered on reload, Abandon() provokes two watcher calls:
            // - first removes from tracked (correct), second adds entity back to tameCandidates,
            // with an empty owner - where it gets stuck forever since TryAddPetFromEntity
            // rejects empty owners.
            .Do(() =>
            {
                wolf =
                    sapi.World.LoadedEntities.Get(wolfId)
                    ?? throw new Exception($"Wolf {wolfId} not found after reload");
                actions.TamePartially(wolf, player.PlayerUID, 0.5f);
            })
            .Wait(stepMs)
            .Do(() => actions.Abandon(wolf))
            .Wait(stepMs)
            .Assert(
                "abandoned pet should be removed from tameCandidates",
                () =>
                {
                    var candidates = actions.GetTameCandidateIds();
                    bool stuck = candidates.Contains(wolfId);
                    return !stuck;
                }
            );
    }
}
