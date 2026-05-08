using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FCCH.Managers;
using FCCH.Managers.Gil;

namespace FCCH.Common
{
    public sealed class FCCHIpc : IDisposable
    {
        private const string Prefix = "FCCH.";

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
            Register("DepositAll", () => TryQueue(_chestHelper.DepositAll));
            Register("DepositDuplicates", () => TryQueue(_chestHelper.DepositDuplicates));
            Register("DepositCustom", () => TryQueue(_chestHelper.DepositCustomItems));
            Register("WithdrawAll", () => TryQueue(_chestHelper.WithdrawAll));
            Register("WithdrawCustom", () => TryQueue(_chestHelper.WithdrawCustomItems));
            Register("WithdrawWorkshop", () => TryQueue(_chestHelper.WithdrawWorkshopItems));
            Register("Stop", Stop);
            Register<string, bool>("DepositGil", amount => TryQueueGil(amount, _gilManager.HandleDepositCommand));
            Register<string, bool>("WithdrawGil", amount => TryQueueGil(amount, _gilManager.HandleWithdrawCommand));
        }

        private bool IsAvailable()
            => true;

        private bool IsBusy()
            => _chestHelper.IsProcessing;

        private bool TryQueue(Action action)
        {
            if (IsBusy()) return false;
            _chestHelper.ProcessCommand(action);
            return true;
        }

        private bool TryQueueGil(string amount, Action<string> action)
        {
            if (IsBusy() || !_gilManager.IsValidAmountSyntax(amount)) return false;
            _chestHelper.ProcessCommand(() => action(amount));
            return true;
        }

        private bool Stop()
        {
            _chestHelper.Stop();
            return true;
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

        public void Dispose()
        {
            foreach (var unregister in _unregister)
                unregister();

            _unregister.Clear();
            _providers.Clear();
        }
    }
}
