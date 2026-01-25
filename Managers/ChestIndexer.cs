using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;

namespace FCCH.Managers
{
    public unsafe class ChestIndexer
    {
        public enum IndexingPhase { Idle, Switching, Scanning }

        private readonly Configuration _configuration;
        private readonly ChestManager _chestManager;

        private IndexingPhase _phase = IndexingPhase.Idle;
        private DateTime _lastActionTime = DateTime.MinValue;
        private InventoryType _targetPage = InventoryType.Invalid;
        private Queue<InventoryType> _queue = new();
        private bool _autoDumpAfterIndexing = false;

        public IndexingPhase Phase => _phase;
        public bool IsIdle => _phase == IndexingPhase.Idle;
        public event Action? OnIndexingComplete;
        public event Action? OnAutoDumpRequested;

        public ChestIndexer(Configuration configuration, ChestManager chestManager)
        {
            _configuration = configuration;
            _chestManager = chestManager;
        }

        public void Start(bool autoDump)
        {
            _autoDumpAfterIndexing = autoDump;
            _queue = new Queue<InventoryType>(_chestManager.GetAvailableTabs());

            if (_queue.Count > 0)
            {
                _targetPage = _queue.Dequeue();
                _phase = IndexingPhase.Switching;
                ChatHelper.Info($"Starting re-index ({_queue.Count + 1} pages)...");
            }
        }

        public void Stop()
        {
            _phase = IndexingPhase.Idle;
        }

        public void SwitchToTab(InventoryType type)
        {
            _targetPage = type;
            _phase = IndexingPhase.Switching;
        }

        public void Tick(AtkUnitBase* addon)
        {
            if (_phase == IndexingPhase.Idle) return;
            if (addon == null) return;
            if ((DateTime.Now - _lastActionTime).TotalMilliseconds < _configuration.IndexingDelayInMs) return;

            bool isPassive = _targetPage == InventoryType.FreeCompanyGil || _targetPage == InventoryType.FreeCompanyCrystals;

            if (_phase == IndexingPhase.Switching)
            {
                if (!isPassive || !_chestManager.IsInventoryLoaded(_targetPage))
                {
                    _chestManager.SwitchToPage(addon, _targetPage);
                }
                _phase = IndexingPhase.Scanning;
                _lastActionTime = DateTime.Now;
            }
            else if (_phase == IndexingPhase.Scanning)
            {
                var currentPage = _chestManager.GetCurrentFCPage(addon);
                if (currentPage == _targetPage || (isPassive && _chestManager.IsInventoryLoaded(_targetPage)))
                {
                    _chestManager.UpdateChestState(_targetPage);

                    if (_queue.Count > 0)
                    {
                        _targetPage = _queue.Dequeue();
                        _phase = IndexingPhase.Switching;
                        _lastActionTime = DateTime.Now;
                    }
                    else
                    {
                        _phase = IndexingPhase.Idle;
                        ChatHelper.Info("Re-indexing complete.");

                        if (_configuration.DebugMode)
                        {
                            Plugin.PluginLog.Info("Scanned Content:");
                            Plugin.PluginLog.Info(_chestManager.GetDebugContent());
                        }

                        OnIndexingComplete?.Invoke();

                        if (_autoDumpAfterIndexing)
                        {
                            OnAutoDumpRequested?.Invoke();
                        }
                    }
                }
                else if ((DateTime.Now - _lastActionTime).TotalSeconds > _configuration.IndexingTimeoutSeconds)
                {
                    ChatHelper.Error("Indexing synchronization timed out. Please try again.");
                    _phase = IndexingPhase.Idle;
                }
            }
        }
    }
}
