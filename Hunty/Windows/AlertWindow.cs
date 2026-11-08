using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Hunty.Resources;

namespace Hunty.Windows;

/// <summary>
/// Small window showing how many hunting log entries are still pending in the zone the player
/// is currently in, for the classes selected in the configuration.
/// </summary>
public class AlertWindow : Window, IDisposable
{
    private const long RefreshIntervalMs = 1000;

    private static readonly Dictionary<uint, uint> JobIcons = new()
    {
        { 1, 62001 },     /* GLA/PAL */
        { 2, 62002 },     /* PGL/MNK */
        { 3, 62003 },     /* MRD/WAR */
        { 4, 62004 },     /* LAN/DRG */
        { 5, 62005 },     /* ARC/BRD */
        { 6, 62006 },     /* CON/WHM */
        { 7, 62007 },     /* THM/BLM */
        { 26, 62026 },    /* ARC/SMN/SCH */
        { 29, 62029 },    /* ROG/NIN */
    };

    private readonly Plugin Plugin;

    private readonly List<JobAlert> Alerts = new();
    private long NextRefresh;
    private uint CachedTerritory;
    private string CachedJobs = string.Empty;
    private string ZoneName = string.Empty;

    public AlertWindow(Plugin plugin) : base("Hunty Alerts##HuntyAlerts")
    {
        Plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(240, 120),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Size = new Vector2(280, 190);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var territory = Plugin.ClientState.TerritoryType;
        if (!Plugin.PlayerState.IsLoaded || territory == 0)
        {
            Helper.TextColored(ImGuiColors.DalamudGrey, Language.AlertNotInZone);
            return;
        }

        RefreshIfNeeded(territory);

        if (!string.IsNullOrEmpty(ZoneName))
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.MapMarkerAlt.ToIconString());

            ImGui.SameLine();
            ImGui.TextUnformatted(ZoneName);
        }

        ImGui.Separator();

        if (Plugin.Configuration.AlertJobs.Count == 0)
        {
            Helper.TextColored(ImGuiColors.DalamudGrey, Language.AlertNoClasses);
            return;
        }

        if (Alerts.Count == 0)
        {
            Helper.TextColored(ImGuiColors.HealerGreen, Language.AlertNoPending);
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGui.TextWrapped(Language.AlertMapHint);

        ImGui.Separator();

        using var table = ImRaii.Table("##alertTable", 2);
        if (table.Success)
        {
            ImGui.TableSetupColumn(Language.AlertTableClass);
            ImGui.TableSetupColumn(Language.AlertTableEntries, ImGuiTableColumnFlags.WidthFixed, 45f * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var alert in Alerts)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var tracked = Plugin.MapMarkers.IsTracked(alert.Job);
                if (tracked)
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);

                if (JobIcons.TryGetValue(alert.Job, out var icon))
                {
                    Helper.DrawIcon(icon, 0.5f);
                    ImGui.SameLine();
                }

                ImGui.AlignTextToFramePadding();
                if (ImGui.Selectable($"{alert.Name}##alertJob{alert.Job}", false))
                    Plugin.MapMarkers.Toggle(alert.Job);

                if (tracked)
                    ImGui.PopStyleColor();

                ShowTooltip(alert);

                ImGui.TableNextColumn();
                ImGui.TextColored(ImGuiColors.DalamudOrange, alert.Pending.Count.ToString());
                ShowTooltip(alert);
            }
        }
    }

    private void ShowTooltip(JobAlert alert)
    {
        if (ImGui.IsItemHovered())
            Helper.Tooltip(BuildTooltip(alert));
    }

    private string BuildTooltip(JobAlert alert)
    {
        var lines = alert.Pending
                         .OrderBy(p => p.Monster.Name)
                         .Select(p => $"{Plugin.GetMonsterNameLoc(p.Monster.Id)}  {p.Progress.Killed}/{p.Monster.Count}");

        return $"{alert.Name}\n{string.Join('\n', lines)}";
    }

    private void RefreshIfNeeded(uint territory)
    {
        var jobs = Plugin.Configuration.AlertJobs;
        var jobsKey = string.Join(',', jobs);

        if (territory == CachedTerritory && jobsKey == CachedJobs && NextRefresh > Environment.TickCount64)
            return;

        CachedTerritory = territory;
        CachedJobs = jobsKey;
        NextRefresh = Environment.TickCount64 + RefreshIntervalMs;

        Alerts.Clear();
        ZoneName = GetZoneName(territory);

        foreach (var job in Plugin.JobArray.Where(jobs.Contains))
        {
            var pending = Plugin.GetPendingMonsters(job, territory);
            if (pending.Count == 0)
                continue;

            Alerts.Add(
                new JobAlert(
                    job,
                    Utils.ToTitleCaseExtended(Sheets.ClassJobSheet.GetRow(job).Name),
                    pending));
        }
    }

    private static string GetZoneName(uint territory)
    {
        var content = Sheets.ContentFinderConditionSheet.FirstOrNull(x => x.TerritoryType.RowId == territory);
        if (content is { } duty && !duty.Name.IsEmpty)
            return Utils.ToTitleCaseExtended(duty.Name);

        var territoryType = Sheets.TerritoryTypeSheet.GetRow(territory);
        if (territoryType.RowId == 0)
            return string.Empty;

        if (territoryType.Map.RowId == 0)
        {
            var place = territoryType.PlaceName.RowId == 0 ? string.Empty : territoryType.PlaceName.Value.Name.ToString();
            return string.IsNullOrEmpty(place) ? string.Empty : Utils.ToTitleCaseExtended(place);
        }

        var mapName = territoryType.Map.Value.PlaceName.Value.Name;
        if (!mapName.IsEmpty)
            return Utils.ToTitleCaseExtended(mapName);

        var placeName = territoryType.PlaceName.Value.Name;
        return placeName.IsEmpty ? string.Empty : Utils.ToTitleCaseExtended(placeName);
    }

    private sealed record JobAlert(uint Job, string Name, List<PendingMonster> Pending);
}
