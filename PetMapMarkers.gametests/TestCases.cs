using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
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
}
