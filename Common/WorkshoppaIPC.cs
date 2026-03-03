using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace FCCH.Common
{
    public sealed class WorkshoppaIPC
    {
        private const string PluginName = "VIWI";
        private const string Ipc_AddQueueItem = "VIWI.Workshoppa.AddQueueItem";
        private const string Ipc_ClearQueue = "VIWI.Workshoppa.ClearQueue";

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly ICallGateSubscriber<uint, int, bool> _addQueueItem;
        private readonly ICallGateSubscriber<bool> _clearQueue;

        public WorkshoppaIPC(IDalamudPluginInterface pluginInterface)
        {
            _pluginInterface = pluginInterface;
            _addQueueItem = pluginInterface.GetIpcSubscriber<uint, int, bool>(Ipc_AddQueueItem);
            _clearQueue = pluginInterface.GetIpcSubscriber<bool>(Ipc_ClearQueue);
        }

        public bool IsAvailable =>
            _pluginInterface.InstalledPlugins.Any(p => p.InternalName == PluginName && p.IsLoaded);

        public bool AddQueueItem(uint workshopItemId, int quantity)
        {
            try
            {
                return IsAvailable && _addQueueItem.InvokeFunc(workshopItemId, quantity);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool ClearQueue()
        {
            try
            {
                return IsAvailable && _clearQueue.InvokeFunc();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
