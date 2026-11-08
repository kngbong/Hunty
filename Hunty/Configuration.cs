using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace Hunty;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public bool SkipDone = true;

    /// <summary> Whether the small alert window is shown. </summary>
    public bool ShowAlertWindow = true;

    /// <summary> Classes whose hunting log is watched by the alert window. </summary>
    public List<uint> AlertJobs { get; set; } = [1, 2, 3, 4, 5, 6, 7, 26, 29];

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
