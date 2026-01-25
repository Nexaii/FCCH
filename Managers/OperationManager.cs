using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using FCCH.Common;
using FCCH.Models;

namespace FCCH.Managers
{
    public unsafe static class OperationManager
    {
        public static (InventoryType, int)? FindEmptyFCSlot(List<ChestManager.ScannedSlot> virtualFC, List<InventoryType> availableTabs, InventoryType? preferredPage = null, bool strict = false)
        {
            var occupiedSlots = new HashSet<(InventoryType, int)>();
            foreach (var slot in virtualFC)
                occupiedSlots.Add((slot.Page, (int)slot.Slot));

            if (preferredPage.HasValue && availableTabs.Contains(preferredPage.Value))
            {
                for (int i = 0; i < Constants.FC_CHEST_PAGE_SIZE; i++)
                {
                    if (!occupiedSlots.Contains((preferredPage.Value, i)))
                    {
                        return (preferredPage.Value, i);
                    }
                }
            }

            if (strict) return null;

            foreach (var page in availableTabs)
            {
                if (preferredPage.HasValue && page == preferredPage.Value) continue;

                for (int i = 0; i < Constants.FC_CHEST_PAGE_SIZE; i++)
                {
                    if (!occupiedSlots.Contains((page, i)))
                    {
                        return (page, i);
                    }
                }
            }
            return null;
        }    

        public static List<(uint ItemId, uint Remaining)> LastDepositOverflow { get; private set; } = new();

        public static List<MoveOperation> CalculateDepositMoves(
            ChestManager chestManager,
            Configuration config,
            InventoryType[] playerInvTypes)
        {
            var moves = new List<MoveOperation>();
            var virtualFC = chestManager.CachedItems.ToList();
            var availableTabs = chestManager.GetDepositableTabs();
            LastDepositOverflow.Clear();

            var stacksByItemId = new Dictionary<uint, List<ChestManager.ScannedSlot>>();
            var occupiedSlots = new HashSet<(InventoryType, uint)>();
            foreach (var slot in virtualFC)
            {
                occupiedSlots.Add((slot.Page, slot.Slot));
                if (!stacksByItemId.ContainsKey(slot.ItemId))
                    stacksByItemId[slot.ItemId] = new List<ChestManager.ScannedSlot>();
                stacksByItemId[slot.ItemId].Add(slot);
            }

            var itemSheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();

            foreach (var type in playerInvTypes)
            {
                var container = chestManager.GetContainer(type);
                if (container == null) continue;

                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;

                    uint itemMaxStack = 999;
                    bool isUntradable = false;
                    try
                    {
                        var row = itemSheet?.GetRowOrDefault(item->ItemId);
                        if (row != null)
                        {
                            itemMaxStack = row.Value.StackSize;
                            isUntradable = row.Value.IsUntradable;
                        }
                    }
                    catch { }

                    if (isUntradable) continue;

                    if (config.CrystalConfig.EnabledIds.Contains(item->ItemId)) continue;

                    if (config.IgnoreList.Any(x => x.ItemId == item->ItemId && x.IgnoreEntrust)) continue;

                    uint remainingToDeposit = (uint)item->Quantity;

                    bool isHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality;
                    uint srcSlot = (uint)i;

                    var partialStacks = stacksByItemId.TryGetValue(item->ItemId, out var stacks)
                        ? stacks.Where(x => x.Quantity < x.MaxStack).OrderBy(x => x.Page).ThenBy(x => x.Slot).ToList()
                        : new List<ChestManager.ScannedSlot>();
                    
                    foreach (var stack in partialStacks)
                    {
                        if (remainingToDeposit == 0) break;

                        uint space = stack.MaxStack - stack.Quantity;
                        uint transfer = Math.Min(remainingToDeposit, space);

                        if (transfer > 0)
                        {
                            moves.Add(new MoveOperation
                            {
                                SrcInv = type,
                                SrcSlot = srcSlot,
                                DstInv = stack.Page,
                                DstSlot = stack.Slot,
                                ItemId = item->ItemId,
                                Amount = transfer,
                                IsNativeMove = true
                            });

                            stack.Quantity += transfer;
                            remainingToDeposit -= transfer;
                        }
                    }

                    while (remainingToDeposit > 0)
                    {
                        var empty = FindEmptyFCSlot(virtualFC, availableTabs.ToList());
                        if (empty == null) break;

                        uint transfer = Math.Min(remainingToDeposit, itemMaxStack);

                        moves.Add(new MoveOperation
                        {
                            SrcInv = type,
                            SrcSlot = srcSlot,
                            DstInv = empty.Value.Item1,
                            DstSlot = (uint)empty.Value.Item2,
                            ItemId = item->ItemId,
                            Amount = transfer,
                            IsNativeMove = true
                        });

                        var newSlot = new ChestManager.ScannedSlot
                        {
                            Page = empty.Value.Item1,
                            Slot = (uint)empty.Value.Item2,
                            ItemId = item->ItemId,
                            Quantity = transfer,
                            IsHq = isHq,
                            MaxStack = itemMaxStack
                        };
                        virtualFC.Add(newSlot);
                        occupiedSlots.Add((newSlot.Page, newSlot.Slot));
                        if (!stacksByItemId.ContainsKey(newSlot.ItemId))
                            stacksByItemId[newSlot.ItemId] = new List<ChestManager.ScannedSlot>();
                        stacksByItemId[newSlot.ItemId].Add(newSlot);

                        remainingToDeposit -= transfer;
                    }

                    if (remainingToDeposit > 0)
                    {
                        bool existsInFC = stacksByItemId.ContainsKey(item->ItemId);
                        if (existsInFC)
                        {
                            var existing = LastDepositOverflow.FindIndex(x => x.ItemId == item->ItemId);
                            if (existing >= 0)
                                LastDepositOverflow[existing] = (item->ItemId, LastDepositOverflow[existing].Remaining + remainingToDeposit);
                            else
                                LastDepositOverflow.Add((item->ItemId, remainingToDeposit));
                        }
                    }
                }
            }

            if (LastDepositOverflow.Count > 0)
            {
                var failureCounts = new Dictionary<string, int>();
                failureCounts["Inventory/Stack Full"] = LastDepositOverflow.Count;
                ChatHelper.PrintBatchWarnings(failureCounts);
            }

            moves.Sort((a, b) => a.DstInv.CompareTo(b.DstInv) != 0 ? a.DstInv.CompareTo(b.DstInv) : a.DstSlot.CompareTo(b.DstSlot));

            return moves;
        }

        public static List<MoveOperation> CalculateDuplicateMoves(
            ChestManager chestManager,
            Configuration config,
            InventoryType[] playerInvTypes)
        {
            var moves = new List<MoveOperation>();
            var virtualFC = chestManager.CachedItems.ToList();
            var availableTabs = chestManager.GetDepositableTabs();

            var stacksByItemId = new Dictionary<uint, List<ChestManager.ScannedSlot>>();
            var occupiedSlots = new HashSet<(InventoryType, uint)>();
            var pagesByItemId = new Dictionary<uint, HashSet<InventoryType>>();
            foreach (var slot in virtualFC)
            {
                occupiedSlots.Add((slot.Page, slot.Slot));
                if (!stacksByItemId.ContainsKey(slot.ItemId))
                    stacksByItemId[slot.ItemId] = new List<ChestManager.ScannedSlot>();
                stacksByItemId[slot.ItemId].Add(slot);
                if (!pagesByItemId.ContainsKey(slot.ItemId))
                    pagesByItemId[slot.ItemId] = new HashSet<InventoryType>();
                pagesByItemId[slot.ItemId].Add(slot.Page);
            }

            var itemSheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();

            foreach (var type in playerInvTypes)
            {
                var container = chestManager.GetContainer(type);
                if (container == null) continue;

                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;

                    uint remainingToDeposit = (uint)item->Quantity;

                    uint itemMaxStack = 999;
                    try
                    {
                        var row = itemSheet?.GetRowOrDefault(item->ItemId);
                        if (row != null)
                        {
                            itemMaxStack = row.Value.StackSize;
                        }
                    }
                    catch { }

                    if (config.CrystalConfig.EnabledIds.Contains(item->ItemId)) continue;

                    if (config.IgnoreList.Any(x => x.ItemId == item->ItemId && x.IgnoreEntrust)) continue;

                    uint srcSlot = (uint)i;

                    var partialStacks = stacksByItemId.TryGetValue(item->ItemId, out var stacks)
                        ? stacks.Where(x => x.Quantity < x.MaxStack && availableTabs.Contains(x.Page)).OrderBy(x => x.Page).ThenBy(x => x.Slot).ToList()
                        : new List<ChestManager.ScannedSlot>();

                    foreach (var stack in partialStacks)
                    {
                        if (remainingToDeposit == 0) break;

                        uint space = stack.MaxStack - stack.Quantity;
                        uint transfer = Math.Min(remainingToDeposit, space);

                        if (transfer > 0)
                        {
                            moves.Add(new MoveOperation
                            {
                                SrcInv = type,
                                SrcSlot = srcSlot,
                                DstInv = stack.Page,
                                DstSlot = stack.Slot,
                                ItemId = item->ItemId,
                                Amount = transfer,
                                IsNativeMove = true
                            });

                            stack.Quantity += transfer;
                            remainingToDeposit -= transfer;
                        }
                    }

                    if (remainingToDeposit > 0)
                    {
                        var presentPages = pagesByItemId.TryGetValue(item->ItemId, out var pages)
                            ? pages.Where(p => availableTabs.Contains(p)).OrderBy(x => x).ToList()
                            : new List<InventoryType>();

                        if (presentPages.Count > 0)
                        {
                            foreach (var targetPage in presentPages)
                            {
                                if (remainingToDeposit == 0) break;

                                for (uint s = 0; s < Constants.FC_CHEST_PAGE_SIZE; s++)
                                {
                                    if (remainingToDeposit == 0) break;

                                    if (!occupiedSlots.Contains((targetPage, s)))
                                    {
                                        uint transfer = Math.Min(remainingToDeposit, itemMaxStack);

                                        moves.Add(new MoveOperation
                                        {
                                            SrcInv = type,
                                            SrcSlot = srcSlot,
                                            DstInv = targetPage,
                                            DstSlot = s,
                                            ItemId = item->ItemId,
                                            Amount = transfer,
                                            IsNativeMove = true
                                        });

                                        var newSlot = new ChestManager.ScannedSlot
                                        {
                                            Page = targetPage,
                                            Slot = s,
                                            ItemId = item->ItemId,
                                            Quantity = transfer,
                                            IsHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality,
                                            MaxStack = itemMaxStack
                                        };
                                        virtualFC.Add(newSlot);
                                        occupiedSlots.Add((targetPage, s));
                                        if (!stacksByItemId.ContainsKey(item->ItemId))
                                            stacksByItemId[item->ItemId] = new List<ChestManager.ScannedSlot>();
                                        stacksByItemId[item->ItemId].Add(newSlot);

                                        remainingToDeposit -= transfer;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            moves.Sort((a, b) => a.DstInv.CompareTo(b.DstInv) != 0 ? a.DstInv.CompareTo(b.DstInv) : a.DstSlot.CompareTo(b.DstSlot));

            return moves;
        }

        public static List<(uint ItemId, uint Remaining)> LastWithdrawOverflow { get; private set; } = new();

        public static List<MoveOperation> CalculateWithdrawMoves(
            ChestManager chestManager,
            Configuration config,
            Dictionary<uint, int> itemsToWithdraw,
            InventoryType[] playerInvTypes,
            bool ignoreLeaveOneRule = false)
        {
            var moves = new List<MoveOperation>();
            LastWithdrawOverflow.Clear();
            
            var playerSlots = new Dictionary<(InventoryType, uint), (uint ItemId, uint Quantity, bool IsHq)>();
            foreach (var type in playerInvTypes)
            {
                var container = chestManager.GetContainer(type);
                if (container == null) continue;
                for (uint i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot((int)i);
                    if (item == null || item->ItemId == 0)
                        playerSlots[(type, i)] = (0, 0, false);
                    else
                        playerSlots[(type, i)] = (item->ItemId, (uint)item->Quantity, (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality);
                }
            }

            foreach (var req in itemsToWithdraw)
            {
                uint itemId = req.Key;
                
                if (config.CrystalConfig.EnabledIds.Contains(itemId)) continue;

                if (config.IgnoreList.Any(x => x.ItemId == itemId && x.IgnoreWithdraw)) continue;

                int amountNeeded = req.Value;

                var chestItems = chestManager.CachedItems
                    .Where(x => x.ItemId == itemId)
                    .OrderByDescending(x => x.Quantity)
                    .ToList();

                foreach (var chestSlot in chestItems)
                {
                    if (amountNeeded <= 0) break;

                    uint availableFromSlot = chestSlot.Quantity;
                    if (!ignoreLeaveOneRule && config.LeaveOneItemPerStack)
                    {
                        if (availableFromSlot > 0) availableFromSlot--;
                    }

                    uint remainingFromThisSlot = (uint)Math.Min(amountNeeded, (int)availableFromSlot);
                    
                    while (remainingFromThisSlot > 0)
                    {
                        var dst = FindSpaceInPlayerInventory(playerSlots, itemId, chestSlot.IsHq, playerInvTypes, 1, chestSlot.MaxStack);
                        if (dst.Type == InventoryType.Invalid) break;

                        if (!playerSlots.TryGetValue((dst.Type, dst.Slot), out var currentSlot)) break;
                        uint playerSpace = chestSlot.MaxStack - currentSlot.Quantity;
                        uint transfer = Math.Min(remainingFromThisSlot, playerSpace);

                        if (transfer == 0) break;

                        moves.Add(new MoveOperation
                        {
                            SrcInv = chestSlot.Page,
                            SrcSlot = chestSlot.Slot,
                            DstInv = dst.Type,
                            DstSlot = dst.Slot,
                            ItemId = itemId,
                            Amount = transfer,
                            IsNativeMove = true
                        });

                        playerSlots[(dst.Type, dst.Slot)] = (itemId, currentSlot.Quantity + transfer, chestSlot.IsHq);
                        
                        remainingFromThisSlot -= transfer;
                        amountNeeded -= (int)transfer;
                    }

                    if (remainingFromThisSlot > 0)
                    {
                        var existing = LastWithdrawOverflow.FindIndex(x => x.ItemId == itemId);
                        if (existing >= 0)
                            LastWithdrawOverflow[existing] = (itemId, LastWithdrawOverflow[existing].Remaining + remainingFromThisSlot);
                        else
                            LastWithdrawOverflow.Add((itemId, remainingFromThisSlot));
                    }
                }
            }

            if (LastWithdrawOverflow.Count > 0)
            {
                var failureCounts = new Dictionary<string, int>();
                failureCounts["Player Inventory Full"] = LastWithdrawOverflow.Count;
                ChatHelper.PrintBatchWarnings(failureCounts);
            }

            moves.Sort((a, b) => a.SrcInv.CompareTo(b.SrcInv) != 0 ? a.SrcInv.CompareTo(b.SrcInv) : a.SrcSlot.CompareTo(b.SrcSlot));

            return moves;
        }

        private static (InventoryType Type, uint Slot) FindSpaceInPlayerInventory(
            Dictionary<(InventoryType, uint), (uint ItemId, uint Quantity, bool IsHq)> slots,
            uint itemId,
            bool isHq,
            InventoryType[] types,
            int amountToAdd,
            uint maxStack = 999)
        {
            foreach (var type in types)
            {
                for (uint i = 0; i < Constants.PLAYER_INVENTORY_PAGE_SIZE; i++)
                {
                    var key = (type, i);
                    if (slots.TryGetValue(key, out var slot))
                    {
                        if (slot.ItemId == itemId && slot.IsHq == isHq && slot.Quantity + amountToAdd <= maxStack)
                        {
                            return (type, i);
                        }
                    }
                }
            }

            foreach (var type in types)
            {
                for (uint i = 0; i < Constants.PLAYER_INVENTORY_PAGE_SIZE; i++)
                {
                    var key = (type, i);
                    if (slots.TryGetValue(key, out var slot))
                    {
                        if (slot.ItemId == 0) return (type, i);
                    }
                }
            }

            return (InventoryType.Invalid, 0);
        }

         public static (InventoryType, uint) FindVirtualSpace(
            Dictionary<(InventoryType, uint), uint> quantities, 
            Dictionary<(InventoryType, uint), uint> itemIds, 
            Dictionary<(InventoryType, uint), bool> itemHqs,
            uint itemId, 
            bool isHq,
            InventoryType[] types,
            bool stackOnly = false,
            bool emptyOnly = false)
        {
            if (!emptyOnly)
            {
                foreach (var type in types)
                {
                    for (uint i = 0; i < Constants.PLAYER_INVENTORY_PAGE_SIZE; i++)
                    {
                        var key = (type, i);
                        if (!quantities.ContainsKey(key)) continue;

                        uint currentQty = quantities[key];
                        uint currentId = itemIds[key];
                        bool currentHq = itemHqs.ContainsKey(key) ? itemHqs[key] : false;

                        if (currentId == itemId && currentHq == isHq)
                        {
                            if (currentQty < 999)
                            {
                                return (type, i);
                            }
                        }
                    }
                }
            }
            
            if (stackOnly) return (InventoryType.Invalid, 0u);

            foreach (var type in types)
            {
                for (uint i = 0; i < Constants.FC_CHEST_PAGE_SIZE; i++)
                {
                    var key = (type, i);
                    if (!quantities.ContainsKey(key)) continue;

                    if (quantities[key] == 0) 
                    {
                        return (type, i);
                    }
                }
            }

            return (InventoryType.Invalid, 0u);
        }
    }
}
