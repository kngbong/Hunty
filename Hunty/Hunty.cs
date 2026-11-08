using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Hunty.Windows;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Hunty.Data;
using Hunty.IPC;
using Hunty.Resources;
using Hunty.Windows.Config;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;

namespace Hunty;

#pragma warning disable SeStringEvaluator
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] public static IDataManager Data { get; set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; set; } = null!;
    [PluginService] public static IClientState ClientState { get; set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] public static IGameGui GameGui { get; set; } = null!;
    [PluginService] public static IFramework Framework { get; set; } = null!;
    [PluginService] public static IPluginLog Log { get; set; } = null!;
    [PluginService] public static ITextureProvider Texture { get; set; } = null!;
    [PluginService] public static ISeStringEvaluator Evaluator { get; set; } = null!;
    [PluginService] public static IGameInteropProvider Interop { get; set; } = null!;

    private const string CommandName = "/hunty";
    private const string CommandXL = "/huntyxl";
    private const string CommandCompanion = "/huntycompanion";
    private const string CommandAlert = "/huntyalert";

    public Configuration Configuration { get; init; }

    private WindowSystem WindowSystem = new("Hunty");
    private ConfigWindow ConfigWindow;
    private MainWindow MainWindow;
    private XLWindow XLWindow;
    private CompanionWindow CompanionWindow;
    private AlertWindow AlertWindow;

    public readonly HuntingData HuntingData;
    public HuntingMapMarkers MapMarkers { get; }
    private uint CurrentJobId;
    private ClassJob CurrentJobParent;

    public TeleportConsumer TeleportConsumer = null!;

    public uint CurrentJob = 1;
    public string CurrentJobName = "";
    public uint CurrentGc = 1;
    public string CurrentGcName = "";

    public static readonly uint[] JobArray = [1, 2, 3, 4, 5, 6, 7, 26, 29];

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        LanguageChanged(PluginInterface.UiLanguage);

        TeleportConsumer = new TeleportConsumer();

        HuntingData = new HuntingData();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        XLWindow = new XLWindow(this);
        CompanionWindow = new CompanionWindow(this);
        AlertWindow = new AlertWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(XLWindow);
        WindowSystem.AddWindow(CompanionWindow);
        WindowSystem.AddWindow(AlertWindow);

        AlertWindow.IsOpen = Configuration.ShowAlertWindow;

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigWindow;
        PluginInterface.LanguageChanged += LanguageChanged;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = Language.HelpMessage
        });

        CommandManager.AddHandler(CommandXL, new CommandInfo(OnXLCommand)
        {
            HelpMessage = Language.HelpMessageXL
        });

        CommandManager.AddHandler(CommandCompanion, new CommandInfo(OnCompanionCommand)
        {
            HelpMessage = Language.HelpMessageCompanion
        });

        CommandManager.AddHandler(CommandAlert, new CommandInfo(OnAlertCommand)
        {
            HelpMessage = Language.HelpMessageAlert
        });

        try
        {
            Log.Debug("Loading Monsters.");

            var path = Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "monsters.json");
            if (!File.Exists(path))
            {
                Log.Error("monsters.json was not found at {Path}.", path);
            }
            else
            {
                var jsonString = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<HuntingData>(jsonString);
                if (data == null)
                    Log.Error("monsters.json could not be deserialized.");
                else
                    HuntingData = data;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "There was a problem building the HuntingData.");
        }

        MapMarkers = new HuntingMapMarkers(this);

        Framework.Update += CheckJobChange;
        ClientState.Login += OnLogin;
    }

    public void Dispose()
    {
        Framework.Update -= CheckJobChange;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigWindow;
        PluginInterface.LanguageChanged -= LanguageChanged;

        MapMarkers.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        XLWindow.Dispose();
        CompanionWindow.Dispose();
        AlertWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandXL);
        CommandManager.RemoveHandler(CommandCompanion);
        CommandManager.RemoveHandler(CommandAlert);
    }

    private void LanguageChanged(string langCode)
    {
        try
        {
            Language.Culture = new CultureInfo(langCode);
        }
        catch (CultureNotFoundException ex)
        {
            Log.Warning(ex, "Unsupported UI language {Language}, falling back to the neutral language.", langCode);
            Language.Culture = CultureInfo.InvariantCulture;
        }
    }

    private void OnLogin()
    {
        SetCurrentState();
    }

    private void CheckJobChange(IFramework framework)
    {
        if (!PlayerState.IsLoaded)
            return;

        var currentJobRes = PlayerState.ClassJob;
        if (currentJobRes.RowId != CurrentJobId)
        {
            CurrentJobId = currentJobRes.RowId;

            var parentJob = currentJobRes.Value.ClassJobParent;
            if (parentJob.RowId != CurrentJobParent.RowId)
                SetCurrentState();
        }
    }

    private void SetCurrentState()
    {
        if (!PlayerState.IsLoaded)
            return;

        var currentJobRes = PlayerState.ClassJob;
        CurrentJobId = currentJobRes.RowId;
        CurrentJobParent = currentJobRes.Value.ClassJobParent.Value;

        var name = Utils.ToTitleCaseExtended(CurrentJobParent.Name);
        CurrentJob = CurrentJobParent.RowId;
        CurrentJobName = name;
        CurrentGc = GetGrandCompany();
        CurrentGcName = GetCurrentGcName();

        MainWindow.Defaults();
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    private void OnXLCommand(string command, string args) => XLWindow.Toggle();
    private void OnCompanionCommand(string command, string args) => CompanionWindow.Toggle();
    private void OnAlertCommand(string command, string args) => AlertWindow.Toggle();
    private void DrawUi()
    {
        WindowSystem.Draw();

        // Persist the alert window state so it survives restarts and close-button clicks.
        if (Configuration.ShowAlertWindow != AlertWindow.IsOpen)
        {
            Configuration.ShowAlertWindow = AlertWindow.IsOpen;
            Configuration.Save();
        }
    }
    private void OpenMainWindow() => MainWindow.Toggle();
    private void OpenConfigWindow() => ConfigWindow.Toggle();

    public void SetAlertWindowVisible(bool visible)
    {
        AlertWindow.IsOpen = visible;
        Configuration.ShowAlertWindow = visible;
        Configuration.Save();
    }

    public void SetMapMarker(MapLinkPayload map) => GameGui.OpenMapWithMapLink(map);
    public unsafe void OpenDutyFinder(uint key) => AgentContentsFinder.Instance()->OpenRegularDuty(key);

    public uint GetGrandCompany()
    {
        var grandCompanyRowId = PlayerState.GrandCompany.RowId;
        return grandCompanyRowId == 0 ? 10000 : grandCompanyRowId + 10000;
    }

    public string GetCurrentGcName()
    {
        var grandCompany = GetGrandCompany();
        return grandCompany == 10000
                   ? string.Empty
                   : Utils.ToTitleCaseExtended(Sheets.GrandCompanySheet.GetRow(grandCompany - 10000).Name);
    }

    public unsafe int GetRankFromMemory(uint job)
    {
        if (!PlayerState.IsLoaded)
            return 0;

        var monsterNoteManager = MonsterNoteManager.Instance();
        return monsterNoteManager == null ? 0 : monsterNoteManager->RankData[StaticData.JobInMemory(job)].Rank;
    }

    public unsafe Dictionary<uint, MonsterProgress> GetMemoryProgress(uint job, int rank)
    {
        var currentProgress = new Dictionary<uint, MonsterProgress>();

        if (!PlayerState.IsLoaded)
            return currentProgress;

        if (!HuntingData.JobRanks.TryGetValue(job, out var ranks) || rank < 0 || rank >= ranks.Count)
            return currentProgress;

        var huntingRank = ranks[rank];
        var monsterNoteManager = MonsterNoteManager.Instance();
        if (monsterNoteManager == null)
            return currentProgress;

        var jobMemory = monsterNoteManager->RankData[StaticData.JobInMemory(job)];
        var progressRank = jobMemory.Rank;
        var monsters = huntingRank.Tasks.SelectMany(a => a.Monsters);

        if (progressRank > rank) // Rank is already finished, all monsters are done
        {
            foreach (var monster in monsters)
                currentProgress[monster.Id] = new MonsterProgress(true);

            return currentProgress;
        }

        if (progressRank < rank) // Rank not yet reached, kills must be zero
        {
            foreach (var monster in monsters)
                currentProgress[monster.Id] = new MonsterProgress(0);

            return currentProgress;
        }

        var rankData = jobMemory.RankData.ToArray();
        for (var taskIndex = 0; taskIndex < huntingRank.Tasks.Count && taskIndex < rankData.Length; taskIndex++)
        {
            var task = huntingRank.Tasks[taskIndex];
            var counts = rankData[taskIndex].Counts;
            for (var monsterIndex = 0; monsterIndex < task.Monsters.Count; monsterIndex++)
            {
                var monster = task.Monsters[monsterIndex];
                var killed = monsterIndex < counts.Length ? counts[monsterIndex] : 0;
                currentProgress[monster.Id] = killed == monster.Count
                                                  ? new MonsterProgress(true)
                                                  : new MonsterProgress(killed);
            }
        }

        return currentProgress;
    }

    /// <summary>
    /// Returns the monsters of the class's active rank that are still pending in the given territory.
    /// A monster counts as present when it spawns in the open world territory or in the duty zone.
    /// </summary>
    public List<PendingMonster> GetPendingMonsters(uint job, uint territory)
    {
        var pending = new List<PendingMonster>();

        if (territory == 0 || !HuntingData.JobRanks.TryGetValue(job, out var ranks) || ranks.Count == 0)
            return pending;

        var rank = GetRankFromMemory(job);
        if (rank < 0 || rank >= ranks.Count)
            return pending;

        var progress = GetMemoryProgress(job, rank);
        foreach (var monster in ranks[rank].Tasks.SelectMany(task => task.Monsters))
        {
            if (!monster.Locations.Any(location => location.Terri == territory || location.Zone == territory))
                continue;

            var monsterProgress = progress.TryGetValue(monster.Id, out var current) ? current : new MonsterProgress(0);
            if (monsterProgress.Done)
                continue;

            pending.Add(new PendingMonster(monster, monsterProgress));
        }

        return pending;
    }

    // From: https://github.com/SheepGoMeh/HuntBuddy/blob/5a92e0e104839c30eaf398790dee32b793c3c53e/HuntBuddy/Location.cs#L520
    public void TeleportToNearestAetheryte(HuntingMonsterLocation location)
    {
        if (location.Map == 0)
            return;

        var map = Sheets.MapSheet.GetRow(location.Map);
        var nearestAetheryteId = Sheets.MapMarkerSheet
                                       .SelectMany(x => x)
                                       .Where(x => x.DataType == 3 && x.RowId == map.MapMarkerRange)
                                       .Select(
                                           marker => new
                                           {
                                               distance = Vector2.DistanceSquared(
                                                   location.Coords,
                                                   ConvertLocationToRaw(marker.X, marker.Y, map.SizeFactor)),
                                               rowId = marker.DataKey.RowId
                                           })
                                       .MinBy(x => x.distance)?.rowId ?? 0;

        // Support the unique case of aetheryte not being in the same map
        var nearestAetheryte = location.Terri == 399
                                   ? map.TerritoryType.Value.Aetheryte.Value
                                   : Sheets.AetheryteSheet.FirstOrNull(x => x.IsAetheryte && x.Territory.RowId == location.Terri && x.RowId == nearestAetheryteId);

        if (nearestAetheryte == null)
            return;

        TeleportConsumer.UseTeleport(nearestAetheryte.Value.RowId);
    }

    private static Vector2 ConvertLocationToRaw(int x, int y, float scale)
    {
        var num = scale / 100f;
        return new Vector2(ConvertRawToMap((int)((x - 1024) * num * 1000f), scale), ConvertRawToMap((int)((y - 1024) * num * 1000f), scale));
    }

    private static float ConvertRawToMap(int pos, float scale)
    {
        var num1 = scale / 100f;
        var num2 = (float)(pos * (double)num1 / 1000.0f);
        return (40.96f / num1 * ((num2 + 1024.0f) / 2048.0f)) + 1.0f;
    }

    public static string GetMonsterNameLoc(uint id)
    {
        return Evaluator.EvaluateObjStr(ObjectKind.BattleNpc, id);
    }
}
