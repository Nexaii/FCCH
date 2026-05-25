using System;
using System.Collections.Generic;
using System.Linq;
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
        private int _tabCount = 0;

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
            var tabs = _chestManager.GetAvailableTabs();
            var restricted = tabs
                .Where(x => _chestManager.GetChestAccess(x) == Constants.FCPermissions.NO_ACCESS)
                .ToList();

            foreach (var tab in restricted)
                _chestManager.ClearPage(tab);

            if (restricted.Count > 0)
                ChatHelper.Info($"Skipping indexing restricted sections: {FormatSections(restricted)}");

            _queue = new Queue<InventoryType>(tabs.Except(restricted));

            if (_queue.Count > 0)
            {
                _tabCount = _queue.Count;
                _targetPage = _queue.Dequeue();
                _phase = IndexingPhase.Switching;
            }
        }

        public void Stop() => _phase = IndexingPhase.Idle;

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
                    if (_configuration.DebugMode) DumpContainerDiag(_targetPage);
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
                        ChatHelper.Info($"Indexed {_tabCount} tabs.");

                        if (_configuration.DebugMode)
                        {
                            FCCH.Common.FCCHLog.Info("Scanned Content:");
                            FCCH.Common.FCCHLog.Info(_chestManager.GetDebugContent());
                        }

                        OnIndexingComplete?.Invoke();
                        if (_autoDumpAfterIndexing) OnAutoDumpRequested?.Invoke();
                    }
                }
                else if ((DateTime.Now - _lastActionTime).TotalSeconds > _configuration.IndexingTimeoutSeconds)
                {
                    ChatHelper.Error("Indexing synchronization timed out. Please try again.");
                    _phase = IndexingPhase.Idle;
                }
            }
        }

        private static string FormatSections(IEnumerable<InventoryType> sections)
            => string.Join(", ", sections.Select(FormatSection));

        private static string FormatSection(InventoryType section)
            => section switch
            {
                >= InventoryType.FreeCompanyPage1 and <= InventoryType.FreeCompanyPage5 => $"tab {((int)section - (int)InventoryType.FreeCompanyPage1 + 1)}",
                InventoryType.FreeCompanyCrystals => "crystals",
                InventoryType.FreeCompanyGil => "gil",
                _ => section.ToString(),
            };

        private static void DumpContainerDiag(InventoryType type)
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(type);
            if (container == null)
            {
                FCCH.Common.FCCHLog.Info($"[Diag] {type} container=NULL");
                return;
            }
            int nonEmpty = 0;
            uint firstId = 0;
            for (int i = 0; i < container->Size; i++)
            {
                var s = container->GetInventorySlot(i);
                if (s != null && s->ItemId != 0)
                {
                    if (firstId == 0) firstId = s->ItemId;
                    nonEmpty++;
                }
            }
            FCCH.Common.FCCHLog.Info($"[Diag] {type} ptr=0x{(nint)container:X} size={container->Size} loaded={container->IsLoaded} nonEmpty={nonEmpty} firstId={firstId}");
        }
    }
}
