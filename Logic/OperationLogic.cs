using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using FC_Chest_Helper.Common;
using FC_Chest_Helper.Managers;
using FC_Chest_Helper.Models;

namespace FC_Chest_Helper.Logic
{
    public unsafe static class OperationLogic
    {

        public static (InventoryType, int)? FindEmptyFCSlot(List<ChestManager.ScannedSlot> virtualFC, List<InventoryType> availableTabs, InventoryType? preferredPage = null, bool strict = false)
        {
            if (preferredPage.HasValue && availableTabs.Contains(preferredPage.Value))
            {
                for (int i = 0; i < Constants.FC_CHEST_PAGE_SIZE; i++)
                {
                    bool occupied = virtualFC.Any(x => x.Page == preferredPage.Value && x.Slot == i);
                    if (!occupied)
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
                    bool occupied = virtualFC.Any(x => x.Page == page && x.Slot == i);
                    if (!occupied)
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
            var availableTabs = chestManager.GetAvailableTabs();
            LastDepositOverflow.Clear();

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
                        var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var row = sheet?.GetRowOrDefault(item->ItemId);
                        if (row != null)
                        {
                            itemMaxStack = row.Value.StackSize;
                            isUntradable = row.Value.IsUntradable;
                        }
                    }
                    catch { /* Fall back to defaults if lookup fails */ }

                    if (isUntradable) continue;

                    if (config.IgnoreList.Any(x => x.ItemId == item->ItemId && x.IgnoreEntrust)) continue;

                    uint remainingToDeposit = (uint)item->Quantity;

                    bool isHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality;
                    uint srcSlot = (uint)i;

                    var partialStacks = virtualFC
                        .Where(x => x.ItemId == item->ItemId && x.Quantity < x.MaxStack)
                        .OrderBy(x => x.Page).ThenBy(x => x.Slot)
                        .ToList();
                    
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

                        virtualFC.Add(new ChestManager.ScannedSlot
                        {
                            Page = empty.Value.Item1,
                            Slot = (uint)empty.Value.Item2,
                            ItemId = item->ItemId,
                            Quantity = transfer,
                            IsHq = isHq,
                            MaxStack = itemMaxStack
                        });

                        remainingToDeposit -= transfer;
                    }

                    if (remainingToDeposit > 0)
                    {
                        bool existsInFC = virtualFC.Any(x => x.ItemId == item->ItemId);
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

            foreach (var type in playerInvTypes)
            {
                var container = chestManager.GetContainer(type);
                if (container == null) continue;

                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;

                    // Deposit full quantity (Leave 1 only applies to FC chest withdrawals)
                    uint remainingToDeposit = (uint)item->Quantity;

                    uint itemMaxStack = 999;
                    try
                    {
                        var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var row = sheet?.GetRowOrDefault(item->ItemId);
                        if (row != null)
                        {
                            itemMaxStack = row.Value.StackSize;
                        }
                    }
                    catch { }

                    if (config.IgnoreList.Any(x => x.ItemId == item->ItemId && x.IgnoreEntrust)) continue;

                    uint srcSlot = (uint)i;

                    var partialStacks = virtualFC
                        .Where(x => x.ItemId == item->ItemId && x.Quantity < x.MaxStack)
                        .OrderBy(x => x.Page).ThenBy(x => x.Slot)
                        .ToList();

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
                        var presentPages = virtualFC
                            .Where(x => x.ItemId == item->ItemId)
                            .Select(x => x.Page)
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();

                        if (presentPages.Count > 0)
                        {
                            foreach (var targetPage in presentPages)
                            {
                                if (remainingToDeposit == 0) break;

                                for (uint s = 0; s < Constants.FC_CHEST_PAGE_SIZE; s++)
                                {
                                    if (remainingToDeposit == 0) break;

                                    bool occupied = virtualFC.Any(x => x.Page == targetPage && x.Slot == s);
                                    if (!occupied)
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

                                        virtualFC.Add(new ChestManager.ScannedSlot
                                        {
                                            Page = targetPage,
                                            Slot = s,
                                            ItemId = item->ItemId,
                                            Quantity = transfer,
                                            IsHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality,
                                            MaxStack = itemMaxStack
                                        });

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

                        var currentSlot = playerSlots[(dst.Type, dst.Slot)];
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
            // Try to stack
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

                    if (quantities[key] == 0) // Empty
                    {
                        return (type, i);
                    }
                }
            }

            return (InventoryType.Invalid, 0u);
        }
    }
}
