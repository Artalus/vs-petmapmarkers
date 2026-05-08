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

    internal void Teleport(Entity entity, int x, int y, int z)
    {
        entity.TeleportTo(new BlockPos(x, y, z).ToGlobalPosition(sapi));
    }

    internal void TeleportPlayer(int x, int y, int z)
    {
        Teleport(player.Entity, x, y, z);
    }
}
