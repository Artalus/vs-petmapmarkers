using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VinTest;

namespace PetMapMarkers.GameTests;

public class AutotestModSystem : GametestModsystemBase
{
    private const int ChunkLoadMs = 2000;

    protected override int StartupDelayMs => ChunkLoadMs;

    protected override object[] CreateSuites(IServerPlayer player)
    {
        int stepMs = SApi.ModLoader.GetModSystem<PetMapMarkersModSystem>().Tracker.IntervalMs * 2;
        return
        [
            new PetMarkerTestCases(SApi, player, stepMs, ChunkLoadMs),
            new AnimalCagesCompatTestCases(SApi, player, stepMs, ChunkLoadMs),
        ];
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        // disable AI tasks and activity system for the test world so freshly spawned
        // NPCs remain idle during tests
        api.World.Config.SetBool("runAiTasks", false);
        api.World.Config.SetBool("runAiActivities", false);

        api.Event.PlayerJoin += OnPlayerJoin;
    }

    // TODO: this should be in VinTest!
    private void OnPlayerJoin(IServerPlayer player)
    {
        player.SetModData("createCharacter", true);
        // Inform client immediately to avoid waiting on character dialog
        SApi.Network.GetChannel("charselection")
            .SendPacket(new CharacterSelectedState { DidSelect = true }, [player]);
        // Push updated playerdata to client
        player.BroadcastPlayerData(true);
    }

    protected override void OnManualModeReady(IServerPlayer player)
    {
        // register init command to create all the entities from testcases
        SApi.ChatCommands.GetOrCreate("init")
            .WithDescription("Initialize test entities")
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(args =>
            {
                new PetMarkerTestCases(SApi, player, 0, ChunkLoadMs).InitTestEntities();
                return TextCommandResult.Success("Test entities initialized");
            });
        var parsers = SApi.ChatCommands.Parsers;

        SApi.ChatCommands.GetOrCreate("rename")
            .WithDescription("Rename whatever player looks at")
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(parsers.Word("newName"))
            .HandleWith(args =>
            {
                IServerPlayer player = (IServerPlayer)args.Caller.Player;
                if (player.CurrentEntitySelection is not { Entity: { } entity })
                    return TextCommandResult.Error("Not looking at any entity");
                TestActions actions = new(SApi, player);
                var to = (string)args[0];
                actions.Rename(entity, to);
                return TextCommandResult.Success($"Renamed entity to {to}");
            });
    }
}
