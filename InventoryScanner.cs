using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FC_Chest_Helper.Common;
using FC_Chest_Helper.GameData;

namespace FC_Chest_Helper
{
    public unsafe class InventoryScanner : IDisposable
    {
        private readonly HashSet<InventoryType> _loadedInventories = new();
        
        private delegate void* ContainerInfoNetworkData(int a2, int* a3);
        
        // Thanks to Critical Impact (Allagan Tools) for this signature.
        [Signature("48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 44 0F B7 42 ??",
                   DetourName = nameof(ContainerInfoDetour), UseFlags = SignatureUseFlags.Hook)]
        private Hook<ContainerInfoNetworkData>? _containerInfoHook = null;
        
        // FC Chest Pages
        private static readonly InventoryType[] FC_PAGES = {
            InventoryType.FreeCompanyPage1,
            InventoryType.FreeCompanyPage2,
            InventoryType.FreeCompanyPage3,
            InventoryType.FreeCompanyPage4,
            InventoryType.FreeCompanyPage5,
        };

        public InventoryScanner()
        {
            Plugin.GameInteropProvider.InitializeFromAttributes(this);
            _containerInfoHook?.Enable();
        }

        public void Dispose()
        {
            _containerInfoHook?.Dispose();
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
                        if (IsFCPage(inventoryType))
                        {
                            _loadedInventories.Add(inventoryType);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Error(e, "[InventoryScanner] ContainerInfo processing failed.");
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
                else
                {
                    _loadedInventories.Remove(page);
                }
            }
        }
        
        public bool IsInventoryLoaded(InventoryType type)
        {
            // For FC pages, we strictly check our tracked state which requires Addon visibility.
            if (IsFCPage(type))
            {
                return _loadedInventories.Contains(type);
            }
            
            // For other inventories (player bags), we can trust InventoryManager directly for now.
            // Or we can add them to _loadedInventories if we want to be consistent.
            var container = InventoryManager.Instance()->GetInventoryContainer(type);
            return container != null && container->IsLoaded;
        }

        private bool IsFCPage(InventoryType type)
        {
            return Array.IndexOf(FC_PAGES, type) >= 0;
        }
        
        public InventoryContainer* GetContainer(InventoryType type)
        {
            // For FC pages, still require loaded check to prevent stale reads
            if (IsFCPage(type))
            {
                if (!IsInventoryLoaded(type)) return null;
            }
            // For player inventory, always return - game memory is always valid
            return InventoryManager.Instance()->GetInventoryContainer(type);
        }
    }
}
