using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VinTest;

namespace PetMapMarkers.GameTests;

[GameTestSuite]
public class AnimalCagesCompatTestCases(
    ICoreServerAPI sapi,
    IServerPlayer player,
    int stepMs,
    int chunkloadMs
)
{
    readonly TestActions actions = new(sapi, player);

    [GameTest]
    public IEnumerable<TestStep> CagingAndReleasingUpdateWaypoint()
    {
        Entity pet = null!;
        long oldEntityId = 0;
        ItemStack cage = null!;
        const string PetName = "CageTestPup";

        return new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() =>
            {
                pet = actions.Spawn("wolftaming:dog-wolf-pup", 2, 5, 2, name: PetName);
                actions.TameFully(pet, player.PlayerUID);
                oldEntityId = pet.EntityId;
            })
            .Wait(stepMs)
            .Do(() => actions.GetWaypointFor(PetName).Waypoint.Color = unchecked((int)0xFF112233))
            .Wait(stepMs)
            .Do(() =>
            {
                cage = actions.CagePet(pet);
                pet = null!;
            })
            .Wait(stepMs)
            .Assert(
                "entity no longer loaded after caging",
                () => actions.TryGetEntityByName(PetName) == null
            )
            .Assert(
                "pet is no longer tracked after caging",
                () => actions.TryGetTrackedPetByName(PetName) == null
            )
            .Assert(
                "waypoint still exists while caged",
                () => actions.GetWaypointFor(oldEntityId, $"caged {PetName}").Logged().Exists
            )
            .Assert(
                "caged waypoint color is black",
                () => actions.GetWaypointFor(oldEntityId, $"caged {PetName}").Color == 0x000000
            )
            .Do(() => actions.ReleasePetFromCage(cage, 0, 5, 0))
            .Wait(stepMs)
            .Assert(
                "pet is tracked again after release",
                () => actions.TryGetTrackedPetByName(PetName) != null
            )
            .Assert(
                "released pet has a new entity id",
                () => actions.GetTrackedPetByName(PetName).EntityId != oldEntityId
            )
            .Assert(
                "old (black) waypoint is gone after release",
                () => actions.GetWaypointFor(oldEntityId, $"old {PetName}").Logged().Missing
            )
            .Assert(
                "new waypoint exists after release",
                () => actions.GetWaypointFor(PetName).Logged().Exists
            )
            .Assert(
                "released waypoint color restored",
                () => actions.GetWaypointFor(PetName).Color == 0x112233
            );
    }

    [GameTest]
    public IEnumerable<TestStep> CagingTwoIdenticalPetsPreservesColors_12()
    {
        var t = new TwoPetCageChain(actions, player, stepMs, chunkloadMs);
        // cage order 1→2, release order 1→2
        return t.BuildSetupAndCage()
            .Do(() => t.Release(t.first.cage))
            .Wait(stepMs)
            .Do(() =>
            {
                t.first.releasedId = actions
                    .Tracker.GetTrackedEntities()
                    .Select(e => e.EntityId)
                    .First(i => !t.preexistingTrackedIds.Contains(i));
                sapi.LogTest($"first.releasedId={t.first.releasedId}");
            })
            .Assert(
                "old first.pet waypoint gone",
                () => actions.GetWaypointFor(t.first.id, "old first.pet").Logged().Missing
            )
            .Assert(
                "released first.pet restores first.color",
                () =>
                    actions.GetWaypointFor(t.first.releasedId, "released first.pet").Logged().Color
                    == t.first.color
            )
            .Do(() => t.Release(t.second.cage))
            .Wait(stepMs)
            .Do(() =>
            {
                t.second.releasedId = actions
                    .Tracker.GetTrackedEntities()
                    .Select(e => e.EntityId)
                    .First(i => !t.preexistingTrackedIds.Contains(i) && i != t.first.releasedId);
                sapi.LogTest($"second.releasedId={t.second.releasedId}");
            })
            .Assert(
                "old second.pet waypoint gone",
                () => actions.GetWaypointFor(t.second.id, "old second.pet").Logged().Missing
            )
            .Assert(
                "released second.pet restores second.color",
                () =>
                    actions
                        .GetWaypointFor(t.second.releasedId, "released second.pet")
                        .Logged()
                        .Color == t.second.color
            );
    }

    [GameTest]
    public IEnumerable<TestStep> CagingTwoIdenticalPetsPreservesColors_21()
    {
        var t = new TwoPetCageChain(actions, player, stepMs, chunkloadMs);
        // cage order 1→2, release order 2→1
        return t.BuildSetupAndCage()
            .Do(() => t.Release(t.second.cage))
            .Wait(stepMs)
            .Do(() =>
            {
                t.second.releasedId = actions
                    .Tracker.GetTrackedEntities()
                    .Select(e => e.EntityId)
                    .First(i => !t.preexistingTrackedIds.Contains(i));
                sapi.LogTest($"second.releasedId={t.second.releasedId}");
            })
            .Assert(
                "old second.pet waypoint gone",
                () => actions.GetWaypointFor(t.second.id, "old second.pet").Logged().Missing
            )
            .Assert(
                "opening second.cage first dequeues second.color",
                () =>
                    actions
                        .GetWaypointFor(t.second.releasedId, "released second.pet")
                        .Logged()
                        .Color == t.second.color
            )
            .Do(() => t.Release(t.first.cage))
            .Wait(stepMs)
            .Do(() =>
            {
                t.first.releasedId = actions
                    .Tracker.GetTrackedEntities()
                    .Select(e => e.EntityId)
                    .First(i => !t.preexistingTrackedIds.Contains(i) && i != t.second.releasedId);
                sapi.LogTest($"first.releasedId={t.first.releasedId}");
            })
            .Assert(
                "old first.pet waypoint gone",
                () => actions.GetWaypointFor(t.first.id, "old first.pet").Logged().Missing
            )
            .Assert(
                "opening first.cage second dequeues first.color",
                () =>
                    actions.GetWaypointFor(t.first.releasedId, "released first.pet").Logged().Color
                    == t.first.color
            );
    }
}

file class TwoPetCageChain(TestActions actions, IServerPlayer player, int stepMs, int chunkloadMs)
{
    public readonly CageTestSet first = new() { color = 0x112233 };
    public readonly CageTestSet second = new() { color = 0xAABBCC };
    public HashSet<long> preexistingTrackedIds = null!;

    void Spawn(CageTestSet set)
    {
        // no name override - both get the same default species name
        set.pet = actions.Spawn("wolftaming:dog-wolf-pup", 0, 5, 0);
        actions.TameFully(set.pet, player.PlayerUID);
        set.id = set.pet.EntityId;
    }

    public void Release(ItemStack cage)
    {
        actions.ReleasePetFromCage(cage, 0, 5, 0);
    }

    public TestChain BuildSetupAndCage() =>
        new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Do(() =>
            {
                preexistingTrackedIds =
                [
                    .. actions.Tracker.GetTrackedEntities().Select(e => e.EntityId),
                ];
                Spawn(first);
                Spawn(second);
            })
            .Wait(stepMs)
            .Do(() =>
            {
                actions.GetWaypointFor(first.pet).Waypoint.Color = first.color;
                actions.GetWaypointFor(second.pet).Waypoint.Color = second.color;
            })
            // cage in order: 1 then 2
            .Do(() => first.cage = actions.CagePet(first.pet))
            .Do(() => second.cage = actions.CagePet(second.pet))
            .Wait(stepMs)
            .Assert(
                "first.pet waypoint is black",
                () => actions.GetWaypointFor(first.id, "caged first.pet").Logged().Color == 0x000000
            )
            .Assert(
                "second.pet waypoint is black",
                () =>
                    actions.GetWaypointFor(second.id, "caged second.pet").Logged().Color == 0x000000
            );
}

file class CageTestSet
{
    public Entity pet = null!;
    public long id = 0;
    public ItemStack cage = null!;
    public int color;
    public long releasedId = 0;
}
