using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.GameData;
using FCCH.Managers;
using FCCH.Managers.Gil;
using FCCH.Common;
using FCCH.Models;
using FCCH.UI;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace FCCH.Managers
{
    public readonly struct ActionGateResult
    {
        public ActionGateResult(bool canRun, string reason)
        {
            CanRun = canRun;
            Reason = reason;
        }

        public bool CanRun { get; }
        public string Reason { get; }
    }

    public unsafe class ChestHelper : IDisposable
    {
        private readonly Configuration _configuration;
        public MoveManager MoveManager { get; init; }
        public ChestManager ChestManager { get; init; }
        private readonly ChestIndexer _indexer;
        public CrystalManager CrystalMgr { get; init; }
        private readonly ChestCommandHandler _commandHandler;
        private Common.RefusalWatch? _refusalWatcher;

        public Configuration Configuration => _configuration;
        public bool IsProcessing => MoveManager.IsProcessing || !_indexer.IsIdle;
        public bool IsUserOperationActive => MoveManager.IsProcessing;
        public Func<bool>? ExternalOperationActive { get; set; }
        public GilManager? Gil { get; set; }
        public List<Models.ShoppingItem> ShoppingList => _configuration.ShoppingItems;
        public bool IsSettingsVisible { get; set; } = false;
        public ItemFilter ItemFilter { get; private set; }

        public event System.Action? CompanyChestClosedDuringOperation;

        private bool _wasMoving = false;
        private bool _wasChestOpen = false;
        private bool _gilSweepPending = false;
        private bool _gilSweptThisVisit = false;

        private System.Action? _pendingCommand;
        private bool _isWaitingForIndex = false;
        private long _indexingCompleteMs;
        private DateTime _pendingCommandQueuedAtUtc = DateTime.MinValue;
        private const int ExecutionDelayMs = 2000;
        private const int GilSweepQuietMs = 2000;
        private static readonly TimeSpan PendingCommandTimeout = TimeSpan.FromSeconds(15);

        public ChestHelper(Configuration configuration)
        {
            _configuration = configuration;
            ChestManager = new ChestManager(_configuration);
            MoveManager = new MoveManager(_configuration, ChestManager);
            CrystalMgr = new CrystalManager(_configuration, ChestManager, MoveManager);
            _indexer = new ChestIndexer(_configuration, ChestManager);
            _commandHandler = new ChestCommandHandler(_configuration, ChestManager, MoveManager, CrystalMgr, _indexer);
            _indexer.OnAutoDumpRequested += _commandHandler.DepositAll;

            ItemFilter = new ItemFilter(Plugin.Data);

            Plugin.Framework.Update += OnUpdate;
            Callback.Initialize();

            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, Constants.FreeCompanyChestAddonName, OnChestOpened);

            try
            {
                _refusalWatcher = new Common.RefusalWatch();
                MoveManager.RefusalWatcher = _refusalWatcher;
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[FCCH] Failed to start RefusalWatch.");
            }
        }

        private void OnUpdate(IFramework framework)
        {
#if DEBUG
            var perfStart = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            try
            {
            var addon = Common.ChestAddon.GetOpen();
            var isChestOpen = addon != null;

            if (_wasChestOpen && !isChestOpen)
            {
                _gilSweepPending = false;
                _gilSweptThisVisit = false;

                if (HasAbortableWork())
                {
                    AbortForClosedChest();
                    _wasChestOpen = false;
                    return;
                }
            }

            _wasChestOpen = isChestOpen;

            MoveManager.Update();
            
            if (isChestOpen)
            {
                 var currentPage = ChestManager.GetCurrentFCPage(addon);
                 if (currentPage != InventoryType.Invalid)
                 {
                     ChestManager.UpdateChestState(currentPage);
                 }

                 _indexer.Tick(addon);

                 if (_indexer.IsIdle && _isWaitingForIndex)
                 {
                     _isWaitingForIndex = false;
                     _indexingCompleteMs = Environment.TickCount64;
                 }
            }           

            if (_pendingCommand != null)
            {
                if ((DateTime.UtcNow - _pendingCommandQueuedAtUtc) > PendingCommandTimeout)
                {
                    CancelPendingCommand("FCCH: chest did not open in time, command cancelled.");
                }
                else if (!_isWaitingForIndex && _indexer.IsIdle)
                {
                    if (addon != null && addon->IsVisible)
                    {
                        if (Environment.TickCount64 - _indexingCompleteMs >= ExecutionDelayMs)
                        {
                            var cmd = _pendingCommand;
                            _pendingCommand = null;
                            _pendingCommandQueuedAtUtc = DateTime.MinValue;
                            cmd.Invoke();
                        }
                    }
                }
            }
            
            if (IsProcessing || Environment.TickCount64 - MoveManager.LastActionMs < 2000)
            {
                var fcChest = Common.ChestAddon.GetOpen();
                if (fcChest != null)
                {
                    var numeric = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.InputNumericAddonName, 1);
                    if (numeric != null && numeric->IsVisible)
                    {
                        Callback.Fire(numeric, true, (int)numeric->AtkValues[Constants.NumericInputCallbackIndex].UInt);
                    }
                }
            }
            
            bool wasMoving = _wasMoving;
            bool currentMoving = MoveManager.IsProcessing;
            
            if (!currentMoving && (wasMoving || MoveManager.ProcessedThisFrame))
            {

                 ChestManager.ScanFCChest();
                 
                 MoveReport.Completed(MoveManager.TakeLastBatch());

                if (!MoveManager.SuppressCompletionSound)
                    SoundHelper.PlayCompletionSound(_configuration);
            }

            _wasMoving = currentMoving;

            if (_gilSweepPending && !IsProcessing && Environment.TickCount64 - MoveManager.LastActionMs >= GilSweepQuietMs)
            {
                _gilSweepPending = false;
                _gilSweptThisVisit = true;
                Gil?.AutoDeposit();
            }

            Gil?.TickPendingTransaction();
            }
            finally
            {
#if DEBUG
                Common.PerfCounter.RecordOnUpdate(System.Diagnostics.Stopwatch.GetTimestamp() - perfStart);
                Common.PerfCounter.TickAndMaybeFlush();
#endif
            }
        }

        private bool HasAbortableWork()
        {
            return MoveManager.IsProcessing ||
                   !_indexer.IsIdle ||
                   _pendingCommand != null ||
                   ExternalOperationActive?.Invoke() == true;
        }

        private void AbortForClosedChest()
        {
            MoveManager.Clear();
            MoveManager.SuppressCompletionSound = false;
            MoveManager.SuppressBatchSummary = false;
            _indexer.Stop();
            _pendingCommand = null;
            _isWaitingForIndex = false;
            _pendingCommandQueuedAtUtc = DateTime.MinValue;
            _indexingCompleteMs = 0;
            _wasMoving = false;
            CompanyChestClosedDuringOperation?.Invoke();
            ChatHelper.Warning("FCCH stopped because the company chest was closed.");
            DebugLog("[Safety] Aborted active FCCH work because the company chest closed.");
        }

        public void ProcessCommand(System.Action command)
        {
            var gate = CanAcceptCommand();
            if (!gate.CanRun)
            {
                ChatHelper.Warning(gate.Reason);
                return;
            }

            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            if (addon != null && addon->IsVisible)
            {
                command.Invoke();
            }
            else
            {
                _pendingCommand = command;
                _isWaitingForIndex = true;
                _pendingCommandQueuedAtUtc = DateTime.UtcNow;
                InteractWithChest();
            }
        }

        public void RunGilCommand(System.Action command)
        {
            ProcessCommand(() =>
            {
                _gilSweepPending = false;
                _gilSweptThisVisit = true;
                Gil?.CancelPendingTransaction();
                command();
            });
        }

        public ActionGateResult CanStartUserAction()
        {
            if (IsUnavailable()) return Blocked("FCCH cannot operate: not logged in or no Free Company.");
            if (_pendingCommand != null) return Blocked("FCCH is waiting for the company chest to finish opening.");
            if (ExternalOperationActive?.Invoke() == true) return Blocked("FCCH is running an organizer job.");
            if (MoveManager.IsProcessing) return Blocked("FCCH is moving items.");
            if (_isWaitingForIndex || !_indexer.IsIdle) return Blocked("FCCH is scanning the company chest.");
            return new ActionGateResult(true, "");
        }

        public bool IsUnavailable()
        {
            try
            {
                if (Plugin.ClientState == null || !Plugin.ClientState.IsLoggedIn) return true;
                if (ChestManager.GetFCRank() == 0) return true;
                return false;
            }
            catch (Exception ex)
            {
                FCCHLog.Debug($"[ChestHelper] Availability check threw, treating as unavailable: {ex.Message}");
                return true;
            }
        }

        public bool IsChestAddonVisible()
        {
            try
            {
                return Common.ChestAddon.GetOpen() != null;
            }
            catch (Exception ex)
            {
                FCCHLog.Debug($"[ChestHelper] Chest addon visibility check threw: {ex.Message}");
                return false;
            }
        }

        public ActionGateResult CanAcceptCommand()
        {
            var gate = CanStartUserAction();
            if (!gate.CanRun) return gate;
            return new ActionGateResult(true, "");
        }

        public bool TryStartUserAction(System.Action action)
        {
            var gate = CanStartUserAction();
            if (!gate.CanRun)
            {
                ChatHelper.Warning(gate.Reason);
                return false;
            }

            action();
            return true;
        }

        private static ActionGateResult Blocked(string reason) => new(false, reason);

        private void CancelPendingCommand(string userMessage)
        {
            _pendingCommand = null;
            _isWaitingForIndex = false;
            _pendingCommandQueuedAtUtc = DateTime.MinValue;
            ChatHelper.Error(userMessage);
            DebugLog($"[PendingCommand] cancelled: {userMessage}");
        }

        private void InteractWithChest()
        {
            try
            {
                var chest = Plugin.ObjectTable.FirstOrDefault(x => x.Name.ToString().Equals("Company Chest", StringComparison.OrdinalIgnoreCase));
                if (chest != null)
                {
                    FCCHLog.Info($"[FCCH] Interacting with Company Chest (Oid: {chest.BaseId:X}).");
                    
                    var targetSystem = FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance();
                    if (targetSystem != null)
                    {
                         targetSystem->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)chest.Address, false);
                    }
                }
                else
                {
                    ChatHelper.Error("Could not find 'Company Chest' nearby.");
                    _pendingCommand = null;
                    _isWaitingForIndex = false;
                    _pendingCommandQueuedAtUtc = DateTime.MinValue;
                }
            }
            catch (Exception ex)
            {
                 FCCHLog.Error(ex, "Failed to interact with chest.");
                 _pendingCommand = null;
                 _isWaitingForIndex = false;
                 _pendingCommandQueuedAtUtc = DateTime.MinValue;
            }
        }
        
        public void DepositAll() => _commandHandler.DepositAll();
        public void WithdrawAll() => _commandHandler.WithdrawAll();
        public void DepositDuplicates() => _commandHandler.DepositDuplicates();
        public void DepositCustomItems() => _commandHandler.DepositCustomItems();
        public void WithdrawCustomItems() => _commandHandler.WithdrawCustomItems();
        public void WithdrawWorkshopItems()
        {
            var list = BuildWorkshopMaterialList();
            if (list.Count == 0)
            {
                ChatHelper.Info("Workshop list is empty.");
                return;
            }

            WithdrawMaterials(list);
        }
        public void DepositToTab(int tab) => _commandHandler.DepositToTab(tab);
        public void WithdrawFromTab(int tab) => _commandHandler.WithdrawFromTab(tab);
        public void DepositItemToTab(InventoryType srcType, uint srcSlot, InventoryType destTab) => _commandHandler.DepositItemToTab(srcType, srcSlot, destTab);
        public void WithdrawItemStack(InventoryType srcPage, uint srcSlot, uint itemId, int amount) => _commandHandler.WithdrawItemStack(srcPage, srcSlot, itemId, amount);

        public InventoryType GetOpenChestPage()
        {
            var addon = Common.ChestAddon.GetOpen();
            if (addon == null) return InventoryType.Invalid;
            return ChestManager.GetCurrentFCPage(addon);
        }

        public (uint ItemId, uint Quantity)? GetChestSlot(InventoryType page, int slot)
        {
            if (slot < 0) return null;
            var container = ChestManager.GetContainer(page);
            if (container == null || slot >= container->Size) return null;
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0) return null;
            return ((uint)item->ItemId, (uint)item->Quantity);
        }
        public void StartIndexing(bool autoDump) => _commandHandler.StartIndexing(autoDump);
        public void Stop()
        {
            _gilSweepPending = false;
            _commandHandler.Stop();
        }

        private Dictionary<uint, int> BuildWorkshopMaterialList()
        {
            var list = new Dictionary<uint, int>();
            foreach (var shopItem in ShoppingList)
            {
                var mats = shopItem.Craft.Phases
                    .SelectMany(p => p.Items)
                    .Select(x => new { Item = x, Required = x.TotalQuantity * shopItem.Quantity });

                foreach (var mat in mats)
                {
                    if (!list.ContainsKey(mat.Item.ItemId)) list[mat.Item.ItemId] = 0;
                    list[mat.Item.ItemId] += mat.Required;
                }
            }

            return list;
        }
        
        public long GetItemCountInPlayerInventory(uint itemId) => ChestManager.GetItemCountInPlayerInventory(itemId);

        public long GetItemCountInChest(uint itemId)
        {
            return ChestManager.ChestState.Values
                .SelectMany(x => x)
                .Where(x => x.ItemId == itemId)
                .Sum(x => x.Quantity);
        }

        public long GetWithdrawableItemCountInChest(uint itemId)
        {
            var withdrawable = new HashSet<InventoryType>(ChestManager.GetWithdrawableTabs());
            return ChestManager.CachedItems
                .Where(x => x.ItemId == itemId)
                .Where(x => withdrawable.Contains(x.Page))
                .Where(x => !_configuration.IgnoreList.Any(i => i.ItemId == itemId && i.IgnoreWithdraw))
                .Sum(x => _configuration.LeaveOneItemPerStack && x.Quantity > 0 ? (long)x.Quantity - 1 : x.Quantity);
        }

        public string GetItemName(uint itemId) => Common.ItemNames.Get(itemId);

        public void WithdrawMaterials(Dictionary<uint, int> items) => _commandHandler.WithdrawMaterials(items);
        public void DepositMaterials(Dictionary<uint, int> items) => _commandHandler.DepositMaterials(items);
        public void WithdrawMissingMaterials(Dictionary<uint, int> requiredTotals)
        {
            var missing = new Dictionary<uint, int>();
            foreach (var (itemId, required) in requiredTotals)
            {
                var amount = required - GetItemCountInPlayerInventory(itemId);
                if (amount <= 0) continue;
                missing[itemId] = amount > int.MaxValue ? int.MaxValue : (int)amount;
            }

            _commandHandler.WithdrawMaterials(missing);
        }
        
        private bool CanDeposit(InventoryType page)
        {
            var access = (byte)ChestManager.GetChestAccess(page);
            return access == Constants.FCPermissions.DepositOnly || access == Constants.FCPermissions.FullAccess;
        }

        public byte GetFCRank() => ChestManager.GetFCRank();
        public List<InventoryType> GetAvailableTabs() => ChestManager.GetAvailableTabs();
        public byte GetChestAccess(InventoryType page) => ChestManager.GetChestAccess(page);
        public void DumpRawPermissions(byte? overrideRank = null) => ChestManager.DumpRawPermissions(overrideRank);
        public string DumpAccessProbe() => ChestManager.DumpAccessProbe();
        
        public void DebugLog(string msg)
        {
            if (!_configuration.DebugMode) return;
            FCCHLog.Info(msg);
            ChatHelper.Debug(msg);
        }

        public void Dispose()
        {
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, Constants.FreeCompanyChestAddonName, OnChestOpened);
            Plugin.Framework.Update -= OnUpdate;
            _indexer.OnAutoDumpRequested -= _commandHandler.DepositAll;
            MoveManager.Dispose();
            ChestManager.Dispose();
            _refusalWatcher?.Dispose();
            Gil = null;
        }
        
        private void OnChestOpened(AddonEvent type, AddonArgs args)
        {
            if (_configuration.GilDepositOnChestOpen && !_gilSweptThisVisit)
                _gilSweepPending = true;

            if (_indexer.IsIdle)
            {
                FCCHLog.Info("[FCCH] Chest opened. Starting full scan...");

                ChestManager.ResetIndexingSession();

                var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FreeCompanyChestAddonName, 1);
                if (addon != null)
                {
                    var current = ChestManager.GetCurrentFCPage(addon);
                    if (current != InventoryType.Invalid)
                        ChestManager.MarkInventoryObserved(current);
                }

                StartIndexing(autoDump: false);
            }
        }
        
        public void SwitchToTab(InventoryType type) => _commandHandler.SwitchToTab(type);

        public void RefreshTabs(IEnumerable<InventoryType> tabs) => _indexer.StartTabs(tabs);
    }

}
