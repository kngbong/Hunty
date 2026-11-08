using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Hunty;

/// <summary>
/// Places map flags on every known spawn location of the hunting log monsters that are still
/// pending for the classes selected in the alert window.
/// </summary>
/// <remarks>
/// The markers are added from inside the game's own map marker reset functions. That way they are
/// recreated automatically whenever the map is opened, the map is changed or the game rebuilds its
/// markers, and they can never duplicate the vanilla markers.
/// </remarks>
public sealed unsafe class HuntingMapMarkers : IDisposable
{
    private const uint FlagIcon = 60561;
    private const long RefreshIntervalMs = 500;

    private readonly Plugin Plugin;
    private readonly HashSet<uint> TrackedJobs = [];

    private Hook<AgentMap.Delegates.ResetMapMarkers>? MapMarkerHook;
    private Hook<AgentMap.Delegates.ResetMiniMapMarkers>? MiniMapMarkerHook;

    private List<Vector3> CurrentPositions = [];
    private uint CurrentTerritory;
    private string LastSignature = string.Empty;
    private long NextRefresh;
    private bool ForceRefresh;

    public HuntingMapMarkers(Plugin plugin)
    {
        Plugin = plugin;

        Plugin.Framework.Update += OnFrameworkUpdate;

        try
        {
            MapMarkerHook = Plugin.Interop.HookFromAddress<AgentMap.Delegates.ResetMapMarkers>(
                AgentMap.Addresses.ResetMapMarkers.Value,
                ResetMapMarkersDetour);
            MapMarkerHook.Enable();

            MiniMapMarkerHook = Plugin.Interop.HookFromAddress<AgentMap.Delegates.ResetMiniMapMarkers>(
                AgentMap.Addresses.ResetMiniMapMarkers.Value,
                ResetMiniMapMarkersDetour);
            MiniMapMarkerHook.Enable();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to hook the map marker functions; hunting log map markers are unavailable.");
        }
    }

    public bool IsTracked(uint job) => TrackedJobs.Contains(job);

    /// <summary>Toggles map marker tracking for the given class.</summary>
    public void Toggle(uint job)
    {
        if (!TrackedJobs.Remove(job))
            TrackedJobs.Add(job);

        ForceRefresh = true;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;

        CurrentPositions = [];
        CurrentTerritory = 0;
        LastSignature = string.Empty;

        MapMarkerHook?.Dispose();
        MapMarkerHook = null;
        MiniMapMarkerHook?.Dispose();
        MiniMapMarkerHook = null;

        // Remove our markers and rebuild the vanilla ones if the map is currently visible.
        try
        {
            var agentMap = AgentMap.Instance();
            if (agentMap == null || Plugin.GameGui.GetAddonByName("AreaMap", 1).IsNull)
                return;

            agentMap->ResetMapMarkers();
            agentMap->ResetMiniMapMarkers();
            agentMap->CreateMapMarkers(false);
            agentMap->CreateMiniMapMarkers(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to restore the vanilla map markers.");
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!Plugin.PlayerState.IsLoaded)
        {
            if (CurrentPositions.Count > 0 || LastSignature.Length > 0)
            {
                CurrentPositions = [];
                CurrentTerritory = 0;
                LastSignature = string.Empty;
                ResetMapMarkers();
            }

            return;
        }

        var now = Environment.TickCount64;
        if (!ForceRefresh && now < NextRefresh)
            return;

        NextRefresh = now + RefreshIntervalMs;
        ForceRefresh = false;

        var territory = Plugin.ClientState.TerritoryType;
        var positions = CollectPositions(territory);
        var signature = BuildSignature(territory, positions);

        if (signature == LastSignature)
            return;

        LastSignature = signature;
        CurrentTerritory = territory;
        CurrentPositions = positions;

        ResetMapMarkers();
    }

    private void ResetMapMarkers()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
            return;

        try
        {
            agentMap->ResetMapMarkers();
            agentMap->ResetMiniMapMarkers();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to refresh hunting log map markers.");
        }
    }

    private void ResetMapMarkersDetour(AgentMap* agentMap)
    {
        MapMarkerHook!.Original(agentMap);

        if (CurrentPositions.Count == 0 || CurrentTerritory != Plugin.ClientState.TerritoryType)
            return;

        // Never draw the current zone's monsters on a map the player opened from somewhere else.
        var mapId = Plugin.ClientState.MapId;
        if (mapId == 0 || agentMap->SelectedMapId != mapId)
            return;

        foreach (var position in CurrentPositions)
            agentMap->AddMapMarker(position, FlagIcon);
    }

    private void ResetMiniMapMarkersDetour(AgentMap* agentMap)
    {
        MiniMapMarkerHook!.Original(agentMap);

        if (CurrentPositions.Count == 0 || CurrentTerritory != Plugin.ClientState.TerritoryType)
            return;

        foreach (var position in CurrentPositions)
            agentMap->AddMiniMapMarker(position, FlagIcon);
    }

    private List<Vector3> CollectPositions(uint territory)
    {
        var positions = new List<Vector3>();
        var seen = new HashSet<(int X, int Z)>();

        if (territory == 0)
            return positions;

        foreach (var job in TrackedJobs.OrderBy(job => job))
        {
            foreach (var pending in Plugin.GetPendingMonsters(job, territory))
            {
                foreach (var location in pending.Monster.Locations)
                {
                    // Only open world locations can be placed on the currently displayed map.
                    if (location.Terri != territory || location.Map == 0)
                        continue;

                    var worldPosition = GetWorldPosition(location);
                    if (worldPosition == null)
                        continue;

                    var key = ((int)MathF.Round(worldPosition.Value.X), (int)MathF.Round(worldPosition.Value.Z));
                    if (!seen.Add(key))
                        continue;

                    positions.Add(worldPosition.Value);
                }
            }
        }

        return positions;
    }

    private static Vector3? GetWorldPosition(HuntingMonsterLocation location)
    {
        try
        {
            var mapLink = new MapLinkPayload(location.Terri, location.Map, location.xCoord, location.yCoord);
            return new Vector3(mapLink.RawX / 1000f, 0f, mapLink.RawY / 1000f);
        }
        catch
        {
            return null;
        }
    }

    private string BuildSignature(uint territory, List<Vector3> positions)
    {
        if (positions.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append(territory).Append('|');

        foreach (var job in TrackedJobs.OrderBy(job => job))
            builder.Append(job).Append(',');

        foreach (var position in positions.OrderBy(p => p.X).ThenBy(p => p.Z))
        {
            builder.Append((int)MathF.Round(position.X))
                   .Append(':')
                   .Append((int)MathF.Round(position.Z))
                   .Append(';');
        }

        return builder.ToString();
    }
}
