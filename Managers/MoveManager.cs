using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FCCH.Models;
using FCCH.Common;

namespace FCCH.Managers
{
    public unsafe class MoveManager : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly ChestManager _chestManager;
        public Queue<MoveOperation> MoveQueue { get; private set; } = new();
        private readonly HashSet<(InventoryType, uint, InventoryType, uint, uint)> _queuedOps = new();

        public long LastActionMs { get; private set; }
        public bool IsProcessing => MoveQueue.Count > 0;
        public bool ProcessedThisFrame { get; private set; } = false;
        public bool SuppressCompletionSound { get; set; } = false;
        public int TotalQueued { get; private set; } = 0;
        public int CompletedCount { get; private set; } = 0;
        public int RefusedCount { get; private set; } = 0;
        public int SkippedByBlockCount { get; private set; } = 0;

        private const int RefusalThreshold = 5;
        private readonly Dictionary<InventoryType, int> _consecutiveRefusalsByTab = new();
        private readonly HashSet<InventoryType> _blockedTabs = new();

        public Common.RefusalWatch? RefusalWatcher { get; set; }
        private DateTime _lastDispatchUtc = DateTime.MinValue;
        
        private delegate int InventoryManagerMoveItemDelegate(InventoryManager* manager, InventoryType srcInv, ushort srcSlot, InventoryType dstInv, ushort dstSlot, int quantity);
        private InventoryManagerMoveItemDelegate? _invManagerMoveItem;
        private const string InvManagerMoveItemSig = "48 89 5C 24 10 48 89 6C 24 18 56 57 41 55 41 56 41 57 48 83 EC 30 8D BA 60 F0 FF FF 45 0F BF E8 8D 82 FC EF FF FF 41 8B E9 44 8B FA 4C 8B F1";

        public MoveManager(Configuration config, ChestManager chestManager)
        {
            _configuration = config;
            _chestManager = chestManager;
            ResolveInvManagerMoveItem();
        }

        private void ResolveInvManagerMoveItem()
        {
            try
            {
                if (Plugin.SigScanner.TryScanText(InvManagerMoveItemSig, out var ptr))
                {
                    _invManagerMoveItem = Marshal.GetDelegateForFunctionPointer<InventoryManagerMoveItemDelegate>(ptr);
                    FCCHLog.Info($"[MoveManager] InventoryManager_MoveItem resolved at 0x{ptr:X16}");
                }
                else
                {
                    FCCHLog.Warning("[MoveManager] InventoryManager_MoveItem signature mismatch.");
                }
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[MoveManager] Failed to resolve InventoryManager_MoveItem.");
            }
        }

        public void Enqueue(MoveOperation op)
        {
            var key = (op.SrcInv, op.SrcSlot, op.DstInv, op.DstSlot, op.ItemId);
            if (_queuedOps.Contains(key)) return;
            _queuedOps.Add(key);
            MoveQueue.Enqueue(op);
            TotalQueued++;
        }

        public void Update()
        {
            ProcessedThisFrame = false;
            if (MoveQueue.Count == 0) return;

            if (Environment.TickCount64 - LastActionMs < _configuration.MoveDelayInMs) return;

            if (Common.InventoryOpGate.HasPendingOperation()) return;

            ProcessNextMove();
        }

        private void ProcessNextMove()
        {
            if (MoveQueue.Count == 0) return;

            var op = MoveQueue.Dequeue();
            _queuedOps.Remove((op.SrcInv, op.SrcSlot, op.DstInv, op.DstSlot, op.ItemId));

            var guardTab = GuardTabFor(op);
            if (_blockedTabs.Contains(guardTab))
            {
                SkippedByBlockCount++;
                LastActionMs = Environment.TickCount64;
                EmitBatchSummaryIfDrained();
                return;
            }

            CompletedCount++;
            var invManager = InventoryManager.Instance();
            if (invManager == null) return;

            var srcContainer = invManager->GetInventoryContainer(op.SrcInv);
            var dstContainer = invManager->GetInventoryContainer(op.DstInv);

            if (srcContainer == null)
            {
                DebugLog($"[ExecuteMove] Source container {op.SrcInv} is null. Skipping.");
                return;
            }
            if (dstContainer == null && !(op.ItemId == 1 && op.IsNativeMove))
            {
                DebugLog($"[ExecuteMove] Destination container {op.DstInv} is null. Skipping.");
                return;
            }

            if (_configuration.VerboseMode)
                ChatHelper.Info($"[Move] {op.Amount}x Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot})");
            
            if (_configuration.DebugMode)
                DebugLog($"[Move] {op.Amount}x Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot}) Native={op.IsNativeMove}");
            
            if (_configuration.LowerQualityOnDeposit && IsFCPage(op.DstInv) && !IsFCPage(op.SrcInv))
            {
                var container = invManager->GetInventoryContainer(op.SrcInv);
                if (container != null)
                {
                    var item = container->GetInventorySlot((int)op.SrcSlot);
                    if (item != null && (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0)
                    {
                        AgentInventoryContext.Instance()->LowerItemQuality(item, op.SrcInv, (int)op.SrcSlot, 0);
                        DebugLog($"[LowerQuality] Item#{op.ItemId} lowered, re-queuing for next tick");
                        
                        var newQueue = new Queue<MoveOperation>();
                        newQueue.Enqueue(op);
                        while (MoveQueue.Count > 0)
                            newQueue.Enqueue(MoveQueue.Dequeue());
                        MoveQueue = newQueue;
                        
                        LastActionMs = Environment.TickCount64;
                        return;
                    }
                }
            }

            try
            {
                if (op.ItemId != 1)
                {
                    var srcSlotPtr = srcContainer->GetInventorySlot((int)op.SrcSlot);
                    if (srcSlotPtr == null || srcSlotPtr->ItemId == 0)
                    {
                        DebugLog($"[Skip] Source slot {op.SrcInv}:{op.SrcSlot} is empty or unavailable.");
                        EmitBatchSummaryIfDrained();
                        return;
                    }
                }

                _lastDispatchUtc = DateTime.UtcNow;

                if (!DispatchMove(invManager, op))
                {
                    EmitBatchSummaryIfDrained();
                    return;
                }

                if (RefusalWatcher != null && RefusalWatcher.ConsumeRefusalSince(_lastDispatchUtc))
                {
                    DebugLog($"[Move/Refused] LogMessage#{RefusalWatcher.LastRefusalLogId} on {guardTab}");
                    NoteRefusal(guardTab);
                }
                else
                {
                    NoteSuccess(guardTab);
                }
                LastActionMs = Environment.TickCount64;
                ProcessedThisFrame = true;
                EmitBatchSummaryIfDrained();
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, $"[Move] Transaction aborted for Item#{op.ItemId}");
                DebugLog($"[Error] Move failed: {ex.Message}");
                EmitBatchSummaryIfDrained();
            }
        }

        private bool DispatchMove(InventoryManager* invManager, MoveOperation op)
        {
            if (op.SortSwap)
            {
                int swapRc = invManager->MoveItemSlot(op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, true);
                DebugLog($"[Move/SortSwap] Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot}) rc={swapRc}");
                return true;
            }

            if (_invManagerMoveItem != null)
            {
                int rc = _invManagerMoveItem(invManager, op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, (int)op.Amount);
                DebugLog($"[Move/InvMgr] {op.Amount}x Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot}) rc={rc}");
                return true;
            }

            if (op.IsNativeMove)
            {
                FCCHLog.Error($"[Move] Native delegate missing - refusing quantity move for Item#{op.ItemId}.");
                return false;
            }

            invManager->MoveItemSlot(op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, true);
            DebugLog($"[Move/Slot-Fallback] Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot})");
            return true;
        }

        private bool IsFCPage(InventoryType type)
        {
            return type == InventoryType.FreeCompanyPage1 ||
                   type == InventoryType.FreeCompanyPage2 ||
                   type == InventoryType.FreeCompanyPage3 ||
                   type == InventoryType.FreeCompanyPage4 ||
                   type == InventoryType.FreeCompanyPage5 ||
                   type == InventoryType.FreeCompanyGil ||
                   type == InventoryType.FreeCompanyCrystals;
        }
        
        private void DebugLog(string msg)
        {
            if (!_configuration.DebugMode) return;
            FCCHLog.Info(msg);
        }
        
        public void Clear()
        {
            MoveQueue.Clear();
            _queuedOps.Clear();
            TotalQueued = 0;
            CompletedCount = 0;
            RefusedCount = 0;
            SkippedByBlockCount = 0;
            _consecutiveRefusalsByTab.Clear();
            _blockedTabs.Clear();
        }

        private static InventoryType GuardTabFor(in MoveOperation op)
        {
            return IsFCPageStatic(op.DstInv) ? op.DstInv : op.SrcInv;
        }

        private static bool IsFCPageStatic(InventoryType type)
        {
            return type == InventoryType.FreeCompanyPage1 ||
                   type == InventoryType.FreeCompanyPage2 ||
                   type == InventoryType.FreeCompanyPage3 ||
                   type == InventoryType.FreeCompanyPage4 ||
                   type == InventoryType.FreeCompanyPage5 ||
                   type == InventoryType.FreeCompanyGil ||
                   type == InventoryType.FreeCompanyCrystals;
        }

        private void BlockTabAndPrune(InventoryType tab)
        {
            if (!_blockedTabs.Add(tab)) return;
            ChatHelper.Warning($"Stopped using {TabLabel(tab)} after {RefusalThreshold} consecutive refusals (likely permission, full, or stack limit).");

            int dropped = 0;
            var kept = new Queue<MoveOperation>();
            while (MoveQueue.Count > 0)
            {
                var q = MoveQueue.Dequeue();
                if (GuardTabFor(q) == tab) { dropped++; _queuedOps.Remove((q.SrcInv, q.SrcSlot, q.DstInv, q.DstSlot, q.ItemId)); continue; }
                kept.Enqueue(q);
            }
            MoveQueue = kept;
            SkippedByBlockCount += dropped;
        }

        private static string TabLabel(InventoryType t) => t switch
        {
            InventoryType.FreeCompanyPage1 => "Tab 1",
            InventoryType.FreeCompanyPage2 => "Tab 2",
            InventoryType.FreeCompanyPage3 => "Tab 3",
            InventoryType.FreeCompanyPage4 => "Tab 4",
            InventoryType.FreeCompanyPage5 => "Tab 5",
            InventoryType.FreeCompanyCrystals => "Crystals",
            InventoryType.FreeCompanyGil => "Gil",
            _ => t.ToString(),
        };

        private void NoteSuccess(InventoryType tab)
        {
            if (_consecutiveRefusalsByTab.ContainsKey(tab)) _consecutiveRefusalsByTab[tab] = 0;
        }

        private void NoteRefusal(InventoryType tab)
        {
            RefusedCount++;
            int n = _consecutiveRefusalsByTab.TryGetValue(tab, out var v) ? v + 1 : 1;
            _consecutiveRefusalsByTab[tab] = n;
            if (n >= RefusalThreshold) BlockTabAndPrune(tab);
        }

        private void EmitBatchSummaryIfDrained()
        {
            if (MoveQueue.Count != 0) return;
            if (TotalQueued == 0) return;
            int succeeded = CompletedCount - RefusedCount;
            if (succeeded < 0) succeeded = 0;
            string msg = $"Done. {succeeded}/{TotalQueued} moves completed.";
            if (RefusedCount > 0) msg += $" {RefusedCount} refused.";
            if (SkippedByBlockCount > 0) msg += $" {SkippedByBlockCount} skipped (blocked tabs).";
            ChatHelper.Info(msg);
            TotalQueued = 0;
            CompletedCount = 0;
            RefusedCount = 0;
            SkippedByBlockCount = 0;
        }

        public void Dispose()
        {
            MoveQueue.Clear();
            _queuedOps.Clear();
            TotalQueued = 0;
            CompletedCount = 0;
            _invManagerMoveItem = null;
        }
    }
}
