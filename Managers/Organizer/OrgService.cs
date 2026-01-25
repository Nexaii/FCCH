using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Managers.Organizer
{
    public class OrgService : IDisposable
    {
        private readonly OrgValidator _validator;
        private readonly OrgExecutor _executor;
        private readonly ChestManager _chestManager;
        private readonly Configuration _config;
        private readonly Action _onReindexRequested;

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
            _config = config;
            _onReindexRequested = onReindexRequested;
            _validator = new OrgValidator(chestManager, config);
            _executor = new OrgExecutor(chestManager, moveManager, config);
            _executor.OnJobCompleted += HandleJobCompleted;
        }

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
            Plugin.PluginLog.Info($"[Organizer] {msg}");
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
            _executor.OnJobCompleted -= HandleJobCompleted;
        }
    }
}
