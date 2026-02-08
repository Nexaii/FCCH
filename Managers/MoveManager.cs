using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FCCH.Models;
using FCCH.Common;
using System.Runtime.InteropServices;

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
        
        private delegate nint MoveItemDelegate(void* agent, InventoryType srcInv, uint srcSlot, InventoryType dstInv, uint dstSlot);
        private MoveItemDelegate? _moveItem;
        
        private delegate nint MoveItemWithQuantityDelegate(IntPtr manager, InventoryType srcType, ushort srcSlot, InventoryType dstType, ushort dstSlot, uint quantity);
        private MoveItemWithQuantityDelegate? _moveItemWithQuantity;

        public MoveManager(Configuration config, ChestManager chestManager)
        {
            _configuration = config;
            _chestManager = chestManager;
            InitializeDelegates();
        }

        private void InitializeDelegates()
        {
            try
            {
                
                // Thanks to Taurenkey (PandorasBox) for discovering this signature
                // Agent MoveItem - for FC chest full stack moves
                var agentSig = "40 53 55 56 57 41 57 48 83 EC ?? 45 33 FF";
                if (Plugin.SigScanner.TryScanText(agentSig, out var agentPtr))
                {
                    _moveItem = Marshal.GetDelegateForFunctionPointer<MoveItemDelegate>(agentPtr);
                    DebugLog($"[MoveManager] Agent MoveItem identified at 0x{agentPtr:X16}");
                }
                else
                {
                    Plugin.PluginLog.Warning("[MoveManager] Agent MoveItem signature mismatch.");
                }
                
                var nativeSig = "48 89 5C 24 10 48 89 6C 24 18 56 57 41 55 41 56 41 57 48 83 EC 30";
                if (Plugin.SigScanner.TryScanText(nativeSig, out var nativePtr))
                {
                    _moveItemWithQuantity = Marshal.GetDelegateForFunctionPointer<MoveItemWithQuantityDelegate>(nativePtr);
                    DebugLog($"[MoveManager] Native MoveItemWithQuantity identified at 0x{nativePtr:X16}");
                }
                else
                {
                    Plugin.PluginLog.Warning("[MoveManager] Native MoveItemWithQuantity signature mismatch.");
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning($"[MoveManager] Delegate initialization failed: {ex.Message}");
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
                if (op.ItemId == 1)
                {
                    ChatHelper.Verbose($"Processing Gil Transaction: {op.Amount:N0}");
                    if (op.IsNativeMove && _moveItemWithQuantity != null)
                    {
                        _moveItemWithQuantity((IntPtr)invManager, op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, op.Amount);
                        DebugLog($"[Success] Native moved {op.Amount} Gil");
                    }
                    else
                    {
                         DebugLog($"[Warning] Non-native Gil move requested. Gil operations handled by GilManager.");
                    }
                }
                else if (op.IsNativeMove && _moveItemWithQuantity != null)
                {
                    _moveItemWithQuantity((IntPtr)invManager, op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, op.Amount);
                    DebugLog($"[Success] Native moved {op.Amount}x Item#{op.ItemId}");
                }
                else if (op.IsNativeMove && _moveItemWithQuantity == null)
                {
                    Plugin.PluginLog.Error($"[Move] Native delegate missing! Cannot do partial move for Item#{op.ItemId}. Skipping.");
                    DebugLog($"[Error] Native delegate missing, skipping partial move (would break Leave 1 rule)");
                    return;
                }
                else if (_moveItem != null && (IsFCPage(op.SrcInv) || IsFCPage(op.DstInv)))
                {
                    var agentModule = UIModule.Instance()->GetAgentModule();
                    if (agentModule == null) { DebugLog("[Error] AgentModule is null"); return; }
                    var agent = agentModule->GetAgentByInternalId(AgentId.FreeCompanyChest);
                    if (agent == null) { DebugLog("[Error] FC Chest Agent is null"); return; }
                    _moveItem(agent, op.SrcInv, op.SrcSlot, op.DstInv, op.DstSlot);
                    DebugLog($"[Success] Agent moved Item#{op.ItemId}");
                }
                else
                {
                    invManager->MoveItemSlot(op.SrcInv, (ushort)op.SrcSlot, op.DstInv, (ushort)op.DstSlot, true);
                    DebugLog($"[Success] Fallback moved Item#{op.ItemId}");
                }
                
                LastActionTime = DateTime.Now;
                ProcessedThisFrame = true;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, $"[Move] Transaction aborted for Item#{op.ItemId}");
                DebugLog($"[Error] Move failed: {ex.Message}");
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
        }

        public void Dispose()
        {
            MoveQueue.Clear();
            _moveItem = null;
            _moveItemWithQuantity = null;
        }
    }
}
