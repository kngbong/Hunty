using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Hunty.Resources;
using Dalamud.Bindings.ImGui;

namespace Hunty.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    private int SelectedClass;
    private int SelectedRank;
    private int SelectedArea;

    private bool OpenGrandCompany;
    private uint? ForcedJob;

    private static readonly string[] Jobs = new string[9];
    private readonly Dictionary<string, List<HuntingMonster>> CurrentAreas = new();

    public MainWindow(Plugin plugin) : base("Hunty")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 450),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;

        for (var i = 0; i < Plugin.JobArray.Length; i++)
            Jobs[i] = Utils.ToTitleCaseExtended(Sheets.ClassJobSheet.GetRow(Plugin.JobArray[i]).Name);
    }

    public void Dispose() { }

    /// <summary>The class currently shown in the window.</summary>
    public uint DisplayedJob => ForcedJob ?? (OpenGrandCompany ? Plugin.CurrentGc : Plugin.CurrentJob);

    /// <summary>
    /// Opens the window for the given class, or closes it when that class is already shown.
    /// </summary>
    public void ToggleForJob(uint job)
    {
        if (IsOpen && DisplayedJob == job)
        {
            IsOpen = false;
            return;
        }

        ForcedJob = job;
        OpenGrandCompany = false;
        SelectedClass = Math.Max(0, Array.IndexOf(Plugin.JobArray, job));
        SelectedRank = 0;
        SelectedArea = 0;
        CurrentAreas.Clear();

        if (Plugin.HuntingData.JobRanks.TryGetValue(job, out var ranks) && ranks.Count > 0)
            SelectedRank = Math.Clamp(Plugin.GetRankFromMemory(job), 0, ranks.Count - 1);

        IsOpen = true;
    }

    public override void Draw()
    {
        var oldRank = SelectedRank;
        var oldClass = SelectedClass;

        ImGui.AlignTextToFramePadding();
        List<HuntingRank>? selClass = null;
        if (OpenGrandCompany)
        {
            Plugin.HuntingData.JobRanks.TryGetValue(Plugin.CurrentGc, out selClass);
            ImGui.TextUnformatted(Plugin.CurrentGcName);
        }
        else if (ForcedJob is { } forcedJob)
        {
            Plugin.HuntingData.JobRanks.TryGetValue(forcedJob, out selClass);
            ImGui.TextUnformatted(Utils.ToTitleCaseExtended(Sheets.ClassJobSheet.GetRow(forcedJob).Name));
        }
        else if (!Plugin.HuntingData.JobRanks.TryGetValue(Plugin.CurrentJob, out selClass))
        {
            ImGui.Combo("##classSelector", ref SelectedClass, Jobs, Jobs.Length);
            Helper.DrawArrows(ref SelectedClass, Jobs.Length);

            SelectedClass = Math.Clamp(SelectedClass, 0, Plugin.JobArray.Length - 1);
            Plugin.HuntingData.JobRanks.TryGetValue(Plugin.JobArray[SelectedClass], out selClass);
        }
        else
        {
            ImGui.TextUnformatted(Plugin.CurrentJobName);
        }

        var btnText = OpenGrandCompany ? Language.ButtonJobs : Language.ButtonGrandCompany;
        var textLength = ImGui.CalcTextSize(btnText).X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - textLength);

        if (ImGui.Button(btnText))
        {
            OpenGrandCompany ^= true;
            Defaults();
            return;
        }

        if (OpenGrandCompany && Plugin.CurrentGc == 10000)
        {
            Helper.TextColored(Helper.RedColor, Language.ErrorNoGrandCompany);
            return;
        }

        if (selClass is not { Count: > 0 })
        {
            Helper.TextColored(Helper.RedColor, Language.ErrorNoLog);
            return;
        }

        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);

        SelectedClass = Math.Clamp(SelectedClass, 0, Plugin.JobArray.Length - 1);
        SelectedRank = Math.Clamp(SelectedRank, 0, selClass.Count - 1);
        var current = ForcedJob ?? (!OpenGrandCompany ? Plugin.JobArray[SelectedClass] : Plugin.CurrentGc);

        var rankList = selClass.Select((_, i) => $"{Language.SelectorRank} {i+1}").ToArray();
        ImGui.Combo("##rankSelector", ref SelectedRank, rankList, rankList.Length);
        Helper.DrawArrows(ref SelectedRank, rankList.Length, 2);
        Helper.DrawProgressSymbol(SelectedRank < Plugin.GetRankFromMemory(current));

        FillCurrentAreas(oldRank, oldClass, selClass);

        if (CurrentAreas.Count == 0)
        {
            Helper.TextColored(Helper.RedColor, Language.ErrorLogDone);
            return;
        }

        var areaList = CurrentAreas.Keys.ToArray();
        SelectedArea = Math.Clamp(SelectedArea, 0, areaList.Length - 1);
        ImGui.Combo("##areaSelector", ref SelectedArea, areaList, areaList.Length);
        Helper.DrawArrows(ref SelectedArea, areaList.Length, 4);

        var monsters = CurrentAreas[areaList[SelectedArea]];
        var memoryProgress = Plugin.GetMemoryProgress(current, SelectedRank);
        Helper.DrawProgressSymbol(monsters.All(x => memoryProgress.TryGetValue(x.Id, out var completed) && completed.Done));

        ImGuiHelpers.ScaledDummy(10);

        using var table = ImRaii.Table("##monsterTable", 4);
        if (table.Success)
        {
            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, Helper.IconSize.X * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn(Language.TableLabelMonster);
            ImGui.TableSetupColumn(Language.TableLabelDone, ImGuiTableColumnFlags.WidthStretch, 0.4f);
            ImGui.TableSetupColumn(monsters.Any(x => x.IsOpenWorld) ? Language.TableLabelCoords : Language.TableLabelDungeon, ImGuiTableColumnFlags.WidthStretch, 1.5f);

            ImGui.TableHeadersRow();
            foreach (var monster in monsters)
            {
                var location = monster.GetLocation;
                if (location == null)
                    continue;

                ImGui.TableNextColumn();
                Helper.DrawIcon(monster.Icon);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(Plugin.GetMonsterNameLoc(monster.Id));

                ImGui.TableNextColumn();
                var monsterProgress = memoryProgress.TryGetValue(monster.Id, out var progress) ? progress : new MonsterProgress(0);
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
                        if (ImGui.Button($"{FontAwesomeIcon.StreetView.ToIconString()}##{monster.Id}{monster.Icon.ToString()}"))
                        {
                            Plugin.TeleportToNearestAetheryte(location);
                            Plugin.SetMapMarker(location.MapLink);
                        }

                        ImGui.SameLine();
                    }

                    if (ImGui.Selectable($"{location.Name} {location.MapLink.CoordinateString}##{monster.Id}{monster.Icon.ToString()}"))
                        Plugin.SetMapMarker(location.MapLink);
                }
                else
                {
                    if (ImGui.Selectable($"{location.DutyName}##{monster.Id}{monster.Icon.ToString()}"))
                        Plugin.OpenDutyFinder(location.DutyKey);
                }

                ImGui.TableNextRow();
            }
        }
    }

    public void Defaults()
    {
        SelectedClass = 0;
        SelectedRank = 0;
        SelectedArea = 0;
        ForcedJob = null;

        if (Plugin.HuntingData.JobRanks.ContainsKey(Plugin.CurrentJob))
            SelectedClass = Math.Max(0, Array.IndexOf(Plugin.JobArray, Plugin.CurrentJob));

        CurrentAreas.Clear();
    }

    private void FillCurrentAreas(int oldRank, int oldClass, IReadOnlyList<HuntingRank> selClass)
    {
        if (SelectedRank == oldRank && SelectedClass == oldClass && CurrentAreas.Count != 0)
            return;

        SelectedArea = 0;
        CurrentAreas.Clear();
        foreach (var m in selClass[SelectedRank].Tasks.SelectMany(a => a.Monsters))
        {
            var location = m.GetLocation;
            if (location == null)
                continue;

            if (location.Name == string.Empty)
                location.InitLocation();

            var areaName = location.IsDuty ? location.DutyName : location.Name;
            if (string.IsNullOrEmpty(areaName))
                continue;

            CurrentAreas.GetOrCreate(areaName).Add(m);
        }
    }
}
