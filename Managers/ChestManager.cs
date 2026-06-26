using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using FCCH.Common;
using FCCH.GameData;

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
                FCCH.Common.FCCHLog.Error($"[SwitchToPage] Invalid target index {targetIndex} for page {targetPage}");
                return;
            }

            var values = stackalloc AtkValue[2];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
            values[0].Int = 0;
            values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
            values[1].Int = targetIndex;

            addon->FireCallback((uint)Constants.FC_CHEST_CALLBACK_ID, values);

            if (targetPage != InventoryType.FreeCompanyGil)
            {
                GameMain.ExecuteCommand(404, (int)targetPage);
            }
        }

        public Dictionary<InventoryType, List<ScannedSlot>> ChestState { get; private set; } = new();
        public List<ScannedSlot> CachedItems { get; private set; } = new();

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
            _inventoryScanner = new InventoryScanner(config);
        }

        public int ScanFCChest()
        {
            Common.PerfCounter.RecordScanFCChest();
            _inventoryScanner.Update();
            CachedItems.Clear();

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

                    uint maxStack = ItemStackCache.GetMaxStack(item->ItemId);

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

                var addon = Common.ChestAddon.GetOpen();
                bool isActivePage = (addon != null && GetCurrentFCPage(addon) == type);

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
            Common.PerfCounter.RecordUpdateChestState();
            _inventoryScanner.Update();
            var container = _inventoryScanner.GetContainer(page);
            if (container == null) return;

            var items = new List<ScannedSlot>();
            for (int i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || (item->ItemId == 0 && item->Quantity == 0)) continue;

                uint maxStack = ItemStackCache.GetMaxStack(item->ItemId);

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
                var addon = Common.ChestAddon.GetOpen();
                if (addon != null)
                {
                    var p = (byte*)addon;
                    uint packedItems = *(uint*)(p + 0x4DC);
                    uint raw = page switch
                    {
                        InventoryType.FreeCompanyPage1    => packedItems & 3,
                        InventoryType.FreeCompanyPage2    => (packedItems >> 2) & 3,
                        InventoryType.FreeCompanyPage3    => (packedItems >> 4) & 3,
                        InventoryType.FreeCompanyPage4    => (packedItems >> 6) & 3,
                        InventoryType.FreeCompanyPage5    => (packedItems >> 8) & 3,
                        InventoryType.FreeCompanyCrystals => *(uint*)(p + 0x4E0),
                        InventoryType.FreeCompanyGil      => *(uint*)(p + 0x4E4),
                        _ => 0
                    };
                    return MapAddonAccess(raw);
                }

                return Constants.FCPermissions.FULL_ACCESS;
            }
            catch { return Constants.FCPermissions.NO_ACCESS; }
        }

        private static byte MapAddonAccess(uint raw) => raw switch
        {
            0 => Constants.FCPermissions.NO_ACCESS,
            1 => Constants.FCPermissions.VIEW_ONLY,
            2 => Constants.FCPermissions.DEPOSIT_ONLY,
            3 => Constants.FCPermissions.FULL_ACCESS,
            _ => Constants.FCPermissions.NO_ACCESS,
        };

        public static byte DecodeChestAccess(System.Span<byte> p, InventoryType page)
        {
            int combined = page switch
            {
                InventoryType.FreeCompanyPage1    => ((p[1] & 0x80) >> 7) | ((p[2] & 0x03) << 1) | ((p[4] & 0x10) >> 1),
                InventoryType.FreeCompanyPage2    => ((p[2] & 0x1C) >> 2) | ((p[4] & 0x20) >> 2),
                InventoryType.FreeCompanyPage3    => ((p[2] & 0xE0) >> 5) | ((p[4] & 0x40) >> 3),
                InventoryType.FreeCompanyPage4    => (p[3] & 0x07) | ((p[4] & 0x80) >> 4),
                InventoryType.FreeCompanyPage5    => ((p[3] & 0x38) >> 3) | ((p[5] & 0x01) << 3),
                InventoryType.FreeCompanyCrystals => ((p[3] & 0xC0) >> 6) | ((p[4] & 0x01) << 2) | ((p[5] & 0x02) << 2),
                InventoryType.FreeCompanyGil      => ((p[4] & 0x0E) >> 1) | ((p[5] & 0x04) << 1),
                _ => 0
            };

            if ((combined & Constants.FCPermissions.FULL_ACCESS)  != 0) return Constants.FCPermissions.FULL_ACCESS;
            if ((combined & Constants.FCPermissions.DEPOSIT_ONLY) != 0) return Constants.FCPermissions.DEPOSIT_ONLY;
            if ((combined & Constants.FCPermissions.VIEW_ONLY)    != 0) return Constants.FCPermissions.VIEW_ONLY;
            return Constants.FCPermissions.NO_ACCESS;
        }

        public void DumpRawPermissions(byte? overrideRank = null)
        {
            try
            {
                var uiModule = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
                if (uiModule == null) { FCCH.Common.FCCHLog.Warning("[FCPerms] UIModule null."); return; }

                var infoModule = uiModule->GetInfoModule();
                if (infoModule == null) { FCCH.Common.FCCHLog.Warning("[FCPerms] InfoModule null."); return; }

                var fcProxy = (InfoProxyFreeCompany*)infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
                if (fcProxy == null) { FCCH.Common.FCCHLog.Warning("[FCPerms] FreeCompany proxy null."); return; }

                byte playerRank = fcProxy->Rank;
                FCCH.Common.FCCHLog.Info($"[FCPerms] PlayerRank field = {playerRank} (0x{playerRank:X2})");

                if (overrideRank.HasValue)
                {
                    if (overrideRank.Value >= 14) { FCCH.Common.FCCHLog.Warning($"[FCPerms] Override rank {overrideRank.Value} out of range."); return; }
                    DumpRankRow(fcProxy, overrideRank.Value, playerRank);
                    return;
                }

                for (byte i = 0; i < 14; i++) DumpRankRow(fcProxy, i, playerRank);
            }
            catch (Exception ex)
            {
                FCCH.Common.FCCHLog.Error(ex, "[FCPerms] Dump failed.");
            }
        }

        public string DumpAccessProbe()
        {
            var addon = Common.ChestAddon.GetOpen();
            if (addon == null)
            {
                const string closed = "[AccessProbe] Company Chest addon is not open.";
                FCCH.Common.FCCHLog.Info(closed);
                return closed;
            }

            var p = (byte*)addon;
            byte mode = *(p + 0x4D8);
            byte selectedTab = *(p + 0x4D9);
            byte page = *(p + 0x4DA);
            uint packedItems = *(uint*)(p + 0x4DC);
            uint crystal = *(uint*)(p + 0x4E0);
            uint gil = *(uint*)(p + 0x4E4);
            uint action = *(uint*)(p + 0x4E8);
            byte visibleMode = *(p + 0x4ED);
            uint visibleMask = *(uint*)(p + 0x4F0);
            byte limitedMaskMode = *(p + 0x4F4);

            uint tab1 = packedItems & 3;
            uint tab2 = (packedItems >> 2) & 3;
            uint tab3 = (packedItems >> 4) & 3;
            uint tab4 = (packedItems >> 6) & 3;
            uint tab5 = (packedItems >> 8) & 3;

            string message = $"[AccessProbe] mode={mode} selectedTab={selectedTab} page={page} packedItems=0x{packedItems:X8} tabs=[1:{tab1},2:{tab2},3:{tab3},4:{tab4},5:{tab5}] crystal={crystal} gil={gil} action={action} visibleMode={visibleMode} visibleMask=0x{visibleMask:X8} limitedMaskMode={limitedMaskMode}";
            FCCH.Common.FCCHLog.Info(message);
            if (_configuration.DebugMode)
                DebugFileLogger.Enqueue(_configuration.DebugLogPath, message);
            return message;
        }

        private void DumpRankRow(InfoProxyFreeCompany* fcProxy, byte rankIndex, byte playerRank)
        {
            var rd = fcProxy->Ranks[rankIndex];
            var p = rd.Permissions;

            var hex = new System.Text.StringBuilder();
            for (int i = 0; i < 10; i++) hex.Append($" {(byte)p[i]:X2}");

            FCCH.Common.FCCHLog.Info($"[FCPerms] PlayerRank={playerRank} dumpRank={rankIndex} RankNumber={rd.RankNumber} MemberCount={rd.MemberCount} Bytes:{hex}");

            byte d1 = DecodeChestAccess(p, InventoryType.FreeCompanyPage1);
            byte d2 = DecodeChestAccess(p, InventoryType.FreeCompanyPage2);
            byte d3 = DecodeChestAccess(p, InventoryType.FreeCompanyPage3);
            byte d4 = DecodeChestAccess(p, InventoryType.FreeCompanyPage4);
            byte d5 = DecodeChestAccess(p, InventoryType.FreeCompanyPage5);
            byte dc = DecodeChestAccess(p, InventoryType.FreeCompanyCrystals);
            byte dg = DecodeChestAccess(p, InventoryType.FreeCompanyGil);

            FCCH.Common.FCCHLog.Info($"[FCPerms] Decoded Items1={NameAccess(d1)}({d1}) Items2={NameAccess(d2)}({d2}) Items3={NameAccess(d3)}({d3}) Items4={NameAccess(d4)}({d4}) Items5={NameAccess(d5)}({d5}) Crystals={NameAccess(dc)}({dc}) Gil={NameAccess(dg)}({dg})");
            FCCH.Common.FCCHLog.Info($"[FCPerms] CS-getter (buggy upstream, for comparison) Items1={(byte)rd.Items1} Items2={(byte)rd.Items2} Items3={(byte)rd.Items3} Items4={(byte)rd.Items4} Items5={(byte)rd.Items5} Crystals={(byte)rd.Crystals} Gil={(byte)rd.Gil}");
        }

        public static string NameAccess(byte v) => v switch
        {
            Constants.FCPermissions.NO_ACCESS => "No Access",
            Constants.FCPermissions.VIEW_ONLY => "View Only",
            Constants.FCPermissions.FULL_ACCESS => "Full Access",
            Constants.FCPermissions.DEPOSIT_ONLY => "Deposit Only",
            _ => $"Unknown ({v})",
        };

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
            var addon = Common.ChestAddon.GetOpen();
            if (addon != null)
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
            var result = new List<InventoryType>();
            foreach (var tab in GetAvailableTabs())
            {
                if (tab == InventoryType.FreeCompanyGil || tab == InventoryType.FreeCompanyCrystals) continue;
                var access = GetChestAccess(tab);
                if (access == Constants.FCPermissions.FULL_ACCESS || access == Constants.FCPermissions.DEPOSIT_ONLY)
                    result.Add(tab);
            }
            return result;
        }

        public List<InventoryType> GetWithdrawableTabs()
        {
            var result = new List<InventoryType>();
            foreach (var tab in GetAvailableTabs())
            {
                if (tab == InventoryType.FreeCompanyGil || tab == InventoryType.FreeCompanyCrystals) continue;
                if (GetChestAccess(tab) == Constants.FCPermissions.FULL_ACCESS)
                    result.Add(tab);
            }
            return result;
        }

        public void ClearPage(InventoryType page)
        {
            ChestState.Remove(page);
            CachedItems.RemoveAll(x => x.Page == page);
        }

        private bool IsNodeVisible(AtkUnitBase* addon, uint nodeId)
        {
             if (nodeId >= addon->UldManager.NodeListCount) return false;
             var node = addon->UldManager.NodeList[nodeId];
             return node != null && node->IsVisible();
        }


        
        public long GetItemCountInPlayerInventory(uint itemId)
        {
            long count = 0;
            foreach (var type in Constants.PlayerInventoryTypes)
            {
                var container = GetContainer(type);
                if (container == null) continue;
                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item != null && item->ItemId == itemId) count += item->Quantity;
                }
            }
            return count;
        }

        public InventoryContainer* GetContainer(InventoryType type) => _inventoryScanner.GetContainer(type);
        public bool IsInventoryLoaded(InventoryType type) => _inventoryScanner.IsInventoryLoaded(type);
        public void ResetIndexingSession() => _inventoryScanner.ResetSession();
        public void MarkInventoryObserved(InventoryType type) => _inventoryScanner.MarkObserved(type);

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
