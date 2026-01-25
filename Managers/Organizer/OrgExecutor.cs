using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;
using FCCH.Models;

namespace FCCH.Managers.Organizer
{
    public unsafe class OrgExecutor
    {
        private enum ExecutorState
        {
            Idle,
            Withdrawing,
            WaitingForSort,
            Depositing,
            WaitingForScan,
            Verifying,
            Completed,
            Failed
        }

        private readonly ChestManager _chestManager;
        private readonly MoveManager _moveManager;
        private readonly Configuration _config;

        private ExecutorState _state = ExecutorState.Idle;
        private OrgCheckResult? _currentJob;
        private int _withdrawIndex;
        private int _depositIndex;
        private DateTime _lastMoveTime;
        private DateTime _scanWaitStart;
        private const int MOVE_DELAY_MS = 150;
        private const int SCAN_WAIT_MS = 1500;

        private InventoryType _lastSrcInv;
        private uint _lastSrcSlot;
        private uint _lastSrcItemId;
        private uint _lastSrcOriginalQty;
        private uint _lastMovedQty;

        public OrgJobStatus Status => _state switch
        {
            ExecutorState.Idle => OrgJobStatus.Idle,
            ExecutorState.Completed => OrgJobStatus.Completed,
            ExecutorState.Failed => OrgJobStatus.Failed,
            _ => OrgJobStatus.Running
        };

        public string StatusMessage { get; private set; } = "";
        public int TotalMoves => (_currentJob?.WithdrawMoves?.Count ?? 0) + (_currentJob?.DepositMoves?.Count ?? 0);
        public int CompletedMoves => _withdrawIndex + _depositIndex;

        public event Action? OnJobCompleted;

        public OrgExecutor(ChestManager chestManager, MoveManager moveManager, Configuration config)
        {
            _chestManager = chestManager;
            _moveManager = moveManager;
            _config = config;
        }

        private void DebugLog(string msg)
        {
            if (!_config.DebugMode) return;
            Plugin.PluginLog.Info($"[OrgExecutor] {msg}");
            ChatHelper.Debug($"[OrgExec] {msg}");
        }

        public bool StartJob(OrgCheckResult checkResult)
        {
            DebugLog($"StartJob called. CurrentState={_state}");
            if (_state != ExecutorState.Idle && _state != ExecutorState.Completed && _state != ExecutorState.Failed)
            {
                StatusMessage = "A job is already running.";
                DebugLog($"StartJob rejected: {StatusMessage}");
                return false;
            }

            if (!checkResult.IsValid)
            {
                StatusMessage = "Check invalid: " + checkResult.StatusMessage;
                return false;
            }

            _currentJob = checkResult;
            _withdrawIndex = 0;
            _depositIndex = 0;
            _state = ExecutorState.Withdrawing;
            _lastMoveTime = DateTime.MinValue;
            _moveManager.SuppressCompletionSound = true;

            _lastSrcInv = InventoryType.Invalid;
            _lastSrcSlot = 0;
            _lastSrcItemId = 0;
            _lastSrcOriginalQty = 0;
            _lastMovedQty = 0;

            StatusMessage = "Starting job...";
            DebugLog($"Job started. Withdraws={_currentJob.WithdrawMoves?.Count ?? 0}, Deposits={_currentJob.DepositMoves?.Count ?? 0}");

            return true;
        }

        public void Cancel()
        {
            if (_state == ExecutorState.Idle) return;

            _state = ExecutorState.Idle;
            _moveManager.Clear();
            _moveManager.SuppressCompletionSound = false;
            StatusMessage = "Job cancelled.";
        }

        public void Update()
        {
            if (_state == ExecutorState.Idle || _state == ExecutorState.Completed || _state == ExecutorState.Failed)
                return;

            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
            if (addon == null || !addon->IsVisible)
            {
                _state = ExecutorState.Failed;
                StatusMessage = "FC Chest closed unexpectedly.";
                DebugLog("FAILED: FC Chest closed unexpectedly.");
                return;
            }

            if ((DateTime.Now - _lastMoveTime).TotalMilliseconds < MOVE_DELAY_MS)
            {
                return;
            }

            if (_moveManager.IsProcessing)
            {
                return;
            }

            DebugLog($"Update tick: State={_state}, WithdrawIdx={_withdrawIndex}, DepositIdx={_depositIndex}");

            switch (_state)
            {
                case ExecutorState.Withdrawing:
                    ProcessWithdraw();
                    break;
                case ExecutorState.WaitingForSort:
                    ProcessSort();
                    break;
                case ExecutorState.Depositing:
                    ProcessDeposit();
                    break;
                case ExecutorState.WaitingForScan:
                    ProcessScanWait();
                    break;
                case ExecutorState.Verifying:
                    ProcessVerify();
                    break;
            }
        }

        private void ProcessWithdraw()
        {
            if (_currentJob?.WithdrawMoves == null || _withdrawIndex >= _currentJob.WithdrawMoves.Count)
            {
                StatusMessage = "Withdraw complete. Waiting for player to sort inventory...";
                DebugLog($"Withdraw phase complete. {_withdrawIndex} moves processed.");
                _state = ExecutorState.WaitingForSort;
                return;
            }

            var move = _currentJob.WithdrawMoves[_withdrawIndex];
            StatusMessage = $"Withdrawing {_withdrawIndex + 1}/{_currentJob.WithdrawMoves.Count}";
            DebugLog($"Withdraw[{_withdrawIndex}]: Item#{move.ItemId} x{move.Amount} from {move.SrcInv}:{move.SrcSlot}");

            _moveManager.Enqueue(move);
            _withdrawIndex++;
            _lastMoveTime = DateTime.Now;
        }

        private void ProcessSort()
        {
            DebugLog("Transitioning to Deposit phase.");
            _state = ExecutorState.Depositing;
            StatusMessage = "Beginning deposit phase...";
        }

        private void ProcessDeposit()
        {
            if (_currentJob?.DepositMoves == null || _depositIndex >= _currentJob.DepositMoves.Count)
            {
                _state = ExecutorState.WaitingForScan;
                _scanWaitStart = DateTime.Now;
                StatusMessage = "Waiting for sync...";
                DebugLog($"Deposit complete. Transitioning to WaitingForScan.");
                return;
            }

            var move = _currentJob.DepositMoves[_depositIndex];
            StatusMessage = $"Depositing {_depositIndex + 1}/{_currentJob.DepositMoves.Count}";

            var types = new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };

            var actualMove = FindActualPlayerSlot(move.ItemId, move.Amount, types);
            if (actualMove.HasValue)
            {
                DebugLog($"Deposit[{_depositIndex}]: Item#{move.ItemId} x{move.Amount} from {actualMove.Value.Type}:{actualMove.Value.Slot} to {move.DstInv}:{move.DstSlot}");
                
                _lastSrcInv = actualMove.Value.Type;
                _lastSrcSlot = actualMove.Value.Slot;
                _lastSrcItemId = move.ItemId;
                _lastMovedQty = move.Amount;
                var container = _chestManager.GetContainer(_lastSrcInv);
                if (container != null)
                {
                    var item = container->GetInventorySlot((int)_lastSrcSlot);
                    if (item != null) _lastSrcOriginalQty = (uint)item->Quantity;
                }

                var correctedMove = new MoveOperation
                {
                    SrcInv = actualMove.Value.Type,
                    SrcSlot = actualMove.Value.Slot,
                    DstInv = move.DstInv,
                    DstSlot = move.DstSlot,
                    ItemId = move.ItemId,
                    Amount = move.Amount,
                    IsNativeMove = true
                };
                _moveManager.Enqueue(correctedMove);
            }
            else
            {
                DebugLog($"Deposit[{_depositIndex}] FAILED: Could not find Item#{move.ItemId} x{move.Amount} in player inventory.");
                StatusMessage = $"Could not find Item#{move.ItemId} in player inventory.";
            }

            _depositIndex++;
            _lastMoveTime = DateTime.Now;
        }

        private (InventoryType Type, uint Slot)? FindActualPlayerSlot(uint itemId, uint requiredAmount, InventoryType[] types)
        {
            bool IsGhost(InventoryType type, uint slot, uint currentQty)
            {
                if (type == _lastSrcInv && slot == _lastSrcSlot && itemId == _lastSrcItemId)
                {
                    if (_lastMovedQty >= _lastSrcOriginalQty && currentQty == _lastSrcOriginalQty)
                    {
                        DebugLog($"Ignoring Ghost Slot {type}:{slot} (Qty {currentQty}). Waiting for memory update.");
                        return true;
                    }
                }
                return false;
            }

            foreach (var type in types)
            {
                var container = _chestManager.GetContainer(type);
                if (container == null) continue;

                for (uint i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot((int)i);
                    if (item != null && item->ItemId == itemId && item->Quantity == requiredAmount)
                    {
                        if (!IsGhost(type, i, (uint)item->Quantity)) return (type, i);
                    }
                }
            }

            foreach (var type in types)
            {
                var container = _chestManager.GetContainer(type);
                if (container == null) continue;

                for (uint i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot((int)i);
                    if (item != null && item->ItemId == itemId && item->Quantity > requiredAmount)
                    {
                        if (!IsGhost(type, i, (uint)item->Quantity)) return (type, i);
                    }
                }
            }

            foreach (var type in types)
            {
                var container = _chestManager.GetContainer(type);
                if (container == null) continue;

                for (uint i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot((int)i);
                    if (item != null && item->ItemId == itemId && item->Quantity > 0)
                    {
                       if (!IsGhost(type, i, (uint)item->Quantity)) return (type, i);
                    }
                }
            }
            return null;
        }

        private void ProcessScanWait()
        {
            if ((DateTime.Now - _scanWaitStart).TotalMilliseconds >= SCAN_WAIT_MS)
            {
                _state = ExecutorState.Verifying;
                StatusMessage = "Verifying...";
                DebugLog("Scan wait complete. Transitioning to Verifying.");
            }
        }

        private void ProcessVerify()
        {
            if (_currentJob?.ExpectedCounts == null || _currentJob.ExpectedCounts.Count == 0)
            {
                _state = ExecutorState.Completed;
                StatusMessage = "Job completed successfully.";
                DebugLog("No expected counts to verify. Job COMPLETED.");
                _moveManager.SuppressCompletionSound = false;
                OnJobCompleted?.Invoke();
                return;
            }

            var depositMoves = _currentJob.DepositMoves;
            var destTab = (depositMoves != null && depositMoves.Count > 0) ? depositMoves[0].DstInv : InventoryType.Invalid;
            var destItems = _chestManager.CachedItems
                .Where(x => x.Page == destTab)
                .ToList();

            var actualCounts = new Dictionary<uint, uint>();
            foreach (var slot in destItems)
            {
                if (actualCounts.ContainsKey(slot.ItemId))
                    actualCounts[slot.ItemId] += slot.Quantity;
                else
                    actualCounts[slot.ItemId] = slot.Quantity;
            }

            var mismatches = new List<string>();
            foreach (var expected in _currentJob.ExpectedCounts)
            {
                uint actual = actualCounts.TryGetValue(expected.Key, out var val) ? val : 0;
                if (actual < expected.Value - 1)
                {
                    mismatches.Add($"Item#{expected.Key}: expected {expected.Value}, found {actual}");
                }
            }

            if (mismatches.Count > 0)
            {
                _state = ExecutorState.Failed;
                StatusMessage = $"Verification failed: {mismatches.First()}";
                DebugLog($"Verification FAILED. Mismatches: {string.Join(", ", mismatches)}");
                _moveManager.SuppressCompletionSound = false;
            }
            else
            {
                _state = ExecutorState.Completed;
                StatusMessage = "Job completed successfully.";
                DebugLog($"Verification passed. Job COMPLETED.");
                _moveManager.SuppressCompletionSound = false;
                OnJobCompleted?.Invoke();
            }
        }

        public void Reset()
        {
            _state = ExecutorState.Idle;
            _currentJob = null;
            _withdrawIndex = 0;
            _depositIndex = 0;
            StatusMessage = "";
        }
    }
}
