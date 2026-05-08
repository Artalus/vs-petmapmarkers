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
}
