using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Hunty.Resources;
using Dalamud.Bindings.ImGui;

namespace Hunty.Windows;

public class XLWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    private int SelectedArea;
    private string LastArea = string.Empty;

    private readonly Dictionary<(uint Job, int Rank), Dictionary<string, List<HuntingMonster>>> AreaCache = new();

    public XLWindow(Plugin plugin) : base("Hunty XL")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 450),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted(Plugin.CurrentGcName);

        var skipText = Language.OptionSkipDone;
        var textLength = ImGui.GetTextLineHeight() + ImGui.CalcTextSize(skipText).X + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().FramePadding.X;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - textLength);

        if (ImGui.Checkbox(skipText, ref Plugin.Configuration.SkipDone))
            Plugin.Configuration.Save();

        ImGui.TextUnformatted(Plugin.CurrentJobName);
        if (!Plugin.HuntingData.JobRanks.TryGetValue(Plugin.CurrentJob, out var jobRanks) || jobRanks.Count == 0)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(5);

            Helper.TextColored(ImGuiColors.ParsedOrange, Language.ErrorNoLog);
            return;
        }

        var currentRank = Plugin.GetRankFromMemory(Plugin.CurrentJob);
        if (currentRank < 0 || currentRank >= jobRanks.Count)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(5);

            Helper.TextColored(ImGuiColors.HealerGreen, Language.ErrorLogDone);
            return;
        }

        var currentAreas = GetAreas(Plugin.CurrentJob, currentRank);
        var areaList = currentAreas.Keys.ToArray();
        if (areaList.Length == 0)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(5);

            Helper.TextColored(ImGuiColors.HealerGreen, Language.ErrorLogDone);
            return;
        }

        SelectedArea = areaList.Contains(LastArea) ? Array.IndexOf(areaList, LastArea) : 0;
        SelectedArea = Math.Clamp(SelectedArea, 0, areaList.Length - 1);

        ImGui.Combo("##areaSelector", ref SelectedArea, areaList, areaList.Length);
        Helper.DrawArrows(ref SelectedArea, areaList.Length, 4);
        LastArea = areaList[SelectedArea];

        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);

        var selectedAreaName = areaList[SelectedArea];
        foreach (var job in Plugin.JobArray)
        {
            if (!Plugin.HuntingData.JobRanks.TryGetValue(job, out var ranks) || ranks.Count == 0)
                continue;

            var jobRank = Plugin.GetRankFromMemory(job);
            if (jobRank < 0 || jobRank >= ranks.Count)
                continue;

            if (!GetAreas(job, jobRank).TryGetValue(selectedAreaName, out var monsters) || monsters.Count == 0)
                continue;

            Helper.TextColored(ImGuiColors.DalamudViolet, Utils.ToTitleCaseExtended(Sheets.ClassJobSheet.GetRow(job).Name));

            var memoryProgress = Plugin.GetMemoryProgress(job, jobRank);
            Helper.DrawProgressSymbol(monsters.All(x => memoryProgress.TryGetValue(x.Id, out var completed) && completed.Done));

            ImGuiHelpers.ScaledDummy(5);
            using var table = ImRaii.Table($"##monsterTable{job}", 4);
            if (table.Success)
            {
                ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, Helper.IconSize.X * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn(Language.TableLabelMonster);
                ImGui.TableSetupColumn(Language.TableLabelDone, ImGuiTableColumnFlags.None, 0.4f);
                ImGui.TableSetupColumn(Language.TableLabelCoords, ImGuiTableColumnFlags.None, 1.5f);

                ImGui.TableHeadersRow();
                foreach (var monster in monsters)
                {
                    var monsterProgress = memoryProgress.TryGetValue(monster.Id, out var progress) ? progress : new MonsterProgress(0);
                    if (Plugin.Configuration.SkipDone && monsterProgress.Done)
                        continue;

                    var location = monster.GetLocation;
                    if (location == null)
                        continue;

                    ImGui.TableNextColumn();
                    Helper.DrawIcon(monster.Icon);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Plugin.GetMonsterNameLoc(monster.Id));

                    ImGui.TableNextColumn();
                    if (monsterProgress.Done)
                    {
                        using (ImRaii.PushFont(UiBuilder.IconFont))
                            ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
                    }
                    else
                    {
                        ImGui.TextUnformatted($"{monsterProgress.Killed} / {monster.Count.ToString()}");
                    }

                    ImGui.TableNextColumn();
                    if (monster.IsOpenWorld)
                    {
                        if (Plugin.TeleportConsumer.IsAvailable)
                        {
                            using var pushedFont = ImRaii.PushFont(UiBuilder.IconFont);
                            if (ImGui.Button($"{FontAwesomeIcon.StreetView.ToIconString()}##{job}{monster.Id}{monster.Icon.ToString()}"))
                            {
                                Plugin.TeleportToNearestAetheryte(location);
                                Plugin.SetMapMarker(location.MapLink);
                            }

                            ImGui.SameLine();
                        }

                        if (ImGui.Selectable($"{location.Name} {location.MapLink.CoordinateString}##{job}{monster.Id}{monster.Icon.ToString()}"))
                            Plugin.SetMapMarker(location.MapLink);
                    }
                    else
                    {
                        if (ImGui.Selectable($"{location.DutyName}##{job}{monster.Id}{monster.Icon.ToString()}"))
                            Plugin.OpenDutyFinder(location.DutyKey);
                    }

                    ImGui.TableNextRow();
                }
            }
        }
    }

    private Dictionary<string, List<HuntingMonster>> GetAreas(uint job, int rank)
    {
        if (AreaCache.TryGetValue((job, rank), out var cached))
            return cached;

        var areas = new Dictionary<string, List<HuntingMonster>>();
        if (Plugin.HuntingData.JobRanks.TryGetValue(job, out var ranks) && rank >= 0 && rank < ranks.Count)
        {
            foreach (var monster in ranks[rank].Tasks.SelectMany(a => a.Monsters))
            {
                var location = monster.GetLocation;
                if (location == null)
                    continue;

                if (location.Name == string.Empty)
                    location.InitLocation();

                if (string.IsNullOrEmpty(location.Name))
                    continue;

                areas.GetOrCreate(location.Name).Add(monster);
            }
        }

        AreaCache[(job, rank)] = areas;
        return areas;
    }
}
