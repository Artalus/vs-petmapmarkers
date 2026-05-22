using Vintagestory.API.Server;
using VinTest;

namespace PetMapMarkers.GameTests;

public class AutotestModSystem : GametestModsystemBase
{
    private const int ChunkLoadMs = 2000;

    protected override int StartupDelayMs => ChunkLoadMs;

    protected override object[] CreateSuites(IServerPlayer player)
    {
        int stepMs = SApi.ModLoader.GetModSystem<PetMapMarkersModSystem>().Tracker.IntervalMs * 2;
        return [new PetMarkerTestCases(SApi, player, stepMs, ChunkLoadMs)];
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        // disable AI tasks and activity system for the test world so freshly spawned
        // NPCs remain idle during tests
        api.World.Config.SetBool("runAiTasks", false);
        api.World.Config.SetBool("runAiActivities", false);
    }
}
