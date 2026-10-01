using FCCH.Common;
using System;
using System.IO;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Diagnostics;

namespace FCCH.Managers
{
    public unsafe class OpLockManager : IDisposable
    {
        private delegate bool SendInventoryRefreshDelegate(InventoryManager* instance, int inventoryType);

        [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B DA 48 8B F1 33 D2 0F B7 FA", DetourName = nameof(SendInventoryRefreshDetour))]
        private Hook<SendInventoryRefreshDelegate>? _sendInventoryRefreshHook = null;

        public OpLockManager()
        {
            Plugin.GameInteropProvider.InitializeFromAttributes(this);

            if (_sendInventoryRefreshHook != null)
            {
                _sendInventoryRefreshHook.Enable();
                Log.Info("Initialized, hook enabled.", "OpLock");
            }
            else
            {
                Log.Warning("[OpLock] SendInventoryRefresh signature mismatch - hook not resolved.");
            }
        }

        private bool SendInventoryRefreshDetour(InventoryManager* instance, int inventoryType)
        {
            FCCH.Common.PerfCounter.RecordOpLockDetour();
            try
            {
                DebugLogCall(inventoryType);
                GameMain.ExecuteCommand(404, inventoryType);
            }
            catch (Exception e)
            {
                try { Log.Error(e, "[OpLockManager] Detour body threw."); } catch { }
            }
            return true;
        }

        private void DebugLogCall(int inventoryType) =>
            Log.Verbose($"SendInventoryRefresh type={inventoryType} ({(InventoryType)(uint)inventoryType})", "OpLock");

        public void Dispose()
        {
            try { _sendInventoryRefreshHook?.Disable(); } catch (Exception e) { try { Log.Error(e, "[OpLockManager] Hook disable threw."); } catch { } }
            _sendInventoryRefreshHook?.Dispose();
            _sendInventoryRefreshHook = null;
        }
    }
}
