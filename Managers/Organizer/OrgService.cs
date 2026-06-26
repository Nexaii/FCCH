using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Common;

namespace FCCH.Managers.Organizer
{
    public class OrgService : IDisposable
    {
        private readonly OrgValidator _validator;
        private readonly OrgExecutor _executor;
        private readonly ChestManager _chestManager;
        private readonly MoveManager _moveManager;
        private readonly Configuration _config;
        private readonly Action _onReindexRequested;

        private bool _sortActive;
        private bool _sortDraining;
        private int _sortPass;
        private InventoryType _sortTab;
        private OrgSortOrder _sortOrder;
        private bool _sortDescending;
        private HashSet<OrgFilterCategory> _sortFilters = new();

        private bool _mergeActive;
        private bool _mergeDraining;
        private bool _mergeRechecked;
        private InventoryType _mergeTab;

        private const int SortMaxPasses = 2;

        public bool IsSortRunning => _sortActive;
        public bool IsMergeRunning => _mergeActive;

        public OrgJobRequest CurrentRequest { get; private set; } = new();
        public OrgCheckResult? LastCheck { get; private set; }
        public OrgJobStatus JobStatus => _executor.Status;
        public string StatusMessage => _executor.StatusMessage;
        public int TotalMoves => _executor.TotalMoves;
        public int CompletedMoves => _executor.CompletedMoves;

        public event Action OnJobCompleted
        {
            add => _executor.OnJobCompleted += value;
            remove => _executor.OnJobCompleted -= value;
        }

        public OrgService(ChestManager chestManager, MoveManager moveManager, Configuration config, Action onReindexRequested)
        {
            _chestManager = chestManager;
            _moveManager = moveManager;
            _config = config;
            _onReindexRequested = onReindexRequested;
            _validator = new OrgValidator(chestManager, config);
            _executor = new OrgExecutor(chestManager, moveManager, config);
            _executor.OnJobCompleted += HandleJobCompleted;
        }

        public static bool IsItemPage(InventoryType tab)
            => tab >= InventoryType.FreeCompanyPage1 && tab <= InventoryType.FreeCompanyPage5;

        public bool CanSortTab(InventoryType tab)
            => IsItemPage(tab) && _chestManager.GetChestAccess(tab) == Constants.FCPermissions.FULL_ACCESS;

        public OrgCheckResult CheckSort(InventoryType tab, OrgSortOrder order, bool descending, HashSet<OrgFilterCategory> filters)
        {
            var result = new OrgCheckResult();

            if (!CanSortTab(tab))
            {
                result.StatusMessage = IsItemPage(tab) ? "No modify access on that tab." : "Sort is only for item tabs.";
                return result;
            }

            _chestManager.ScanFCChest();

            var slots = _chestManager.CachedItems.Where(x => x.Page == tab).ToList();
            var predicate = OrgFilters.GetMultiPredicate(filters);
            var filtered = slots.Where(predicate).ToList();

            result.StackCount = filtered.Count;
            result.PreviewItems = filtered
                .GroupBy(x => x.ItemId)
                .Select(g => new OrgPreviewItem
                {
                    ItemId = g.Key,
                    ItemName = GetItemName(g.Key),
                    CategoryName = OrgFilters.GetCategoryName(g.Key),
                    Quantity = (uint)g.Sum(x => x.Quantity),
                    WillMerge = g.Count() > 1,
                    IsSelected = true
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            result.IsValid = filtered.Count > 0;
            result.StatusMessage = result.IsValid ? "Ready" : "No items match the current filter.";
            LastCheck = result;
            return result;
        }

        public bool RunSort(InventoryType tab, OrgSortOrder order, bool descending, HashSet<OrgFilterCategory> filters)
        {
            if (!CanSortTab(tab))
            {
                ChatHelper.Warning(IsItemPage(tab) ? "No modify access on that tab." : "Sort is only for item tabs.");
                return false;
            }

            _chestManager.ScanFCChest();

            var moves = SortPlanner.Plan(tab, TabSlots(tab), order, descending, filters);
            if (moves.Count == 0)
            {
                ChatHelper.Info("Tab is already sorted.");
                return false;
            }

            if (moves.Count > SortPlanner.MoveCap)
            {
                ChatHelper.Warning($"Sort needs {moves.Count} moves, over the {SortPlanner.MoveCap} cap. Aborting.");
                return false;
            }

            _moveManager.Clear();
            _moveManager.SuppressCompletionSound = true;

            _sortActive = true;
            _sortDraining = false;
            _sortPass = 1;
            _sortTab = tab;
            _sortOrder = order;
            _sortDescending = descending;
            _sortFilters = new HashSet<OrgFilterCategory>(filters);

            foreach (var move in moves)
                _moveManager.Enqueue(move);

            ChatHelper.Info($"Queued {moves.Count} sort moves.");
            return true;
        }

        public void UpdateSortWatch()
        {
            if (!_sortActive) return;

            if (_moveManager.IsProcessing)
            {
                _sortDraining = true;
                return;
            }

            if (!_sortDraining) return;

            _sortDraining = false;

            var moves = SortPlanner.Plan(_sortTab, TabSlots(_sortTab), _sortOrder, _sortDescending, _sortFilters);
            if (moves.Count == 0)
            {
                FinishSort(success: true);
                return;
            }

            if (_sortPass >= SortMaxPasses)
            {
                ChatHelper.Warning("Could not fully sort the tab (likely chest contention or permissions).");
                FinishSort(success: false);
                return;
            }

            _sortPass++;
            _sortDraining = true;
            foreach (var move in moves)
                _moveManager.Enqueue(move);
        }

        public void CancelSort()
        {
            if (!_sortActive) return;
            _moveManager.Clear();
            FinishSort(success: false);
        }

        private void FinishSort(bool success)
        {
            _sortActive = false;
            _sortDraining = false;
            _moveManager.SuppressCompletionSound = false;

            if (success)
            {
                Common.SoundHelper.PlayCompletionSound(_config);
                _onReindexRequested?.Invoke();
            }
        }

        public bool RunMerge(InventoryType tab)
        {
            if (!CanSortTab(tab))
            {
                ChatHelper.Warning(IsItemPage(tab) ? "No modify access on that tab." : "Merge is only for item tabs.");
                return false;
            }

            _chestManager.ScanFCChest();

            var moves = SortPlanner.PlanMergeOnly(tab, TabSlots(tab));
            if (moves.Count == 0)
            {
                ChatHelper.Info("Stacks are already merged.");
                return false;
            }

            if (moves.Count > SortPlanner.MoveCap)
            {
                ChatHelper.Warning($"Merge needs {moves.Count} moves, over the {SortPlanner.MoveCap} cap. Aborting.");
                return false;
            }

            _moveManager.Clear();
            _moveManager.SuppressCompletionSound = true;

            _mergeActive = true;
            _mergeDraining = false;
            _mergeRechecked = false;
            _mergeTab = tab;

            foreach (var move in moves)
                _moveManager.Enqueue(move);

            ChatHelper.Info($"Queued {moves.Count} merge moves.");
            return true;
        }

        public void UpdateMergeWatch()
        {
            if (!_mergeActive) return;

            if (_moveManager.IsProcessing)
            {
                _mergeDraining = true;
                return;
            }

            if (!_mergeDraining) return;

            _mergeDraining = false;

            var moves = SortPlanner.PlanMergeOnly(_mergeTab, TabSlots(_mergeTab));
            if (moves.Count == 0)
            {
                FinishMerge(success: true);
                return;
            }

            if (_mergeRechecked)
            {
                ChatHelper.Warning("Could not fully merge the tab (likely chest contention or permissions).");
                FinishMerge(success: false);
                return;
            }

            _mergeRechecked = true;
            _mergeDraining = true;
            foreach (var move in moves)
                _moveManager.Enqueue(move);
        }

        public void CancelMerge()
        {
            if (!_mergeActive) return;
            _moveManager.Clear();
            FinishMerge(success: false);
        }

        private void FinishMerge(bool success)
        {
            _mergeActive = false;
            _mergeDraining = false;
            _moveManager.SuppressCompletionSound = false;

            if (success)
            {
                Common.SoundHelper.PlayCompletionSound(_config);
                _onReindexRequested?.Invoke();
            }
        }

        private List<ChestManager.ScannedSlot> TabSlots(InventoryType tab)
            => _chestManager.CachedItems.Where(x => x.Page == tab).ToList();

        private static string GetItemName(uint itemId) => Common.ItemNames.Get(itemId);

        private void HandleJobCompleted()
        {
            DebugLog("Job completed. Triggering chest reindex.");
            LastCheck = null;
            _onReindexRequested?.Invoke();
            Common.SoundHelper.PlayCompletionSound(_config);
        }

        private void DebugLog(string msg)
        {
            if (!_config.DebugMode) return;
            FCCH.Common.FCCHLog.Info($"[Organizer] {msg}");
            Common.ChatHelper.Debug($"[Org] {msg}");
        }

        public OrgCheckResult Check()
        {
            DebugLog($"Check: {CurrentRequest.Mode} from {CurrentRequest.SourceTab} to {CurrentRequest.DestTab}, Filters={CurrentRequest.Filters.Count}");
            _chestManager.ScanFCChest();
            LastCheck = _validator.Check(CurrentRequest);
            DebugLog($"CheckResult: Valid={LastCheck.IsValid}, Items={LastCheck.PreviewItems.Count}, WithdrawMoves={LastCheck.WithdrawMoves?.Count ?? 0}, DepositMoves={LastCheck.DepositMoves?.Count ?? 0}");
            if (!LastCheck.IsValid)
                DebugLog($"CheckFailed: {LastCheck.StatusMessage}");
            return LastCheck;
        }

        public bool Run()
        {
            DebugLog("Run() called");
            if (LastCheck == null || !LastCheck.IsValid)
            {
                DebugLog("No valid check, re-checking...");
                LastCheck = Check();
            }

            if (!LastCheck.IsValid)
            {
                DebugLog($"Run aborted: {LastCheck.StatusMessage}");
                return false;
            }

            DebugLog($"Starting executor with {LastCheck.WithdrawMoves?.Count ?? 0} withdraws, {LastCheck.DepositMoves?.Count ?? 0} deposits");
            return _executor.StartJob(LastCheck);
        }

        public void Cancel()
        {
            DebugLog("Job cancelled by user");
            _executor.Cancel();
        }

        public void AbortForClosedChest()
        {
            DebugLog("Job aborted because chest closed");
            _executor.Cancel();
            if (_sortActive) FinishSort(success: false);
            if (_mergeActive) FinishMerge(success: false);
            LastCheck = null;
        }

        public void Update()
        {
            _executor.Update();
        }

        public void Reset()
        {
            _executor.Reset();
            LastCheck = null;
        }

        public static string GetTabDisplayName(InventoryType type)
        {
            return type switch
            {
                InventoryType.Inventory1 => "Player Inventory",
                InventoryType.FreeCompanyPage1 => "Tab 1",
                InventoryType.FreeCompanyPage2 => "Tab 2",
                InventoryType.FreeCompanyPage3 => "Tab 3",
                InventoryType.FreeCompanyPage4 => "Tab 4",
                InventoryType.FreeCompanyPage5 => "Tab 5",
                _ => "Unknown"
            };
        }

        public static InventoryType[] GetAvailableTabs()
        {
            return new[]
            {
                InventoryType.Inventory1,
                InventoryType.FreeCompanyPage1,
                InventoryType.FreeCompanyPage2,
                InventoryType.FreeCompanyPage3,
                InventoryType.FreeCompanyPage4,
                InventoryType.FreeCompanyPage5
            };
        }

        public void Dispose()
        {
            try { _executor.Cancel(); } catch { }
            if (_sortActive) FinishSort(success: false);
            if (_mergeActive) FinishMerge(success: false);
            _executor.OnJobCompleted -= HandleJobCompleted;
            LastCheck = null;
        }
    }
}
