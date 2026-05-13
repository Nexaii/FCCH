using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FCCH.Managers;
using FCCH.Managers.Gil;

namespace FCCH.Common
{
    /// <summary>
    /// FCCH external IPC surface. Method bodies registered with Dalamud IPC are the wire contract.
    /// Named IPC-backing methods in this class carry per-method XML doc; trivial pass-through lambdas
    /// inherit their contract from this summary: every mutation call returns <c>true</c> when FCCH
    /// has accepted and queued the request, <c>false</c> when the command gate refuses or arguments
    /// are invalid. Read-only IPC never throws for normal unavailable/busy states.
    /// </summary>
    public sealed class FCCHIpc : IDisposable
    {
        private const string Prefix = "FCCH.";
        private const int ContractVersion = 3;

        private const string TokenReady = "";
        private const string TokenBusy = "busy";
        private const string TokenChestClosed = "chest-closed";
        private const string TokenUnavailable = "unavailable";

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly ChestHelper _chestHelper;
        private readonly GilManager _gilManager;
        private readonly List<object> _providers = new();
        private readonly List<Action> _unregister = new();

        public FCCHIpc(IDalamudPluginInterface pluginInterface, ChestHelper chestHelper, GilManager gilManager)
        {
            _pluginInterface = pluginInterface;
            _chestHelper = chestHelper;
            _gilManager = gilManager;

            Register("IsAvailable", IsAvailable);
            Register("IsBusy", IsBusy);
            Register("GetVersion", GetVersion);
            Register("CanAcceptCommand", CanAcceptCommand);
            Register("GetBlockReason", GetBlockReason);
            Register("DepositAll", () => Mutation("DepositAll", TryQueue(_chestHelper.DepositAll)));
            Register("DepositDuplicates", () => Mutation("DepositDuplicates", TryQueue(_chestHelper.DepositDuplicates)));
            Register("DepositCustom", () => Mutation("DepositCustom", TryQueue(_chestHelper.DepositCustomItems)));
            Register("WithdrawAll", () => Mutation("WithdrawAll", TryQueue(_chestHelper.WithdrawAll)));
            Register("WithdrawCustom", () => Mutation("WithdrawCustom", TryQueue(_chestHelper.WithdrawCustomItems)));
            Register("WithdrawWorkshop", () => Mutation("WithdrawWorkshop", TryQueue(_chestHelper.WithdrawWorkshopItems)));
            Register("Stop", () => Mutation("Stop", Stop()));
            Register<uint, int, bool>("DepositItem", (itemId, quantity) => Mutation("DepositItem", TryQueueItem(itemId, quantity, _chestHelper.DepositMaterials)));
            Register<uint, int, bool>("WithdrawItem", (itemId, quantity) => Mutation("WithdrawItem", TryQueueItem(itemId, quantity, _chestHelper.WithdrawMaterials)));
            Register<Dictionary<uint, int>, bool>("DepositItems", items => Mutation("DepositItems", TryQueueItems(items, _chestHelper.DepositMaterials)));
            Register<Dictionary<uint, int>, bool>("WithdrawItems", items => Mutation("WithdrawItems", TryQueueItems(items, _chestHelper.WithdrawMaterials)));
            Register<Dictionary<uint, int>, bool>("WithdrawMissingItems", items => Mutation("WithdrawMissingItems", TryQueueItems(items, _chestHelper.WithdrawMissingMaterials)));
            Register<uint, long>("GetChestItemCount", _chestHelper.GetItemCountInChest);
            Register<uint, long>("GetWithdrawableItemCount", _chestHelper.GetWithdrawableItemCountInChest);
            Register<uint, long>("GetPlayerInventoryCount", _chestHelper.GetItemCountInPlayerInventory);
            Register<string, bool>("DepositGil", amount => Mutation("DepositGil", TryQueueGil(amount, _gilManager.HandleDepositCommand)));
            Register<string, bool>("WithdrawGil", amount => Mutation("WithdrawGil", TryQueueGil(amount, _gilManager.HandleWithdrawCommand)));
        }

        /// <summary><c>true</c> when FCCH IPC is loaded.</summary>
        private bool IsAvailable()
            => true;

        /// <summary><c>true</c> when FCCH is indexing or running an operation.</summary>
        private bool IsBusy()
            => !_chestHelper.CanAcceptCommand().CanRun;

        /// <summary>Returns the FCCH IPC contract version. Bumped whenever any IPC signature, return contract, or documented behavior changes.</summary>
        private int GetVersion()
            => ContractVersion;

        /// <summary>Advisory readiness probe. <c>true</c> when the command gate is open. Consumers must still treat the mutation IPC return value as authoritative.</summary>
        private bool CanAcceptCommand()
            => _chestHelper.CanAcceptCommand().CanRun;

        /// <summary>Returns an empty string when ready, otherwise a machine-stable block-reason token. See README IPC section for the canonical token enumeration.</summary>
        private string GetBlockReason()
        {
            try
            {
                if (_chestHelper.IsUnavailable()) return TokenUnavailable;
                if (!_chestHelper.CanAcceptCommand().CanRun) return TokenBusy;
                if (!_chestHelper.IsChestAddonVisible()) return TokenChestClosed;
                return TokenReady;
            }
            catch
            {
                return TokenUnavailable;
            }
        }

        private bool TryQueue(Action action)
        {
            if (!CanQueue()) return false;
            _chestHelper.ProcessCommand(action);
            return true;
        }

        private bool TryQueueGil(string amount, Action<string> action)
        {
            if (!CanQueue() || !_gilManager.IsValidAmountSyntax(amount)) return false;
            _chestHelper.ProcessCommand(() => action(amount));
            return true;
        }

        private bool TryQueueItem(uint itemId, int quantity, Action<Dictionary<uint, int>> action)
        {
            if (itemId == 0 || quantity <= 0) return false;
            return TryQueueItems(new Dictionary<uint, int> { [itemId] = quantity }, action);
        }

        private bool TryQueueItems(Dictionary<uint, int>? items, Action<Dictionary<uint, int>> action)
        {
            if (!CanQueue()) return false;

            var sanitized = NormalizeItems(items);
            if (sanitized.Count == 0) return false;

            _chestHelper.ProcessCommand(() => action(sanitized));
            return true;
        }

        private bool CanQueue() => _chestHelper.CanAcceptCommand().CanRun;

        private static Dictionary<uint, int> NormalizeItems(Dictionary<uint, int>? items)
        {
            var result = new Dictionary<uint, int>();
            if (items == null) return result;

            foreach (var (itemId, quantity) in items)
            {
                if (itemId == 0 || quantity <= 0) continue;
                result[itemId] = quantity;
            }

            return result;
        }

        private bool Stop()
        {
            _chestHelper.Stop();
            return true;
        }

        private bool Mutation(string name, bool result)
        {
            if (result)
            {
                Plugin.PluginLog.Info($"[FCCH.IPC] {name} accepted.");
            }
            else
            {
                var reason = GetBlockReason();
                var token = reason.Length == 0 ? "rejected" : reason;
                Plugin.PluginLog.Info($"[FCCH.IPC] {name} refused (reason={token}).");
            }
            return result;
        }

        private void Register<TReturn>(string name, Func<TReturn> func)
        {
            var provider = _pluginInterface.GetIpcProvider<TReturn>(Prefix + name);
            provider.RegisterFunc(func);
            _providers.Add(provider);
            _unregister.Add(provider.UnregisterFunc);
        }

        private void Register<T, TReturn>(string name, Func<T, TReturn> func)
        {
            var provider = _pluginInterface.GetIpcProvider<T, TReturn>(Prefix + name);
            provider.RegisterFunc(func);
            _providers.Add(provider);
            _unregister.Add(provider.UnregisterFunc);
        }

        private void Register<T1, T2, TReturn>(string name, Func<T1, T2, TReturn> func)
        {
            var provider = _pluginInterface.GetIpcProvider<T1, T2, TReturn>(Prefix + name);
            provider.RegisterFunc(func);
            _providers.Add(provider);
            _unregister.Add(provider.UnregisterFunc);
        }

        public void Dispose()
        {
            for (int i = _unregister.Count - 1; i >= 0; i--)
            {
                try { _unregister[i](); } catch (Exception e) { try { Plugin.PluginLog.Error(e, "[FCCH.IPC] Unregister threw."); } catch { } }
            }

            _unregister.Clear();
            _providers.Clear();
        }
    }
}
