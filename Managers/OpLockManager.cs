using System;
using System.IO;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Managers
{
    public unsafe class OpLockManager : IDisposable
    {
        private readonly Configuration _configuration;

        private delegate bool SendInventoryRefreshDelegate(InventoryManager* instance, int inventoryType);

        [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B DA 48 8B F1 33 D2 0F B7 FA", DetourName = nameof(SendInventoryRefreshDetour))]
        private Hook<SendInventoryRefreshDelegate>? _sendInventoryRefreshHook = null;

        public OpLockManager(Configuration configuration)
        {
            _configuration = configuration;
            Plugin.GameInteropProvider.InitializeFromAttributes(this);
            _sendInventoryRefreshHook?.Enable();
            Plugin.PluginLog.Info("[OpLockManager] Initialized and hook enabled.");
        }

        private bool SendInventoryRefreshDetour(InventoryManager* instance, int inventoryType)
        {
            FCCH.Common.PerfCounter.RecordOpLockDetour();
            try
            {
                DebugLogCall(instance, inventoryType);
                GameMain.ExecuteCommand(404, inventoryType);
            }
            catch (Exception e)
            {
                try { Plugin.PluginLog.Error(e, "[OpLockManager] Detour body threw."); } catch { }
            }
            return true;
        }

        private void DebugLogCall(InventoryManager* instance, int inventoryType)
        {
            if (!_configuration.DebugMode) return;

            string typeName = ((InventoryType)(uint)inventoryType).ToString();

            string msg = $"[OpLockManager] SendInventoryRefresh intercepted: type={inventoryType} ({typeName}) instance=0x{(nint)instance:X}";
            Plugin.PluginLog.Info(msg);
            FCCH.Common.DebugFileLogger.Enqueue(_configuration.DebugLogPath, msg);
        }

        public void Dispose()
        {
            try { _sendInventoryRefreshHook?.Disable(); } catch (Exception e) { try { Plugin.PluginLog.Error(e, "[OpLockManager] Hook disable threw."); } catch { } }
            _sendInventoryRefreshHook?.Dispose();
            _sendInventoryRefreshHook = null;
        }
    }
}
