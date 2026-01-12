using System;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FC_Chest_Helper.Managers
{
    public unsafe class OpLockManager : IDisposable
    {      
        private delegate bool SendInventoryRefreshDelegate(InventoryManager* instance, int inventoryType);
        
        [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B DA 48 8B F1 33 D2 0F B7 FA", DetourName = nameof(SendInventoryRefreshDetour))]
        private Hook<SendInventoryRefreshDelegate>? _sendInventoryRefreshHook;

        public OpLockManager()
        {
            Plugin.GameInteropProvider.InitializeFromAttributes(this);
            _sendInventoryRefreshHook?.Enable();
            Plugin.PluginLog.Info("[OpLockManager] Initialized and hook enabled.");
        }

        private bool SendInventoryRefreshDetour(InventoryManager* instance, int inventoryType)
        {
            GameMain.ExecuteCommand(405, inventoryType); // 405 = RequestInventory
            return true;
        }

        public void Dispose()
        {
            _sendInventoryRefreshHook?.Dispose();
        }
    }
}
