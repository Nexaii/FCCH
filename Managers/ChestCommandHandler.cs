using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Common;

namespace FCCH.Managers
{
    public unsafe class ChestCommandHandler
    {
        private readonly Configuration _configuration;
        private readonly ChestManager _chestManager;
        private readonly MoveManager _moveManager;
        private readonly CrystalManager _crystalManager;
        private readonly ChestIndexer _indexer;

        public ChestCommandHandler(
            Configuration configuration,
            ChestManager chestManager,
            MoveManager moveManager,
            CrystalManager crystalManager,
            ChestIndexer indexer)
        {
            _configuration = configuration;
            _chestManager = chestManager;
            _moveManager = moveManager;
            _crystalManager = crystalManager;
            _indexer = indexer;
        }

        private static readonly InventoryType[] PlayerInvTypes = Constants.PlayerInventoryTypes;

        public void DepositAll()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            _crystalManager.Deposit(false);
            ReportSkippedTabs("da", CanDeposit);

            var moves = OperationManager.CalculateDepositMoves(_chestManager, _configuration, PlayerInvTypes);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for deposit.");
            else ChatHelper.Info("No items to deposit.");
        }

        public void WithdrawAll()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            _crystalManager.Withdraw(false);
            ReportSkippedTabs("wa", CanWithdraw);

            var withdrawable = new HashSet<InventoryType>(_chestManager.GetWithdrawableTabs());

            var requirements = new Dictionary<uint, int>();
            foreach (var slot in _chestManager.CachedItems)
            {
                if (!withdrawable.Contains(slot.Page)) continue;
                if (slot.Quantity > 0)
                {
                    if (_configuration.IgnoreList.Any(x => x.ItemId == slot.ItemId && x.IgnoreWithdraw)) continue;

                    if (!requirements.ContainsKey(slot.ItemId))
                        requirements[slot.ItemId] = 0;
                    requirements[slot.ItemId] += (int)slot.Quantity;
                }
            }

            var moves = OperationManager.CalculateWithdrawMoves(
                _chestManager,
                _configuration,
                requirements,
                PlayerInvTypes,
                ignoreLeaveOneRule: true,
                sourcePages: withdrawable);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for withdrawal.");
            else ChatHelper.Info("No items to withdraw.");
        }

        private void ReportSkippedTabs(string command, Func<byte, bool> allowed)
        {
            var skipped = _chestManager.GetAvailableTabs()
                .Where(IsItemTab)
                .Where(x => !allowed(_chestManager.GetChestAccess(x)))
                .ToList();

            if (skipped.Count > 0)
                ChatHelper.Info($"Skipping {command} for tabs: {FormatTabs(skipped)}");
        }

        private static bool CanDeposit(byte access)
            => access == Constants.FCPermissions.FULL_ACCESS || access == Constants.FCPermissions.DEPOSIT_ONLY;

        private static bool CanWithdraw(byte access)
            => access == Constants.FCPermissions.FULL_ACCESS;

        private static bool IsItemTab(InventoryType type)
            => type >= InventoryType.FreeCompanyPage1 && type <= InventoryType.FreeCompanyPage5;

        private static string FormatTabs(IEnumerable<InventoryType> tabs)
            => string.Join(", ", tabs.Select(x => ((int)x - (int)InventoryType.FreeCompanyPage1 + 1).ToString()));

        private bool TryGuardTab(InventoryType tab, bool needWithdraw, out string error)
        {
            int tabNum = ((int)tab - (int)InventoryType.FreeCompanyPage1) + 1;

            if (!_chestManager.GetAvailableTabs().Contains(tab))
            {
                error = $"Tab {tabNum} not unlocked yet.";
                return false;
            }

            var access = _chestManager.GetChestAccess(tab);
            bool allowed = needWithdraw
                ? access == Constants.FCPermissions.FULL_ACCESS
                : access == Constants.FCPermissions.FULL_ACCESS || access == Constants.FCPermissions.DEPOSIT_ONLY;

            if (!allowed)
            {
                error = needWithdraw ? $"Tab {tabNum}: no withdraw permission." : $"Tab {tabNum}: no deposit permission.";
                return false;
            }

            error = "";
            return true;
        }

        public void DepositToTab(int tab)
        {
            if (tab < 1 || tab > 5) { ChatHelper.Warning("Tab must be 1-5."); return; }

            var target = (InventoryType)((int)InventoryType.FreeCompanyPage1 + (tab - 1));
            if (!TryGuardTab(target, false, out var guardError))
            {
                ChatHelper.Warning(guardError);
                return;
            }

            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var moves = OperationManager.CalculateDepositMoves(_chestManager, _configuration, PlayerInvTypes, target);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (OperationManager.LastDepositOverflow.Count > 0)
                ChatHelper.Warning($"{OperationManager.LastDepositOverflow.Count} item(s) skipped - Tab {tab} full.");

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for deposit to Tab {tab}.");
            else ChatHelper.Info($"No items to deposit to Tab {tab}.");
        }

        public void DepositItemToTab(InventoryType srcType, uint srcSlot, InventoryType destTab)
        {
            int tab = ((int)destTab - (int)InventoryType.FreeCompanyPage1) + 1;

            if (!TryGuardTab(destTab, false, out var guardError))
            {
                ChatHelper.Warning(guardError);
                return;
            }

            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var moves = OperationManager.CalculateDepositMoves(_chestManager, _configuration, PlayerInvTypes, destTab, (srcType, srcSlot));

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (OperationManager.LastDepositOverflow.Count > 0)
                ChatHelper.Warning($"Item skipped - Tab {tab} full.");
            else if (moves.Count == 0)
                ChatHelper.Info($"Nothing to deposit to Tab {tab}.");
        }

        public void WithdrawItemStack(InventoryType srcPage, uint itemId, int amount)
        {
            if (amount <= 0) return;

            int tab = ((int)srcPage - (int)InventoryType.FreeCompanyPage1) + 1;
            if (_chestManager.GetChestAccess(srcPage) != Constants.FCPermissions.FULL_ACCESS)
            {
                ChatHelper.Warning($"Tab {tab}: no withdraw permission.");
                return;
            }

            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var requirements = new Dictionary<uint, int> { [itemId] = amount };
            var moves = OperationManager.CalculateWithdrawMoves(
                _chestManager, _configuration, requirements, PlayerInvTypes,
                ignoreLeaveOneRule: false,
                sourcePageFilter: srcPage);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count == 0)
                ChatHelper.Info("Nothing to withdraw.");
        }

        public void DepositDuplicates()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            _crystalManager.DepositDuplicates();

            var moves = OperationManager.CalculateDuplicateMoves(_chestManager, _configuration, PlayerInvTypes);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} duplicates for deposit.");
            else ChatHelper.Info("No duplicates to deposit.");
        }

        public void WithdrawFromTab(int tab)
        {
            if (tab < 1 || tab > 5) { ChatHelper.Warning("Tab must be 1-5."); return; }

            var target = (InventoryType)((int)InventoryType.FreeCompanyPage1 + (tab - 1));
            if (!TryGuardTab(target, true, out var guardError))
            {
                ChatHelper.Warning(guardError);
                return;
            }

            _moveManager.Clear();
            _chestManager.ScanFCChest();
            var requirements = new Dictionary<uint, int>();
            foreach (var slot in _chestManager.CachedItems)
            {
                if (slot.Page != target) continue;
                if (slot.Quantity == 0) continue;
                if (_configuration.IgnoreList.Any(x => x.ItemId == slot.ItemId && x.IgnoreWithdraw)) continue;

                if (!requirements.ContainsKey(slot.ItemId))
                    requirements[slot.ItemId] = 0;
                requirements[slot.ItemId] += (int)slot.Quantity;
            }

            var moves = OperationManager.CalculateWithdrawMoves(
                _chestManager, _configuration, requirements, PlayerInvTypes,
                ignoreLeaveOneRule: true,
                sourcePageFilter: target);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for withdrawal from Tab {tab}.");
            else ChatHelper.Info($"No items to withdraw from Tab {tab}.");
        }

        public void WithdrawMaterials(Dictionary<uint, int> items)
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var moves = OperationManager.CalculateWithdrawMoves(_chestManager, _configuration, items, PlayerInvTypes);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for workshop withdrawal.");
            else ChatHelper.Info("No materials found to withdraw.");
        }

        public void DepositMaterials(Dictionary<uint, int> items)
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            if (items.Count == 0)
            {
                ChatHelper.Info("Deposit request is empty.");
                return;
            }

            var moves = OperationManager.CalculateDepositMoves(_chestManager, _configuration, PlayerInvTypes, items);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} requested items for deposit.");
            else ChatHelper.Info("No requested items to deposit.");
        }

        public void DepositCustomItems()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var items = BuildCustomItemAmounts(x => x.CanDeposit, true);
            if (items.Count == 0)
            {
                ChatHelper.Info("Custom deposit list is empty.");
                return;
            }

            var moves = OperationManager.CalculateDepositMoves(_chestManager, _configuration, PlayerInvTypes, items);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} custom items for deposit.");
            else ChatHelper.Info("No custom items to deposit.");
        }

        public void WithdrawCustomItems()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var items = BuildCustomItemAmounts(x => x.CanWithdraw, false);
            if (items.Count == 0)
            {
                ChatHelper.Info("Custom withdrawal list is empty.");
                return;
            }

            var moves = OperationManager.CalculateWithdrawMoves(_chestManager, _configuration, items, PlayerInvTypes);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} custom items for withdrawal.");
            else ChatHelper.Info("No custom items to withdraw.");
        }

        private Dictionary<uint, int> BuildCustomItemAmounts(Func<WithdrawItem, bool> include, bool deposit)
        {
            var result = new Dictionary<uint, int>();
            foreach (var item in _configuration.WithdrawItems.Where(include))
            {
                var amount = item.AlwaysMax
                    ? GetCustomMaxAmount(item.ItemId, deposit)
                    : item.Quantity;

                if (amount <= 0) continue;

                if (!result.ContainsKey(item.ItemId)) result[item.ItemId] = 0;
                result[item.ItemId] = (int)Math.Min(int.MaxValue, (long)result[item.ItemId] + amount);
            }
            return result;
        }

        private int GetCustomMaxAmount(uint itemId, bool deposit)
        {
            long amount = deposit ? _chestManager.GetItemCountInPlayerInventory(itemId) : GetChestAvailableCount(itemId);
            return amount > int.MaxValue ? int.MaxValue : (int)Math.Max(0, amount);
        }

        private long GetChestAvailableCount(uint itemId)
        {
            long count = 0;
            foreach (var slot in _chestManager.CachedItems.Where(x => x.ItemId == itemId))
            {
                var available = (long)slot.Quantity;
                if (_configuration.LeaveOneItemPerStack && available > 0) available--;
                count += available;
            }
            return count;
        }

        public void StartIndexing(bool autoDump)
        {
            _indexer.Start(autoDump);
        }

        public void Stop()
        {
            _moveManager.Clear();
            _indexer.Stop();
            ChatHelper.Info("Stopped.");
        }

        public void SwitchToTab(InventoryType type)
        {
            _indexer.SwitchToTab(type);
        }
    }
}
