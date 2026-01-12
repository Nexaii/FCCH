using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using FC_Chest_Helper.Common;

namespace FC_Chest_Helper.Managers
{
    public unsafe class ChestManager : IDisposable
    {
        private readonly InventoryScanner _inventoryScanner;
        private readonly Configuration _configuration;
        
        public void SwitchToPage(AtkUnitBase* addon, InventoryType targetPage)
        {
            if (addon == null) return;
            
            // Standard InventoryType enumeration usually starts FC Page 1 at 20001
            // Target Index should be 0-based
            int targetIndex = (int)targetPage - 20000;
            
            if (targetIndex < 0 || targetIndex > 4) 
            {
                Plugin.PluginLog.Error($"[SwitchToPage] Invalid target index {targetIndex} for page {targetPage}");
                return;
            }

            var values = stackalloc AtkValue[2];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[0].Int = 0; // Action: Change Tab?
            values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[1].Int = targetIndex;
            
            addon->FireCallback((uint)Constants.FC_CHEST_CALLBACK_ID, values);
        }

        public Dictionary<InventoryType, List<ScannedSlot>> ChestState { get; private set; } = new();
        public List<ScannedSlot> CachedItems { get; private set; } = new();
        public string ScannedCharacterName { get; private set; } = "";

        public class ScannedSlot
        {
            public InventoryType Page;
            public uint Slot;
            public uint ItemId;
            public uint Quantity;
            public bool IsHq;
            public uint MaxStack;
        }

        public bool IsFullyScanned => ChestState.Count >= GetAvailableTabs().Count;

        public ChestManager(Configuration config)
        {
            _configuration = config;
            _inventoryScanner = new InventoryScanner();
        }

        public int ScanFCChest()
        {
            _inventoryScanner.Update();
            CachedItems.Clear();
            if (Plugin.ObjectTable.LocalPlayer != null)
            {
                ScannedCharacterName = Plugin.ObjectTable.LocalPlayer.Name.ToString();
            }

            foreach (var kvp in ChestState)
            {
                CachedItems.AddRange(kvp.Value);
            }

            var availableTabs = GetAvailableTabs();
            int totalFound = 0;

            foreach (var type in availableTabs)
            {
                if (!_inventoryScanner.IsInventoryLoaded(type)) continue;

                var container = _inventoryScanner.GetContainer(type);
                if (container == null) continue;
                
                var pageItems = new List<ScannedSlot>();
                int pageCount = 0;
                
                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;

                    uint maxStack = 999;
                    try
                    {
                        var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var row = sheet?.GetRowOrDefault(item->ItemId);
                        if (row != null) maxStack = row.Value.StackSize;
                    }
                    catch { }

                    pageItems.Add(new ScannedSlot
                    {
                        Page = type,
                        Slot = (uint)i,
                        ItemId = item->ItemId,
                        Quantity = (uint)item->Quantity,
                        IsHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality,
                        MaxStack = maxStack
                    });
                    pageCount++;
                }

                var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
                bool isActivePage = (addon != null && addon->IsVisible && GetCurrentFCPage(addon) == type);

                if (pageCount > 0 || isActivePage)
                {
                    ChestState[type] = pageItems;
                    CachedItems.RemoveAll(x => x.Page == type);
                    CachedItems.AddRange(pageItems);
                    totalFound += pageCount;
                }
            }
            return CachedItems.Count;
        }

        public void UpdateChestState(InventoryType page)
        {
            _inventoryScanner.Update(); 
            var container = _inventoryScanner.GetContainer(page);
            if (container == null) return;

            var items = new List<ScannedSlot>();
            for (int i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0) continue;

                uint maxStack = 999;
                try
                {
                    var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    var row = sheet?.GetRowOrDefault(item->ItemId);
                    if (row != null) maxStack = row.Value.StackSize;
                }
                catch { }

                items.Add(new ScannedSlot
                {
                    Page = page,
                    Slot = (uint)i,
                    ItemId = item->ItemId,
                    Quantity = (uint)item->Quantity,
                    IsHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality,
                    MaxStack = maxStack
                });
            }
            ChestState[page] = items;
        }

        public InventoryType GetCurrentFCPage(AtkUnitBase* addon)
        {
            if (addon == null) return InventoryType.Invalid;
            
            if (IsRadioButtonChecked(addon, 101)) return InventoryType.FreeCompanyPage1;
            if (IsRadioButtonChecked(addon, 100)) return InventoryType.FreeCompanyPage2;
            if (IsRadioButtonChecked(addon, 99)) return InventoryType.FreeCompanyPage3;
            if (IsRadioButtonChecked(addon, 98)) return InventoryType.FreeCompanyPage4;
            if (IsRadioButtonChecked(addon, 97)) return InventoryType.FreeCompanyPage5;
            
            return InventoryType.Invalid;
        }

        private bool IsRadioButtonChecked(AtkUnitBase* addon, uint nodeId)
        {
             if (nodeId >= addon->UldManager.NodeListCount) return false;
             var node = addon->UldManager.NodeList[nodeId];
             if (node == null || !node->IsVisible()) return false;
             
             var compNode = node->GetAsAtkComponentNode();
             if (compNode == null) return false;

             var component = compNode->Component;
             if (component == null) return false;
             
             // Check if "checked" image (usually node 2 or 3 in the component) is visible
             if (component->UldManager.NodeListCount > 2)
             {
                 var checkMark = component->UldManager.NodeList[2];
                 if (checkMark != null && checkMark->IsVisible()) return true;
             }
             
             var button = (AtkComponentRadioButton*)component;
             return (button->Flags & 0x40000) != 0; 
        }

        public byte GetChestAccess(InventoryType page)
        {
            try
            {
                var uiModule = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
                if (uiModule == null) return Constants.FCPermissions.NO_ACCESS;
                
                var infoModule = uiModule->GetInfoModule();
                if (infoModule == null) return Constants.FCPermissions.NO_ACCESS;

                var fcProxy = (InfoProxyFreeCompany*)infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
                if (fcProxy == null) return Constants.FCPermissions.NO_ACCESS;

                byte rankIndex = fcProxy->Rank;
                if (rankIndex >= 14) return Constants.FCPermissions.NO_ACCESS;

                var rankData = fcProxy->Ranks[rankIndex];
                
                var access = page switch
                {
                    InventoryType.FreeCompanyPage1 => rankData.Items1,
                    InventoryType.FreeCompanyPage2 => rankData.Items2,
                    InventoryType.FreeCompanyPage3 => rankData.Items3,
                    InventoryType.FreeCompanyPage4 => rankData.Items4,
                    InventoryType.FreeCompanyPage5 => rankData.Items5,
                    _ => (InfoProxyFreeCompany.RankData.ChestAccess)Constants.FCPermissions.NO_ACCESS
                };
                return (byte)access;
            }
            catch { return Constants.FCPermissions.NO_ACCESS; }
        }

        public byte GetFCRank()
        {
            try
            {
                var uiModule = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
                if (uiModule == null) return 0;
                
                var infoModule = uiModule->GetInfoModule();
                if (infoModule == null) return 0;

                var fcInfo = (InfoProxyFreeCompany*)infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
                return fcInfo != null ? fcInfo->Rank : (byte)0;
            }
            catch { return 0; }
        }

        public List<InventoryType> GetAvailableTabs()
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
            if (addon != null && addon->IsVisible)
            {
                var tabs = new List<InventoryType>();
                if (IsNodeVisible(addon, 101)) tabs.Add(InventoryType.FreeCompanyPage1);
                if (IsNodeVisible(addon, 100)) tabs.Add(InventoryType.FreeCompanyPage2);
                if (IsNodeVisible(addon, 99)) tabs.Add(InventoryType.FreeCompanyPage3);
                if (IsNodeVisible(addon, 98)) tabs.Add(InventoryType.FreeCompanyPage4);
                if (IsNodeVisible(addon, 97)) tabs.Add(InventoryType.FreeCompanyPage5);
                
                if (tabs.Count > 0) return tabs;
            }

            return new List<InventoryType> 
            { 
                InventoryType.FreeCompanyPage1, 
                InventoryType.FreeCompanyPage2, 
                InventoryType.FreeCompanyPage3, 
                InventoryType.FreeCompanyPage4, 
                InventoryType.FreeCompanyPage5 
            };
        }

        private bool IsNodeVisible(AtkUnitBase* addon, uint nodeId)
        {
             if (nodeId >= addon->UldManager.NodeListCount) return false;
             var node = addon->UldManager.NodeList[nodeId];
             return node != null && node->IsVisible();
        }

        public void Dispose()
        {
            _inventoryScanner.Dispose();
        }
        
        public void UpdateCacheAfterMove(InventoryType srcInv, uint srcSlot, InventoryType dstInv, uint dstSlot, uint itemId, uint amount, bool isHq)
        {
            if (srcInv >= InventoryType.FreeCompanyPage1 && srcInv <= InventoryType.FreeCompanyPage5)
            {
                var srcEntry = CachedItems.Find(x => x.Page == srcInv && x.Slot == srcSlot && x.ItemId == itemId);
                if (srcEntry != null)
                {
                    if (srcEntry.Quantity <= amount)
                        CachedItems.Remove(srcEntry);
                    else
                        srcEntry.Quantity -= amount;
                }
            }
            
            if (dstInv >= InventoryType.FreeCompanyPage1 && dstInv <= InventoryType.FreeCompanyPage5)
            {
                var dstEntry = CachedItems.Find(x => x.Page == dstInv && x.Slot == dstSlot);
                if (dstEntry != null)
                {
                    dstEntry.Quantity += amount;
                }
                else
                {
                    CachedItems.Add(new ScannedSlot
                    {
                        Page = dstInv,
                        Slot = dstSlot,
                        ItemId = itemId,
                        Quantity = amount,
                        IsHq = isHq,
                        MaxStack = 999
                    });
                }
            }
        }
        
        public InventoryContainer* GetContainer(InventoryType type) => _inventoryScanner.GetContainer(type);
        public bool IsInventoryLoaded(InventoryType type) => _inventoryScanner.IsInventoryLoaded(type);
    }
}
