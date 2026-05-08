using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Server;
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
    readonly TestActions actions = new(sapi, player);

    [GameTest]
    public IEnumerable<TestStep> NearbyPetsAreTracked()
    {
        var chain = new TestChain()
            .EnsurePlayerAround(actions, x: 0, z: 0, wait: chunkloadMs)
            .Wait(stepMs); // wait for initial tracker update
        foreach (var petName in new[] { "Wolfdog (pup)", "Volchitsa", "NearbyHuntingDog" })
            chain.Assert(
                $"* {petName} should be tracked",
                () => actions.Tracker.GetTrackedEntities().Any(e => e?.GetName() == petName)
            );
        return chain;
    }
}
