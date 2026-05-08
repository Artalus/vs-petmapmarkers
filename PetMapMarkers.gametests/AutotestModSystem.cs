using Vintagestory.API.Server;
using VinTest;

namespace PetMapMarkers.GameTests;

public class AutotestModSystem : GametestModsystemBase
{
    private const int ChunkLoadMs = 2000;

    protected override int StartupDelayMs => ChunkLoadMs;

    protected override object[] CreateSuites(IServerPlayer player)
    {
        int stepMs = 200;
        return [new PetMarkerTestCases(SApi, player, stepMs, ChunkLoadMs)];
    }
}
