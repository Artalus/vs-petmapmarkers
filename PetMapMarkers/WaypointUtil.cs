using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PetMapMarkers;

public static class WaypointUtil
{
    // TODO: grab a mapSink from somewhere, and SendMapDataToClient with pet waypoints only?
    private static readonly MethodInfo resendMethod =
        typeof(WaypointMapLayer).GetMethod(
            "ResendWaypoints",
            BindingFlags.NonPublic | BindingFlags.Instance
        ) ?? throw new Exception("Could not access WaypointMapLayer.ResendWaypoints method");

    public static WaypointMapLayer GetWaypointLayer(ICoreServerAPI sapi)
    {
        return sapi.ModLoader.GetModSystem<WorldMapManager>()
                .MapLayers.FirstOrDefault(ml => ml is WaypointMapLayer) as WaypointMapLayer
            ?? throw new Exception("Could not find WaypointMapLayer");
    }

    public static Waypoint? FindWaypointByGuid(WaypointMapLayer layer, string guid)
    {
        return layer.Waypoints.FirstOrDefault(wp => wp.Guid == guid);
    }

    public static void ResendWaypoints(WaypointMapLayer layer, IServerPlayer player)
    {
        resendMethod.Invoke(layer, [player]);
    }

    public static void ResendWaypointsToAll(
        WaypointMapLayer layer,
        ICoreServerAPI sapi,
        HashSet<string> playerUIDs
    )
    {
        foreach (var uid in playerUIDs)
        {
            if (sapi.World.PlayerByUid(uid) is IServerPlayer p)
                ResendWaypoints(layer, p);
        }
    }
}
