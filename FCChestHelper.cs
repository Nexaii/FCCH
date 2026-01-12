using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FC_Chest_Helper.GameData;
using FC_Chest_Helper.Managers; 
using FC_Chest_Helper.Logic;    
using FC_Chest_Helper.Common;
using FC_Chest_Helper.Models;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace FC_Chest_Helper
{
    public unsafe class FCChestHelper : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly MoveManager _moveManager;
        private readonly ChestManager _chestManager;

        
        public Configuration Configuration => _configuration;
        public bool IsProcessing => _moveManager.IsProcessing || _indexingPhase != IndexingPhase.Idle;
        public List<Models.ShoppingItem> ShoppingList => _configuration.ShoppingItems;
        public bool IsSettingsVisible { get; set; } = false;
        public ItemFilter ItemFilter { get; private set; }
        public string LastError { get; private set; } = "";
        
        public bool IsChestFullyScanned => _chestManager.IsFullyScanned;

        private enum IndexingPhase { Idle, Switching, Scanning }
        private IndexingPhase _indexingPhase = IndexingPhase.Idle;
        private DateTime _lastActionTime = DateTime.MinValue;
        private InventoryType _targetPage = InventoryType.Invalid;
        private Queue<InventoryType> _indexingQueue = new();
        private bool _autoDumpAfterIndexing = false;
        
        private bool _wasProcessing = false;
        private bool _wasMoving = false;

        public FCChestHelper(Configuration configuration)
        {
            _configuration = configuration;
            _chestManager = new ChestManager(_configuration);
            _moveManager = new MoveManager(_configuration, _chestManager);
            
            ItemFilter = new ItemFilter(Plugin.Data);

            
            Plugin.GameInteropProvider.InitializeFromAttributes(this);
            Plugin.Framework.Update += OnUpdate;
            Callback.Initialize();
            
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, Constants.FC_CHEST_ADDON_NAME, OnChestOpened);
        }

        private void OnUpdate(IFramework framework)
        {
            _moveManager.Update();
            
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            if (addon != null && addon->IsVisible)
            {
                 var currentPage = _chestManager.GetCurrentFCPage(addon);
                 if (currentPage != InventoryType.Invalid)
                 {
                     _chestManager.UpdateChestState(currentPage);
                 }
                 
                 if (_indexingPhase != IndexingPhase.Idle)
                 {
                     HandleIndexing(addon);
                 }
            }
            
            if (IsProcessing || (DateTime.Now - _moveManager.LastActionTime).TotalSeconds < 2.0)
            {
                var numeric = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.INPUT_NUMERIC_ADDON_NAME, 1);
                if (numeric != null && numeric->IsVisible)
                {
                    Callback.Fire(numeric, true, (int)numeric->AtkValues[Constants.NUMERIC_INPUT_CALLBACK_IDX].UInt);
                }
            }
            
            bool currentProcessing = IsProcessing;
            bool wasMoving = _wasMoving;
            bool currentMoving = _moveManager.IsProcessing;

            if (!currentMoving && (wasMoving || _moveManager.ProcessedThisFrame))
            {

                 _chestManager.ScanFCChest();
                 
                 if (OperationLogic.LastDepositOverflow.Count > 0)
                 {
                     ChatHelper.Warning("Overflow (FC stacks full):");
                     foreach (var (itemId, remaining) in OperationLogic.LastDepositOverflow)
                     {
                         ChatHelper.Warning($"  - {GetItemName(itemId)}: {remaining} remaining");
                     }
                 }
                 
                 if (OperationLogic.LastWithdrawOverflow.Count > 0)
                 {
                     ChatHelper.Warning("Overflow (inventory full):");
                     foreach (var (itemId, remaining) in OperationLogic.LastWithdrawOverflow)
                     {
                         ChatHelper.Warning($"  - {GetItemName(itemId)}: {remaining} remaining");
                     }
                 }
                 
                 ChatHelper.Info("Operation complete.");
                 
                 if (_configuration.PlayCompletionSound)
                 {
                     try
                     {
                         string soundPath = _configuration.CustomSoundPath;
                         if (string.IsNullOrWhiteSpace(soundPath))
                         {
                             soundPath = System.IO.Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Assets", "Completion.mp3");
                         }
                         if (System.IO.File.Exists(soundPath))
                         {
                             var audioFile = new NAudio.Wave.AudioFileReader(soundPath);
                             var outputDevice = new NAudio.Wave.WaveOutEvent();
                             outputDevice.Init(audioFile);
                             outputDevice.Play();
                         }
                     }
                     catch (Exception ex)
                     {
                         Plugin.PluginLog.Warning($"Failed to play completion sound: {ex.Message}");
                     }
                 }
            }
            
            _wasProcessing = currentProcessing;
            _wasMoving = currentMoving;
        }
        
        public void DepositAll()
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();
            
            var playerInvTypes = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
            var moves = OperationLogic.CalculateDepositMoves(_chestManager, _configuration, playerInvTypes);
            
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
            
            var playerInvTypes = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
            var moves = OperationLogic.CalculateWithdrawMoves(_chestManager, _configuration, requirements, playerInvTypes, true);

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

            var playerInvTypes = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
            var moves = OperationLogic.CalculateDuplicateMoves(_chestManager, _configuration, playerInvTypes);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} duplicates for deposit.");
            else ChatHelper.Info("No duplicates to deposit.");
        }

        public void StartIndexing(bool autoDump)
        {
            _autoDumpAfterIndexing = autoDump;
            _indexingQueue = new Queue<InventoryType>(_chestManager.GetAvailableTabs());
            
            if (_indexingQueue.Count > 0)
            {
                _targetPage = _indexingQueue.Dequeue();
                _indexingPhase = IndexingPhase.Switching;
                ChatHelper.Info($"Starting re-index ({_indexingQueue.Count + 1} pages)...");
            }
        }
        
        public void Stop()
        {
            _moveManager.Clear();
            _indexingPhase = IndexingPhase.Idle;
            ChatHelper.Info("Stopped.");
        }
        
        public long GetItemCountInPlayerInventory(uint itemId)
        {
            long count = 0;
            var types = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
            foreach(var t in types)
            {
               var container = _chestManager.GetContainer(t);
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
            return _chestManager.ChestState.Values
                .SelectMany(x => x)
                .Where(x => x.ItemId == itemId)
                .Sum(x => x.Quantity);
        }

        public long GetEffectiveItemCountInChest(uint itemId)
        {
            return _chestManager.ChestState.Values
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
        
        public void WithdrawMaterials(Dictionary<uint, int> items)
        {
            _moveManager.Clear();
            _chestManager.ScanFCChest();

            var playerInvTypes = new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
            var moves = OperationLogic.CalculateWithdrawMoves(_chestManager, _configuration, items, playerInvTypes);

            foreach (var move in moves)
            {
                _moveManager.Enqueue(move);
            }

            if (moves.Count > 0) ChatHelper.Info($"Queued {moves.Count} items for workshop withdrawal.");
            else ChatHelper.Info("No materials found to withdraw.");
        }
        
        private bool CanDeposit(InventoryType page)
        {
            var access = (byte)_chestManager.GetChestAccess(page);
            return access == Constants.FCPermissions.DEPOSIT_ONLY || access == Constants.FCPermissions.FULL_ACCESS;
        }

        public byte GetFCRank() => _chestManager.GetFCRank();
        public List<InventoryType> GetAvailableTabs() => _chestManager.GetAvailableTabs();
        public byte GetChestAccess(InventoryType page) => _chestManager.GetChestAccess(page);
        
        public void DebugLog(string msg)
        {
            if (!_configuration.DebugMode) return;
            Plugin.PluginLog.Info(msg);
            
            try 
            {
                var path = _configuration.DebugLogPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
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
            _moveManager.Dispose();
            _chestManager.Dispose();
        }
        
        private void OnChestOpened(AddonEvent type, AddonArgs args)
        {
            if (_indexingPhase == IndexingPhase.Idle)
            {
                Plugin.PluginLog.Info("[FCCH] Chest opened. Starting full scan...");
                StartIndexing(autoDump: false);
            }
        }
        
        private void HandleIndexing(AtkUnitBase* addon)
        {
            if ((DateTime.Now - _lastActionTime).TotalMilliseconds < _configuration.IndexingDelayInMs) return;
            
            if (_indexingPhase == IndexingPhase.Switching)
            {
                _chestManager.SwitchToPage(addon, _targetPage);
                 _indexingPhase = IndexingPhase.Scanning;
                 _lastActionTime = DateTime.Now;
            }
            else if (_indexingPhase == IndexingPhase.Scanning)
            {
                var currentPage = _chestManager.GetCurrentFCPage(addon);
                if (currentPage == _targetPage)
                {
                    _chestManager.UpdateChestState(currentPage);
                    
                    if (_indexingQueue.Count > 0)
                    {
                        _targetPage = _indexingQueue.Dequeue();
                        _indexingPhase = IndexingPhase.Switching;
                        _lastActionTime = DateTime.Now;
                    }
                    else
                    {
                        _indexingPhase = IndexingPhase.Idle;
                        ChatHelper.Info("Re-indexing complete.");
                        if (_autoDumpAfterIndexing) DepositAll();
                    }
                }
                else if ((DateTime.Now - _lastActionTime).TotalSeconds > 3.0)
                {
                    ChatHelper.Error("Indexing synchronization timed out. Please try again.");
                    _indexingPhase = IndexingPhase.Idle;
                }
            }
        }
        
        public void SwitchToTab(InventoryType type)
        {
            _targetPage = type;
            _indexingPhase = IndexingPhase.Switching;
        }
    }
}
