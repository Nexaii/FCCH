using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
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

        public DateTime LastActionTime { get; private set; } = DateTime.MinValue;
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

        public Common.InventoryRefusalWatcher? RefusalWatcher { get; set; }
        private DateTime _lastDispatchUtc = DateTime.MinValue;
        
        private delegate int InventoryManagerMoveItemDelegate(InventoryManager* manager, InventoryType srcInv, ushort srcSlot, InventoryType dstInv, ushort dstSlot, int quantity);
        private InventoryManagerMoveItemDelegate? _invManagerMoveItem;
        private const string InvManagerMoveItemSig = "48 89 5C 24 10 48 89 6C 24 18 56 57 41 55 41 56 41 57 48 83 EC 30 8D BA 60 F0 FF FF 45 0F BF E8 8D 82 FC EF FF FF 41 8B E9 44 8B FA 4C 8B F1";

        private delegate nint AgentMoveItemDelegate(void* agent, InventoryType srcInv, uint srcSlot, InventoryType dstInv, uint dstSlot);
        private AgentMoveItemDelegate? _agentMoveItem;
        private const string AgentMoveItemSig = "40 53 55 56 57 41 57 48 83 EC ?? 45 33 FF";

        public MoveManager(Configuration config, ChestManager chestManager)
        {
            _configuration = config;
            _chestManager = chestManager;
            ResolveInvManagerMoveItem();
            ResolveAgentMoveItem();
        }

        private void ResolveInvManagerMoveItem()
        {
            try
            {
                if (Plugin.SigScanner.TryScanText(InvManagerMoveItemSig, out var ptr))
                {
                    _invManagerMoveItem = Marshal.GetDelegateForFunctionPointer<InventoryManagerMoveItemDelegate>(ptr);
                    Plugin.PluginLog.Info($"[MoveManager] InventoryManager_MoveItem resolved at 0x{ptr:X16}");
                }
                else
                {
                    Plugin.PluginLog.Warning("[MoveManager] InventoryManager_MoveItem signature mismatch.");
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, "[MoveManager] Failed to resolve InventoryManager_MoveItem.");
            }
        }

        private void ResolveAgentMoveItem()
        {
            try
            {
                if (Plugin.SigScanner.TryScanText(AgentMoveItemSig, out var ptr))
                {
                    _agentMoveItem = Marshal.GetDelegateForFunctionPointer<AgentMoveItemDelegate>(ptr);
                    Plugin.PluginLog.Info($"[MoveManager] Agent MoveItem resolved at 0x{ptr:X16}");
                }
                else
                {
                    Plugin.PluginLog.Warning("[MoveManager] Agent MoveItem signature mismatch — full-stack moves will fall back to native partial path.");
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, "[MoveManager] Failed to resolve Agent MoveItem.");
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

            if ((DateTime.Now - LastActionTime).TotalMilliseconds < _configuration.MoveDelayInMs) return;

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
                LastActionTime = DateTime.Now;
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
                        
                        LastActionTime = DateTime.Now;
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
                bool dispatched = false;

                if (op.IsNativeMove && _invManagerMoveItem != null)
                {
                    int rc = _invManagerMoveItem(invManager, op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, (int)op.Amount);
                    DebugLog($"[Move/InvMgr] {op.Amount}x Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot}) rc={rc}");
                    dispatched = true;
                }
                else if (op.IsNativeMove && _invManagerMoveItem == null)
                {
                    Plugin.PluginLog.Error($"[Move] Native delegate missing — refusing to fake partial move for Item#{op.ItemId} (would break Leave-1).");
                    EmitBatchSummaryIfDrained();
                    return;
                }
                else if (_agentMoveItem != null && (IsFCPage(op.SrcInv) || IsFCPage(op.DstInv)))
                {
                    var agentModule = UIModule.Instance()->GetAgentModule();
                    if (agentModule == null) { DebugLog("[Error] AgentModule null"); EmitBatchSummaryIfDrained(); return; }
                    var agent = agentModule->GetAgentByInternalId(AgentId.FreeCompanyChest);
                    if (agent == null) { DebugLog("[Error] FC Chest Agent null"); EmitBatchSummaryIfDrained(); return; }
                    _agentMoveItem(agent, op.SrcInv, op.SrcSlot, op.DstInv, op.DstSlot);
                    DebugLog($"[Move/Agent] full-stack Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot})");
                    dispatched = true;
                }
                else
                {
                    invManager->MoveItemSlot(op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, true);
                    DebugLog($"[Move/Slot-Fallback] Item#{op.ItemId} ({op.SrcInv}:{op.SrcSlot} -> {op.DstInv}:{op.DstSlot})");
                    dispatched = true;
                }

                if (dispatched && RefusalWatcher != null && RefusalWatcher.ConsumeRefusalSince(_lastDispatchUtc))
                {
                    DebugLog($"[Move/Refused] LogMessage#{RefusalWatcher.LastRefusalLogId} on {guardTab}");
                    NoteRefusal(guardTab);
                }
                else if (dispatched)
                {
                    NoteSuccess(guardTab);
                }

                LastActionTime = DateTime.Now;
                ProcessedThisFrame = true;
                EmitBatchSummaryIfDrained();
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, $"[Move] Transaction aborted for Item#{op.ItemId}");
                DebugLog($"[Error] Move failed: {ex.Message}");
                EmitBatchSummaryIfDrained();
            }
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
            Plugin.PluginLog.Info(msg);
            Common.DebugFileLogger.Enqueue(_configuration.DebugLogPath, msg);
        }
        
        public void SetDelay()
        {
            LastActionTime = DateTime.Now;
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

        private static InventoryType DepositGuardTab(in MoveOperation op) => op.DstInv;
        private static InventoryType WithdrawGuardTab(in MoveOperation op) => op.SrcInv;

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
            _agentMoveItem = null;
        }
    }
}
