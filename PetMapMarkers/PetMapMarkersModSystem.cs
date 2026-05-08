using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PetMapMarkers;

public class PetMapMarkersModSystem : ModSystem
{
    public PetTracker Tracker
    {
        get
        {
            if (_tracker == null)
                throw new InvalidOperationException(
                    "Tracker accessed before modsystem initialization"
                );
            return _tracker;
        }
        private set => _tracker = value;
    }
    private PetTracker? _tracker;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api) { }

    public override void StartServerSide(ICoreServerAPI api)
    {
        // TODO: wrap into trycatch as the doc suggests
        // TODO: dump default mod config if file does not exist
        var cfg = api.LoadModConfig<ModConfig>("petmapmarkersconfig.json") ?? new ModConfig();
        Tracker = new PetTracker(api, cfg);
    }

    public override void StartClientSide(ICoreClientAPI api) { }
}
