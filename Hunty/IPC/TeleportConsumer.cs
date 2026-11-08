using System;
using Dalamud.Plugin.Ipc;

namespace Hunty.IPC;

public class TeleportConsumer
{
    private const int AvailabilityCheckIntervalMs = 5000;

    private bool Available;
    private long NextAvailabilityCheck;

    private ICallGateSubscriber<bool> IsTeleportAvailable = null!;
    private ICallGateSubscriber<uint, byte, bool> Teleport = null!;

    public TeleportConsumer() => Subscribe();

    public bool IsAvailable
    {
        get
        {
            if (NextAvailabilityCheck > Environment.TickCount64)
                return Available;

            NextAvailabilityCheck = Environment.TickCount64 + AvailabilityCheckIntervalMs;

            try
            {
                IsTeleportAvailable.InvokeFunc();
                Available = true;
            }
            catch
            {
                Available = false;
            }

            return Available;
        }
    }

    private void Subscribe()
    {
        try
        {
            IsTeleportAvailable = Plugin.PluginInterface.GetIpcSubscriber<bool>("Teleport.ChatMessage");
            Teleport = Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"Failed to subscribe to Teleport\nReason: {e}");
        }
    }

    public bool UseTeleport(uint aetheryteId)
    {
        try
        {
            return Teleport.InvokeFunc(aetheryteId, 0);
        }
        catch
        {
            Plugin.ChatGui.PrintError("Teleport plugin is not responding");
            return false;
        }
    }
}
