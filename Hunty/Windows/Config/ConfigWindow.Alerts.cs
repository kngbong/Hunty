using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Hunty.Resources;

namespace Hunty.Windows.Config;

public partial class ConfigWindow
{
    private void Alerts()
    {
        using var tabItem = ImRaii.TabItem(Language.AlertTab);
        if (!tabItem.Success)
            return;

        var showWindow = Plugin.Configuration.ShowAlertWindow;
        if (ImGui.Checkbox(Language.AlertShowWindow, ref showWindow))
            Plugin.SetAlertWindowVisible(showWindow);

        ImGuiHelpers.ScaledDummy(5);
        ImGui.TextUnformatted(Language.AlertSelectClasses);

        using (ImRaii.Disabled(Plugin.Configuration.AlertJobs.Count == 0))
        {
            if (ImGui.SmallButton(Language.AlertClearAll))
            {
                Plugin.Configuration.AlertJobs.Clear();
                Plugin.Configuration.Save();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(Plugin.Configuration.AlertJobs.Count == Plugin.JobArray.Length))
        {
            if (ImGui.SmallButton(Language.AlertSelectAll))
            {
                Plugin.Configuration.AlertJobs.Clear();
                Plugin.Configuration.AlertJobs.AddRange(Plugin.JobArray);
                Plugin.Configuration.Save();
            }
        }

        using var table = ImRaii.Table("##alertJobs", 3, ImGuiTableFlags.SizingStretchSame);
        if (!table.Success)
            return;

        foreach (var job in Plugin.JobArray)
        {
            ImGui.TableNextColumn();
            var selected = Plugin.Configuration.AlertJobs.Contains(job);
            var name = Utils.ToTitleCaseExtended(Sheets.ClassJobSheet.GetRow(job).Name);
            if (!ImGui.Checkbox($"{name}##alertJob{job}", ref selected))
                continue;

            if (selected)
            {
                if (!Plugin.Configuration.AlertJobs.Contains(job))
                    Plugin.Configuration.AlertJobs.Add(job);
            }
            else
            {
                Plugin.Configuration.AlertJobs.Remove(job);
            }

            Plugin.Configuration.Save();
        }
    }
}
