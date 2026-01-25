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

        private static readonly InventoryType[] PlayerInvTypes =
        {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4
        };

        public void DepositAll()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            _crystalManager.Deposit(false);

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

            var requirements = new Dictionary<uint, int>();
            foreach (var slot in _chestManager.CachedItems)
            {
                if (slot.Quantity > 0)
                {
                    if (_configuration.IgnoreList.Any(x => x.ItemId == slot.ItemId && x.IgnoreWithdraw)) continue;

                    if (!requirements.ContainsKey(slot.ItemId))
                        requirements[slot.ItemId] = 0;
                    requirements[slot.ItemId] += (int)slot.Quantity;
                }
            }

            var moves = OperationManager.CalculateWithdrawMoves(_chestManager, _configuration, requirements, PlayerInvTypes, true);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for withdrawal.");
            else ChatHelper.Info("No items to withdraw.");
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
