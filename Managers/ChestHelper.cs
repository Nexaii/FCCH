using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.GameData;
using FCCH.Managers;
using FCCH.Common;
using FCCH.Models;
using FCCH.UI;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace FCCH.Managers
{
    public unsafe class ChestHelper : IDisposable
    {
        private readonly Configuration _configuration;
        public MoveManager MoveManager { get; init; }
        public ChestManager ChestManager { get; init; }
        private readonly ChestIndexer _indexer;
        public CrystalManager CrystalMgr { get; init; }
        private readonly ChestCommandHandler _commandHandler;

        public Configuration Configuration => _configuration;
        public bool IsProcessing => MoveManager.IsProcessing || !_indexer.IsIdle;
        public List<Models.ShoppingItem> ShoppingList => _configuration.ShoppingItems;
        public bool IsSettingsVisible { get; set; } = false;
        public ItemFilter ItemFilter { get; private set; }
        public string LastError { get; private set; } = "";

        public bool IsChestFullyScanned => ChestManager.IsFullyScanned;

        private bool _wasProcessing = false;
        private bool _wasMoving = false;

        private System.Action? _pendingCommand;
        private bool _isWaitingForIndex = false;
        private DateTime _indexingCompleteTime = DateTime.MinValue;
        private const int EXECUTION_DELAY_MS = 2000;

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

            Plugin.GameInteropProvider.InitializeFromAttributes(this);
            Plugin.Framework.Update += OnUpdate;
            Callback.Initialize();

            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, Constants.FC_CHEST_ADDON_NAME, OnChestOpened);
        }

        private void OnUpdate(IFramework framework)
        {
            MoveManager.Update();
            
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            if (addon != null && addon->IsVisible)
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
                     _indexingCompleteTime = DateTime.Now;
                 }
            }           

            if (_pendingCommand != null)
            {               
                if (!_isWaitingForIndex && _indexer.IsIdle)
                {
                    if (addon != null && addon->IsVisible)
                    {
                        if ((DateTime.Now - _indexingCompleteTime).TotalMilliseconds >= EXECUTION_DELAY_MS)
                        {
                            var cmd = _pendingCommand;
                            _pendingCommand = null;
                            cmd.Invoke();
                        }
                    }
                }
            }
            
            if (IsProcessing || (DateTime.Now - MoveManager.LastActionTime).TotalSeconds < 2.0)
            {
                var numeric = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.INPUT_NUMERIC_ADDON_NAME, 1);
                if (numeric != null && numeric->IsVisible)
                {
                    Callback.Fire(numeric, true, (int)numeric->AtkValues[Constants.NUMERIC_INPUT_CALLBACK_IDX].UInt);
                }
            }
            
            bool currentProcessing = IsProcessing;
            bool wasMoving = _wasMoving;
            bool currentMoving = MoveManager.IsProcessing;
            
            if (!currentMoving && (wasMoving || MoveManager.ProcessedThisFrame))
            {

                 ChestManager.ScanFCChest();
                 
                 if (OperationManager.LastDepositOverflow.Count > 0)
                 {
                     ChatHelper.Warning("Overflow (FC stacks full):");
                     foreach (var (itemId, remaining) in OperationManager.LastDepositOverflow)
                     {
                         ChatHelper.Warning($"  - {GetItemName(itemId)}: {remaining} remaining");
                     }
                 }
                 
                 if (OperationManager.LastWithdrawOverflow.Count > 0)
                 {
                     ChatHelper.Warning("Overflow (inventory full):");
                     foreach (var (itemId, remaining) in OperationManager.LastWithdrawOverflow)
                     {
                         ChatHelper.Warning($"  - {GetItemName(itemId)}: {remaining} remaining");
                     }
                 }
                 
                 ChatHelper.Info("Operation complete.");
                 
                if (!MoveManager.SuppressCompletionSound)
                    SoundHelper.PlayCompletionSound(_configuration);
            }
            
            _wasProcessing = currentProcessing;
            _wasMoving = currentMoving;
        }

        public void ProcessCommand(System.Action command)
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            if (addon != null && addon->IsVisible)
            {
                command.Invoke();
            }
            else
            {
                _pendingCommand = command;
                _isWaitingForIndex = true; 
                InteractWithChest();
            }
        }

        private void InteractWithChest()
        {
            try
            {
                var chest = Plugin.ObjectTable.FirstOrDefault(x => x.Name.ToString().Equals("Company Chest", StringComparison.OrdinalIgnoreCase));
                if (chest != null)
                {
                    Plugin.PluginLog.Info($"[FCCH] Interacting with Company Chest (Oid: {chest.BaseId:X}).");
                    
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
                }
            }
            catch (Exception ex)
            {
                 Plugin.PluginLog.Error(ex, "Failed to interact with chest.");
                 _pendingCommand = null;
                 _isWaitingForIndex = false;
            }
        }
        
        public void DepositAll() => _commandHandler.DepositAll();
        public void WithdrawAll() => _commandHandler.WithdrawAll();
        public void DepositDuplicates() => _commandHandler.DepositDuplicates();
        public void StartIndexing(bool autoDump) => _commandHandler.StartIndexing(autoDump);
        public void Stop() => _commandHandler.Stop();
        
        public long GetItemCountInPlayerInventory(uint itemId)
        {
            long count = 0;
            var types = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
            foreach(var t in types)
            {
               var container = ChestManager.GetContainer(t);
               if (container == null) continue;
               for(int i=0; i<container->Size; i++)
               {
                   var item = container->GetInventorySlot(i);
                   if (item != null && item->ItemId == itemId) count += item->Quantity;
               }
            }
            return count;
        }

        public long GetItemCountInChest(uint itemId)
        {
            return ChestManager.ChestState.Values
                .SelectMany(x => x)
                .Where(x => x.ItemId == itemId)
                .Sum(x => x.Quantity);
        }

        public long GetEffectiveItemCountInChest(uint itemId)
        {
            return ChestManager.ChestState.Values
                .SelectMany(x => x)
                .Where(x => x.ItemId == itemId)
                .Sum(x => (long)x.Quantity);
        }

        public string GetItemName(uint itemId)
        {
            try
            {
                var sheet = Plugin.Data.GetExcelSheet<Item>();
                if (sheet == null) return $"Item #{itemId}";
                var row = sheet.GetRowOrDefault(itemId);
                return row != null ? row.Value.Name.ToString() : $"Item #{itemId}";
            }
            catch { return $"Item #{itemId}"; }
        }
        
        public void WithdrawMaterials(Dictionary<uint, int> items) => _commandHandler.WithdrawMaterials(items);
        
        private bool CanDeposit(InventoryType page)
        {
            var access = (byte)ChestManager.GetChestAccess(page);
            return access == Constants.FCPermissions.DEPOSIT_ONLY || access == Constants.FCPermissions.FULL_ACCESS;
        }

        public byte GetFCRank() => ChestManager.GetFCRank();
        public List<InventoryType> GetAvailableTabs() => ChestManager.GetAvailableTabs();
        public byte GetChestAccess(InventoryType page) => ChestManager.GetChestAccess(page);
        
        public void DebugLog(string msg)
        {
            if (!_configuration.DebugMode) return;
            Plugin.PluginLog.Info(msg);
            
            ChatHelper.Debug(msg);
            
            try
            {
                var path = _configuration.DebugLogPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning($"Failed to write debug log: {ex.Message}");
            }
        }
        
        public void VerboseLog(string msg)
        {
            if (_configuration.VerboseMode)
            {
                ChatHelper.Info($"[Verbose] {msg}");
            }
        }

        public void Dispose()
        {
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, Constants.FC_CHEST_ADDON_NAME, OnChestOpened);
            Plugin.Framework.Update -= OnUpdate;
            MoveManager.Dispose();
            ChestManager.Dispose();
        }
        
        private void OnChestOpened(AddonEvent type, AddonArgs args)
        {
            if (_indexer.IsIdle)
            {
                Plugin.PluginLog.Info("[FCCH] Chest opened. Starting full scan...");

                if (_configuration.DebugMode)
                {
                    DebugLog("Dumping Debug Info:");
                    DebugLog(DebugEnums.GetDebugInfo());
                }

                StartIndexing(autoDump: false);
            }
        }
        
        public void SwitchToTab(InventoryType type) => _commandHandler.SwitchToTab(type);
    }

}
