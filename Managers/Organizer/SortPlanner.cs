using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Common;
using FCCH.GameData;
using FCCH.Models;

namespace FCCH.Managers.Organizer
{
    public static class SortPlanner
    {
        private sealed class SlotState
        {
            public uint ItemId;
            public uint Quantity;
            public bool IsHq;
            public uint MaxStack;
            public bool Empty => ItemId == 0 || Quantity == 0;
        }

        public const int MoveCap = 400;

        public static List<MoveOperation> PlanMergeOnly(
            InventoryType tab,
            IReadOnlyList<ChestManager.ScannedSlot> tabSlots)
        {
            var moves = new List<MoveOperation>();
            PlanMerge(tab, BuildBoard(tabSlots), moves);
            return moves;
        }

        public static List<MoveOperation> Plan(
            InventoryType tab,
            IReadOnlyList<ChestManager.ScannedSlot> tabSlots,
            OrgSortOrder order,
            bool descending,
            HashSet<OrgFilterCategory> filters)
        {
            var moves = new List<MoveOperation>();
            var slots = BuildBoard(tabSlots);

            PlanMerge(tab, slots, moves);

            var predicate = OrgFilters.GetMultiPredicate(filters);
            var pool = new List<int>();
            var filteredSlots = new List<int>();
            for (var i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.Empty)
                {
                    pool.Add(i);
                    continue;
                }

                if (predicate(ToScanned(tab, i, s)))
                {
                    pool.Add(i);
                    filteredSlots.Add(i);
                }
            }

            if (filteredSlots.Count == 0)
                return moves;

            var ordered = OrgFilters
                .ApplySortOrder(filteredSlots.Select(i => ToScanned(tab, i, slots[i])), order, descending)
                .Select(x => (int)x.Slot)
                .ToList();

            var targets = pool.OrderBy(x => x).Take(ordered.Count).ToList();
            PlanReorder(tab, slots, moves, targets, ordered);

            return moves;
        }

        private static SlotState[] BuildBoard(IReadOnlyList<ChestManager.ScannedSlot> tabSlots)
        {
            var slots = new SlotState[Constants.FreeCompanyChestPageSize];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = new SlotState();

            foreach (var s in tabSlots)
            {
                if (s.Slot >= Constants.FreeCompanyChestPageSize) continue;
                var dst = slots[s.Slot];
                dst.ItemId = s.ItemId;
                dst.Quantity = s.Quantity;
                dst.IsHq = s.IsHq;
                dst.MaxStack = s.MaxStack > 0 ? s.MaxStack : ItemStackCache.GetMaxStack(s.ItemId);
            }

            return slots;
        }

        private static ChestManager.ScannedSlot ToScanned(InventoryType tab, int slot, SlotState s) => new()
        {
            Page = tab,
            Slot = (uint)slot,
            ItemId = s.ItemId,
            Quantity = s.Quantity,
            IsHq = s.IsHq,
            MaxStack = s.MaxStack
        };

        private static void PlanMerge(InventoryType tab, SlotState[] slots, List<MoveOperation> moves)
        {
            for (var dst = 0; dst < slots.Length; dst++)
            {
                var d = slots[dst];
                if (d.Empty || d.MaxStack <= 1 || d.Quantity >= d.MaxStack) continue;

                for (var src = dst + 1; src < slots.Length; src++)
                {
                    if (d.Quantity >= d.MaxStack) break;
                    if (moves.Count >= MoveCap) return;

                    var s = slots[src];
                    if (s.Empty || s.ItemId != d.ItemId || s.IsHq != d.IsHq) continue;

                    var space = d.MaxStack - d.Quantity;
                    var transfer = s.Quantity < space ? s.Quantity : space;
                    if (transfer == 0) continue;

                    moves.Add(new MoveOperation
                    {
                        SrcInv = tab,
                        SrcSlot = (uint)src,
                        DstInv = tab,
                        DstSlot = (uint)dst,
                        ItemId = d.ItemId,
                        Amount = transfer,
                        IsNativeMove = true
                    });

                    d.Quantity += transfer;
                    s.Quantity -= transfer;
                    if (s.Quantity == 0) { s.ItemId = 0; s.IsHq = false; s.MaxStack = 1; }
                }
            }
        }

        private static void PlanReorder(
            InventoryType tab,
            SlotState[] slots,
            List<MoveOperation> moves,
            List<int> targets,
            List<int> ordered)
        {
            var at = new int[slots.Length];
            for (var i = 0; i < at.Length; i++)
                at[i] = slots[i].Empty ? -1 : i;

            var pos = new Dictionary<int, int>();
            for (var i = 0; i < at.Length; i++)
                if (at[i] >= 0) pos[at[i]] = i;

            for (var k = 0; k < targets.Count; k++)
            {
                if (moves.Count >= MoveCap) return;

                var i = targets[k];
                var want = ordered[k];
                if (at[i] == want) continue;

                var j = pos[want];

                if (at[i] < 0)
                {
                    var s = slots[j];
                    moves.Add(new MoveOperation
                    {
                        SrcInv = tab,
                        SrcSlot = (uint)j,
                        DstInv = tab,
                        DstSlot = (uint)i,
                        ItemId = s.ItemId,
                        Amount = s.Quantity,
                        IsNativeMove = true
                    });

                    slots[i] = s;
                    slots[j] = new SlotState();
                    at[i] = want;
                    at[j] = -1;
                    pos[want] = i;
                }
                else
                {
                    var displaced = at[i];
                    var srcStack = slots[j];
                    moves.Add(new MoveOperation
                    {
                        SrcInv = tab,
                        SrcSlot = (uint)j,
                        DstInv = tab,
                        DstSlot = (uint)i,
                        ItemId = srcStack.ItemId,
                        Amount = srcStack.Quantity,
                        IsNativeMove = true,
                        SortSwap = true
                    });

                    (slots[i], slots[j]) = (slots[j], slots[i]);
                    at[i] = want;
                    at[j] = displaced;
                    pos[want] = i;
                    pos[displaced] = j;
                }
            }
        }
    }
}
