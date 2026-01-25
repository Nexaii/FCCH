using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using FCCH.Common;

namespace FCCH.Managers
{
    public unsafe class ChestManager : IDisposable
    {
        private readonly InventoryScanner _inventoryScanner;
        private readonly Configuration _configuration;
        
        public void SwitchToPage(AtkUnitBase* addon, InventoryType targetPage)
        {
            if (addon == null) return;
            
            int targetIndex = targetPage switch
            {
                InventoryType.FreeCompanyPage1 => 0,
                InventoryType.FreeCompanyPage2 => 1,
                InventoryType.FreeCompanyPage3 => 2,
                InventoryType.FreeCompanyPage4 => 3,
                InventoryType.FreeCompanyPage5 => 4,
                InventoryType.FreeCompanyCrystals => 5,
                InventoryType.FreeCompanyGil => 6,
                _ => -1
            };
            
            if (targetIndex == -1) 
            {
                Plugin.PluginLog.Error($"[SwitchToPage] Invalid target index {targetIndex} for page {targetPage}");
                return;
            }

            var values = stackalloc AtkValue[2];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[0].Int = 0;
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

            var permissions = new List<InventoryType> 
            { 
                InventoryType.FreeCompanyPage1, 
                InventoryType.FreeCompanyPage2, 
                InventoryType.FreeCompanyPage3, 
                InventoryType.FreeCompanyPage4, 
                InventoryType.FreeCompanyPage5,
                InventoryType.FreeCompanyCrystals,
                InventoryType.FreeCompanyGil 
            };
            int totalFound = 0;

            foreach (var type in permissions)
            {
                if (!_inventoryScanner.IsInventoryLoaded(type)) continue;

                var container = _inventoryScanner.GetContainer(type);
                if (container == null) continue;
                
                var pageItems = new List<ScannedSlot>();
                int pageCount = 0;
                
                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || (item->ItemId == 0 && item->Quantity == 0)) continue;

                    uint maxStack = 999;
                    try
                    {
                        var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var row = sheet?.GetRowOrDefault(item->ItemId);
                        if (row != null) maxStack = row.Value.StackSize;
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Warning($"Failed to get stack size for Item#{item->ItemId}: {ex.Message}");
                    }

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

                if (type == InventoryType.FreeCompanyGil && pageItems.Count == 0)
                {
                     pageItems.Add(new ScannedSlot
                     {
                         Page = type,
                         ItemId = 1, 
                         Quantity = 0,
                         Slot = 0,
                         IsHq = false
                     });
                }

                if (pageCount > 0 || isActivePage || type == InventoryType.FreeCompanyGil || type == InventoryType.FreeCompanyCrystals)
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
                if (item == null || (item->ItemId == 0 && item->Quantity == 0)) continue;

                uint maxStack = 999;
                try
                {
                    var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    var row = sheet?.GetRowOrDefault(item->ItemId);
                    if (row != null) maxStack = row.Value.StackSize;
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning($"Failed to get stack size for Item#{item->ItemId}: {ex.Message}");
                }

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

            if (page == InventoryType.FreeCompanyGil && items.Count == 0)
            {
                items.Add(new ScannedSlot
                {
                    Page = page,
                    ItemId = 1,
                    Quantity = 0,
                    Slot = 0,
                    IsHq = false
                });
            }

            ChestState[page] = items;
            CachedItems.RemoveAll(x => x.Page == page);
            CachedItems.AddRange(items);
        }

        public InventoryType GetCurrentFCPage(AtkUnitBase* addon)
        {
            if (addon == null) return InventoryType.Invalid;
            
            if (IsRadioButtonChecked(addon, 101)) return InventoryType.FreeCompanyPage1;
            if (IsRadioButtonChecked(addon, 100)) return InventoryType.FreeCompanyPage2;
            if (IsRadioButtonChecked(addon, 99)) return InventoryType.FreeCompanyPage3;
            if (IsRadioButtonChecked(addon, 98)) return InventoryType.FreeCompanyPage4;
            if (IsRadioButtonChecked(addon, 97)) return InventoryType.FreeCompanyPage5;
            if (IsRadioButtonChecked(addon, 15)) return InventoryType.FreeCompanyCrystals;
            if (IsRadioButtonChecked(addon, 16)) return InventoryType.FreeCompanyGil;
            
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
                if (IsNodeVisible(addon, 15)) tabs.Add(InventoryType.FreeCompanyCrystals);
                if (IsNodeVisible(addon, 16)) tabs.Add(InventoryType.FreeCompanyGil);
                
                if (tabs.Count > 0) return tabs;
            }

            return new List<InventoryType> 
            { 
                InventoryType.FreeCompanyPage1, 
                InventoryType.FreeCompanyPage2, 
                InventoryType.FreeCompanyPage3, 
                InventoryType.FreeCompanyPage4, 
                InventoryType.FreeCompanyPage5,
                InventoryType.FreeCompanyCrystals,
                InventoryType.FreeCompanyGil 
            };
        }

        public List<InventoryType> GetDepositableTabs()
        {
            var allTabs = GetAvailableTabs();
            var result = new List<InventoryType>();
            
            foreach (var tab in allTabs)
            {
                if (tab == InventoryType.FreeCompanyCrystals || tab == InventoryType.FreeCompanyGil)
                {
                    result.Add(tab);
                    continue;
                }
                
                var access = GetChestAccess(tab);
                if (access == 0 || access == 2)
                {
                    result.Add(tab);
                }
            }
            
            return result;
        }

        private bool IsNodeVisible(AtkUnitBase* addon, uint nodeId)
        {
             if (nodeId >= addon->UldManager.NodeListCount) return false;
             var node = addon->UldManager.NodeList[nodeId];
             return node != null && node->IsVisible();
        }


        
        public void UpdateCacheAfterMove(InventoryType srcInv, uint srcSlot, InventoryType dstInv, uint dstSlot, uint itemId, uint amount, bool isHq)
        {
            if ((srcInv >= InventoryType.FreeCompanyPage1 && srcInv <= InventoryType.FreeCompanyPage5) || srcInv == InventoryType.FreeCompanyGil || srcInv == InventoryType.FreeCompanyCrystals)
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
            
            if ((dstInv >= InventoryType.FreeCompanyPage1 && dstInv <= InventoryType.FreeCompanyPage5) || dstInv == InventoryType.FreeCompanyGil || dstInv == InventoryType.FreeCompanyCrystals)
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

        public string GetDebugContent()
        {
            var sb = new System.Text.StringBuilder();
            
            var gil = CachedItems.FirstOrDefault(x => x.Page == InventoryType.FreeCompanyGil);
            if (gil != null)
            {
                sb.AppendLine($"Gil: {gil.Quantity:N0}");
            }
            else
            {
                 sb.AppendLine("Gil: Not Scanned / 0");
            }
            
            var crystals = CachedItems.Where(x => x.Page == InventoryType.FreeCompanyCrystals).ToList();
            if (crystals.Count > 0)
            {
                sb.AppendLine("Crystals:");
                foreach (var c in crystals)
                {
                    try
                    {
                        var sheet = Plugin.Data.GetExcelSheet<Item>();
                        var name = sheet?.GetRowOrDefault(c.ItemId)?.Name.ToString() ?? $"Item#{c.ItemId}";
                        sb.AppendLine($"  - {name}: {c.Quantity:N0}");
                    }
                    catch
                    {
                        sb.AppendLine($"  - Item#{c.ItemId}: {c.Quantity:N0}");
                    }
                }
            }
            else if (IsInventoryLoaded(InventoryType.FreeCompanyCrystals))
            {
                sb.AppendLine("Crystals: None");
            }
            else
            {
                sb.AppendLine("Crystals: None / Not Scanned");
            }

            return sb.ToString().TrimEnd();
        }

        public void Dispose()
        {
            _inventoryScanner?.Dispose();
        }
    }
}
