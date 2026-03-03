using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;
using FCCH.GameData;

namespace FCCH.Managers
{
    public unsafe class InventoryScanner : IDisposable
    {
        private readonly HashSet<InventoryType> _loadedInventories = new();
        private readonly Configuration _configuration;
        
        private delegate void* ContainerInfoNetworkData(int a2, int* a3);
        
        [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 44 0F B7 42 02 48 8B FA 48 8B D9",
                   DetourName = nameof(ContainerInfoDetour), UseFlags = SignatureUseFlags.Hook)]
        private Hook<ContainerInfoNetworkData>? _containerInfoHook = null;
        
        private static readonly InventoryType[] FC_PAGES = {
            InventoryType.FreeCompanyPage1,
            InventoryType.FreeCompanyPage2,
            InventoryType.FreeCompanyPage3,
            InventoryType.FreeCompanyPage4,
            InventoryType.FreeCompanyPage5,
            InventoryType.FreeCompanyGil,
            InventoryType.FreeCompanyCrystals,
        };

        public InventoryScanner(Configuration configuration)
        {
            _configuration = configuration;
            Plugin.GameInteropProvider.InitializeFromAttributes(this);

            if (_containerInfoHook != null)
            {
                _containerInfoHook.Enable();
                DebugLog("[InventoryScanner] ContainerInfoCallback hook resolved and enabled.");
            }
            else
            {
                Plugin.PluginLog.Warning("[InventoryScanner] ContainerInfoCallback signature mismatch — hook not resolved.");
                DebugLog("[InventoryScanner] ContainerInfoCallback signature mismatch — hook not resolved.");
            }
        }

        public void Dispose()
        {
            if (_containerInfoHook != null)
            {
                if (_containerInfoHook.IsEnabled)
                    _containerInfoHook.Disable();
                _containerInfoHook.Dispose();
            }
            _loadedInventories.Clear();
        }
        
        private void* ContainerInfoDetour(int seq, int* a3)
        {
            try
            {
                if (a3 != null)
                {
                    var ptr = (IntPtr)a3 + 16;
                    var containerInfo = NetworkDecoder.DecodeContainerInfo(ptr);
                    
                    if (Enum.IsDefined(typeof(InventoryType), containerInfo.ContainerId))
                    {
                        var inventoryType = (InventoryType)containerInfo.ContainerId;
                        DebugLog($"[InventoryScanner] ContainerInfo received: ContainerId={containerInfo.ContainerId} ({inventoryType}), NumItems={containerInfo.NumItems}, Seq={seq}");
                        if (IsFCPage(inventoryType))
                        {
                            _loadedInventories.Add(inventoryType);
                            DebugLog($"[InventoryScanner] FC page {inventoryType} marked as loaded.");
                        }
                    }
                    else
                    {
                        DebugLog($"[InventoryScanner] ContainerInfo received with unknown ContainerId={containerInfo.ContainerId}, Seq={seq}");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Error(e, "[InventoryScanner] ContainerInfo processing failed.");
                DebugLog($"[InventoryScanner] ContainerInfo processing failed: {e.Message}");
            }
            
            return _containerInfoHook!.Original(seq, a3);
        }

        public void Update()
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            
            if (addon == null || !addon->IsVisible)
            {
                foreach (var page in FC_PAGES)
                {
                    _loadedInventories.Remove(page);
                }
                return;
            }

            foreach (var page in FC_PAGES)
            {
                var container = InventoryManager.Instance()->GetInventoryContainer(page);
                if (container != null && (container->IsLoaded || container->Size > 0))
                {
                    _loadedInventories.Add(page);
                }

            }
        }
        
        public bool IsInventoryLoaded(InventoryType type)
        {
            if (IsFCPage(type))
            {
                return _loadedInventories.Contains(type);
            }
            
            var container = InventoryManager.Instance()->GetInventoryContainer(type);
            return container != null && container->IsLoaded;
        }

        private bool IsFCPage(InventoryType type)
        {
            return Array.IndexOf(FC_PAGES, type) >= 0;
        }
        
        public InventoryContainer* GetContainer(InventoryType type)
        {
            if (IsFCPage(type))
            {
                if (!IsInventoryLoaded(type)) return null;
            }
            return InventoryManager.Instance()->GetInventoryContainer(type);
        }

        private void DebugLog(string msg)
        {
            if (!_configuration.DebugMode) return;
            Plugin.PluginLog.Info(msg);

            try
            {
                var path = _configuration.DebugLogPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
