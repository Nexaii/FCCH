using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using FCCH.Common;
using FCCH.Models;

namespace FCCH.Managers.Organizer
{
    public unsafe class OrgValidator
    {
        private readonly ChestManager _chestManager;
        private readonly Configuration _config;

        public OrgValidator(ChestManager chestManager, Configuration config)
        {
            _chestManager = chestManager;
            _config = config;
        }

        public OrgCheckResult Check(OrgJobRequest request)
        {
            var result = new OrgCheckResult();

            bool sourceIsPlayer = IsPlayerInventory(request.SourceTab);
            bool destIsPlayer = IsPlayerInventory(request.DestTab);

            if (request.Mode == OrgOperationMode.Sort && request.SourceTab != request.DestTab)
            {
                result.StatusMessage = "Sort mode requires source and destination to be the same tab.";
                return result;
            }

            if (request.SourceTab == request.DestTab && request.Mode != OrgOperationMode.Sort)
            {
                result.StatusMessage = "Source and destination cannot be the same for Move operations.";
                return result;
            }

            var sourceItems = GetItems(request.SourceTab)
                .Where(OrgFilters.GetMultiPredicate(request.Filters))
                .ToList();

            if (!request.SelectAll && request.SelectedItemIds.Count > 0)
            {
                sourceItems = sourceItems.Where(x => request.SelectedItemIds.Contains(x.ItemId)).ToList();
            }

            if (sourceItems.Count == 0)
            {
                result.StatusMessage = "No items match the current filter.";
                return result;
            }

            var sortedItems = OrgFilters.ApplySortOrder(sourceItems, request.SortOrder, request.SortDescending).ToList();
            result.StackCount = sortedItems.Count;
            result.PlayerFreeSlots = CountPlayerFreeSlots();

            List<ChestManager.ScannedSlot> destItems;
            if (request.Mode == OrgOperationMode.Sort)
            {
                destItems = new List<ChestManager.ScannedSlot>();
                result.DestFreeSlots = sourceIsPlayer ? Constants.PLAYER_INVENTORY_PAGE_SIZE * 4 : Constants.FC_CHEST_PAGE_SIZE;
                result.NetSlotsNeeded = sortedItems.Count;
            }
            else if (destIsPlayer)
            {
                destItems = new List<ChestManager.ScannedSlot>();
                result.DestFreeSlots = result.PlayerFreeSlots;
                result.NetSlotsNeeded = sortedItems.Count;
            }
            else
            {
                destItems = _chestManager.CachedItems
                    .Where(x => x.Page == request.DestTab)
                    .ToList();
                result.DestFreeSlots = Constants.FC_CHEST_PAGE_SIZE - destItems.Count;
                result.NetSlotsNeeded = CalculateNetSlotsNeeded(sortedItems, destItems);
            }

            if (sourceIsPlayer)
                result.PlayerBufferOK = true;
            else
                result.PlayerBufferOK = result.PlayerFreeSlots >= result.StackCount;

            result.DestCapacityOK = result.DestFreeSlots >= result.NetSlotsNeeded;

            foreach (var slot in sortedItems)
            {
                var existsInDest = destItems.Any(d => d.ItemId == slot.ItemId && d.Quantity < d.MaxStack);
                result.PreviewItems.Add(new OrgPreviewItem
                {
                    ItemId = slot.ItemId,
                    ItemName = GetItemName(slot.ItemId),
                    CategoryName = OrgFilters.GetCategoryName(slot.ItemId),
                    Quantity = slot.Quantity,
                    WillMerge = existsInDest,
                    IsSelected = request.SelectAll || request.SelectedItemIds.Contains(slot.ItemId)
                });
            }

            result.PreviewItems = result.PreviewItems
                .GroupBy(x => x.ItemId)
                .Select(g => new OrgPreviewItem
                {
                    ItemId = g.Key,
                    ItemName = g.First().ItemName,
                    CategoryName = g.First().CategoryName,
                    Quantity = (uint)g.Sum(x => x.Quantity),
                    WillMerge = g.Any(x => x.WillMerge),
                    IsSelected = true
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            result.IsValid = result.PlayerBufferOK && result.DestCapacityOK;

            if (result.IsValid)
            {
                result.StatusMessage = "Ready";

                if (sourceIsPlayer && !destIsPlayer)
                {
                    result.WithdrawMoves = new List<MoveOperation>();
                    result.DepositMoves = BuildDepositMovesFromPlayer(sortedItems, request.DestTab, destItems);
                }
                else if (!sourceIsPlayer && destIsPlayer)
                {
                    result.WithdrawMoves = BuildWithdrawMoves(sortedItems);
                    result.DepositMoves = new List<MoveOperation>();
                }
                else if (request.Mode == OrgOperationMode.Sort)
                {
                    result.WithdrawMoves = BuildWithdrawMoves(sortedItems);
                    result.DepositMoves = BuildDepositMoves(sortedItems, request.SourceTab, new List<ChestManager.ScannedSlot>());
                }
                else
                {
                    result.WithdrawMoves = BuildWithdrawMoves(sortedItems);
                    result.DepositMoves = BuildDepositMoves(sortedItems, request.DestTab, destItems);
                }

                foreach (var slot in destItems)
                {
                    if (result.ExpectedCounts.ContainsKey(slot.ItemId))
                        result.ExpectedCounts[slot.ItemId] += slot.Quantity;
                    else
                        result.ExpectedCounts[slot.ItemId] = slot.Quantity;
                }
                foreach (var item in sortedItems)
                {
                    if (result.ExpectedCounts.ContainsKey(item.ItemId))
                        result.ExpectedCounts[item.ItemId] += item.Quantity;
                    else
                        result.ExpectedCounts[item.ItemId] = item.Quantity;
                }
            }
            else
            {
                var issues = new List<string>();
                if (!result.PlayerBufferOK)
                    issues.Add($"Need {result.StackCount} player slots, have {result.PlayerFreeSlots}");
                if (!result.DestCapacityOK)
                    issues.Add($"Need {result.NetSlotsNeeded} dest slots, have {result.DestFreeSlots}");
                result.StatusMessage = string.Join("; ", issues);
            }

            return result;
        }

        private bool IsPlayerInventory(InventoryType type)
        {
            return type == InventoryType.Inventory1 ||
                   type == InventoryType.Inventory2 ||
                   type == InventoryType.Inventory3 ||
                   type == InventoryType.Inventory4;
        }

        private List<ChestManager.ScannedSlot> GetItems(InventoryType type)
        {
            if (IsPlayerInventory(type))
            {
                var items = new List<ChestManager.ScannedSlot>();
                var types = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
                foreach (var invType in types)
                {
                    var container = _chestManager.GetContainer(invType);
                    if (container == null) continue;
                    for (uint i = 0; i < container->Size; i++)
                    {
                        var item = container->GetInventorySlot((int)i);
                        if (item == null || item->ItemId == 0) continue;

                        uint maxStack = 999;
                        try
                        {
                            var sheet = Plugin.Data.GetExcelSheet<Item>();
                            var row = sheet?.GetRowOrDefault(item->ItemId);
                            if (row != null) maxStack = row.Value.StackSize;
                        }
                        catch { }

                        items.Add(new ChestManager.ScannedSlot
                        {
                            Page = invType,
                            Slot = i,
                            ItemId = item->ItemId,
                            Quantity = (uint)item->Quantity,
                            IsHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality,
                            MaxStack = maxStack
                        });
                    }
                }
                return items;
            }
            return _chestManager.CachedItems.Where(x => x.Page == type).ToList();
        }

        private List<MoveOperation> BuildDepositMovesFromPlayer(
            List<ChestManager.ScannedSlot> sourceItems,
            InventoryType destTab,
            List<ChestManager.ScannedSlot> destItems)
        {
            var moves = new List<MoveOperation>();
            var virtualDest = destItems.Select(x => new ChestManager.ScannedSlot
            {
                Page = x.Page,
                Slot = x.Slot,
                ItemId = x.ItemId,
                Quantity = x.Quantity,
                IsHq = x.IsHq,
                MaxStack = x.MaxStack
            }).ToList();

            foreach (var srcSlot in sourceItems)
            {
                uint remaining = srcSlot.Quantity;

                var partialStacks = virtualDest
                    .Where(d => d.ItemId == srcSlot.ItemId && d.Quantity < d.MaxStack)
                    .OrderBy(d => d.Slot)
                    .ToList();

                foreach (var stack in partialStacks)
                {
                    if (remaining == 0) break;
                    uint space = stack.MaxStack - stack.Quantity;
                    uint transfer = Math.Min(remaining, space);

                    moves.Add(new MoveOperation
                    {
                        SrcInv = srcSlot.Page,
                        SrcSlot = srcSlot.Slot,
                        DstInv = destTab,
                        DstSlot = stack.Slot,
                        ItemId = srcSlot.ItemId,
                        Amount = transfer,
                        IsNativeMove = true
                    });

                    stack.Quantity += transfer;
                    remaining -= transfer;
                }

                while (remaining > 0)
                {
                    uint nextSlot = 0;
                    for (uint s = 0; s < Constants.FC_CHEST_PAGE_SIZE; s++)
                    {
                        if (!virtualDest.Any(d => d.Slot == s))
                        {
                            nextSlot = s;
                            break;
                        }
                    }

                    uint transfer = Math.Min(remaining, srcSlot.MaxStack);

                    moves.Add(new MoveOperation
                    {
                        SrcInv = srcSlot.Page,
                        SrcSlot = srcSlot.Slot,
                        DstInv = destTab,
                        DstSlot = nextSlot,
                        ItemId = srcSlot.ItemId,
                        Amount = transfer,
                        IsNativeMove = true
                    });

                    virtualDest.Add(new ChestManager.ScannedSlot
                    {
                        Page = destTab,
                        Slot = nextSlot,
                        ItemId = srcSlot.ItemId,
                        Quantity = transfer,
                        IsHq = srcSlot.IsHq,
                        MaxStack = srcSlot.MaxStack
                    });

                    remaining -= transfer;
                }
            }

            return moves;
        }

        private int CountPlayerFreeSlots()
        {
            int free = 0;
            var types = new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };

            foreach (var type in types)
            {
                var container = _chestManager.GetContainer(type);
                if (container == null) continue;

                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0)
                        free++;
                }
            }
            return free;
        }

        private int CalculateNetSlotsNeeded(
            List<ChestManager.ScannedSlot> sourceItems,
            List<ChestManager.ScannedSlot> destItems)
        {
            var itemGroups = sourceItems.GroupBy(x => x.ItemId);
            int netSlots = 0;

            foreach (var group in itemGroups)
            {
                uint totalToMove = (uint)group.Sum(x => (long)x.Quantity);
                uint itemId = group.Key;
                uint maxStack = group.First().MaxStack;

                var destStacks = destItems.Where(d => d.ItemId == itemId).ToList();
                uint destAvailableSpace = (uint)destStacks.Sum(d => (long)(d.MaxStack - d.Quantity));

                if (totalToMove <= destAvailableSpace)
                    continue;

                uint overflow = totalToMove - destAvailableSpace;
                int newStacksNeeded = (int)Math.Ceiling((double)overflow / maxStack);
                netSlots += newStacksNeeded;
            }

            return netSlots;
        }

        private List<MoveOperation> BuildWithdrawMoves(List<ChestManager.ScannedSlot> sourceItems)
        {
            var moves = new List<MoveOperation>();
            var playerSlots = GetPlayerSlotState();
            var types = new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };

            foreach (var slot in sourceItems)
            {
                uint remaining = slot.Quantity;
                uint originalQuantity = slot.Quantity;

                while (remaining > 0)
                {
                    var dst = FindPlayerSpace(playerSlots, slot.ItemId, slot.IsHq, types, slot.MaxStack);
                    if (dst.Type == InventoryType.Invalid) break;

                    var current = playerSlots[(dst.Type, dst.Slot)];
                    uint space = slot.MaxStack - current.Quantity;
                    uint transfer = Math.Min(remaining, space);

                    bool isFullStack = transfer == originalQuantity && current.Quantity == 0;

                    moves.Add(new MoveOperation
                    {
                        SrcInv = slot.Page,
                        SrcSlot = slot.Slot,
                        DstInv = dst.Type,
                        DstSlot = dst.Slot,
                        ItemId = slot.ItemId,
                        Amount = transfer,
                        IsNativeMove = true
                    });

                    playerSlots[(dst.Type, dst.Slot)] = (slot.ItemId, current.Quantity + transfer, slot.IsHq);
                    remaining -= transfer;
                }
            }

            return moves;
        }

        private List<MoveOperation> BuildDepositMoves(
            List<ChestManager.ScannedSlot> sourceItems,
            InventoryType destTab,
            List<ChestManager.ScannedSlot> destItems)
        {
            var moves = new List<MoveOperation>();
            var virtualDest = destItems.Select(x => new ChestManager.ScannedSlot
            {
                Page = x.Page,
                Slot = x.Slot,
                ItemId = x.ItemId,
                Quantity = x.Quantity,
                IsHq = x.IsHq,
                MaxStack = x.MaxStack
            }).ToList();

            var types = new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };

            foreach (var srcSlot in sourceItems)
            {
                uint remaining = srcSlot.Quantity;

                var partialStacks = virtualDest
                    .Where(d => d.ItemId == srcSlot.ItemId && d.Quantity < d.MaxStack)
                    .OrderBy(d => d.Slot)
                    .ToList();

                foreach (var stack in partialStacks)
                {
                    if (remaining == 0) break;
                    uint space = stack.MaxStack - stack.Quantity;
                    uint transfer = Math.Min(remaining, space);

                    moves.Add(new MoveOperation
                    {
                        SrcInv = types[0],
                        SrcSlot = 0,
                        DstInv = destTab,
                        DstSlot = stack.Slot,
                        ItemId = srcSlot.ItemId,
                        Amount = transfer,
                        IsNativeMove = true
                    });

                    stack.Quantity += transfer;
                    remaining -= transfer;
                }

                while (remaining > 0)
                {
                    uint nextSlot = 0;
                    for (uint s = 0; s < Constants.FC_CHEST_PAGE_SIZE; s++)
                    {
                        if (!virtualDest.Any(d => d.Slot == s))
                        {
                            nextSlot = s;
                            break;
                        }
                    }

                    uint transfer = Math.Min(remaining, srcSlot.MaxStack);

                    moves.Add(new MoveOperation
                    {
                        SrcInv = types[0],
                        SrcSlot = 0,
                        DstInv = destTab,
                        DstSlot = nextSlot,
                        ItemId = srcSlot.ItemId,
                        Amount = transfer,
                        IsNativeMove = true
                    });

                    virtualDest.Add(new ChestManager.ScannedSlot
                    {
                        Page = destTab,
                        Slot = nextSlot,
                        ItemId = srcSlot.ItemId,
                        Quantity = transfer,
                        IsHq = srcSlot.IsHq,
                        MaxStack = srcSlot.MaxStack
                    });

                    remaining -= transfer;
                }
            }

            return moves;
        }

        private Dictionary<(InventoryType, uint), (uint ItemId, uint Quantity, bool IsHq)> GetPlayerSlotState()
        {
            var slots = new Dictionary<(InventoryType, uint), (uint, uint, bool)>();
            var types = new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };

            foreach (var type in types)
            {
                var container = _chestManager.GetContainer(type);
                if (container == null) continue;

                for (uint i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot((int)i);
                    if (item == null || item->ItemId == 0)
                        slots[(type, i)] = (0, 0, false);
                    else
                        slots[(type, i)] = (item->ItemId, (uint)item->Quantity,
                            (item->Flags & InventoryItem.ItemFlags.HighQuality) == InventoryItem.ItemFlags.HighQuality);
                }
            }
            return slots;
        }

        private (InventoryType Type, uint Slot) FindPlayerSpace(
            Dictionary<(InventoryType, uint), (uint ItemId, uint Quantity, bool IsHq)> slots,
            uint itemId,
            bool isHq,
            InventoryType[] types,
            uint maxStack)
        {
            foreach (var type in types)
            {
                for (uint i = 0; i < Constants.PLAYER_INVENTORY_PAGE_SIZE; i++)
                {
                    if (!slots.TryGetValue((type, i), out var slot)) continue;
                    if (slot.ItemId == itemId && slot.IsHq == isHq && slot.Quantity < maxStack)
                        return (type, i);
                }
            }

            foreach (var type in types)
            {
                for (uint i = 0; i < Constants.PLAYER_INVENTORY_PAGE_SIZE; i++)
                {
                    if (!slots.TryGetValue((type, i), out var slot)) continue;
                    if (slot.ItemId == 0)
                        return (type, i);
                }
            }

            return (InventoryType.Invalid, 0);
        }

        private string GetItemName(uint itemId)
        {
            try
            {
                var sheet = Plugin.Data.GetExcelSheet<Item>();
                return sheet?.GetRowOrDefault(itemId)?.Name.ToString() ?? $"Item#{itemId}";
            }
            catch
            {
                return $"Item#{itemId}";
            }
        }
    }
}
