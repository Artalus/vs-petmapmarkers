using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VinTest;

namespace PetMapMarkers.GameTests;

internal static class BlockPosExtensions
{
    /// <summary>
    /// Turn coords relative to the world spawn into global coords with spawn offset.
    /// </summary>
    internal static BlockPos ToGlobalPosition(this BlockPos local, ICoreAPI api)
    {
        return new BlockPos(
            local.X + (int)api.World.DefaultSpawnPosition.X,
            local.Y,
            local.Z + (int)api.World.DefaultSpawnPosition.Z
        );
    }

    /// <summary>
    /// Fuzzy-check that something is nearby something else.
    /// </summary>
    internal static bool IsNearbyXZ(
        this BlockPos pos,
        ICoreServerAPI sapi,
        int x,
        int z,
        int tolerance = 5
    )
    {
        var vec = pos.ToLocalPosition(sapi);
        return Math.Abs(vec.X - x) <= tolerance && Math.Abs(vec.Z - z) <= tolerance;
    }
}

internal static class TestChainExtensions
{
    /// <summary>
    /// Add chain step to teleport player to (x,z) and wait for chunk load,
    /// but only if player is not already nearby.
    /// </summary>
    internal static TestChain EnsurePlayerAround(
        this TestChain chain,
        TestActions actions,
        int x,
        int z,
        int wait
    )
    {
        bool away = false;
        chain
            .Do(() =>
            {
                away = !actions.Player.Entity.Pos.AsBlockPos.IsNearbyXZ(
                    actions.SApi,
                    x,
                    z,
                    tolerance: 30
                );
                if (away)
                    actions.TeleportPlayer(x, 10, z);
            })
            .WaitIf(wait, () => away);
        return chain;
    }
}
