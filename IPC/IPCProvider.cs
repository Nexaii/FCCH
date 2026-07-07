using FCCH.Common;
using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FCCH.Managers;
using FCCH.Managers.Gil;

namespace FCCH.IPC;

public sealed class IPCProvider : IDisposable
{
    private readonly ChestHelper _chestHelper;
    private readonly GilManager _gilManager;

    private readonly ICallGateProvider<bool> _isAvailable;
    private readonly ICallGateProvider<bool> _isBusy;

    private readonly ICallGateProvider<uint, long> _getChestItemCount;
    private readonly ICallGateProvider<uint, long> _getWithdrawableItemCount;
    private readonly ICallGateProvider<uint, long> _getPlayerInventoryCount;

    private readonly ICallGateProvider<bool> _depositAll;
    private readonly ICallGateProvider<bool> _depositCustom;
    private readonly ICallGateProvider<bool> _depositDuplicates;
    private readonly ICallGateProvider<string, bool> _depositGil;
    private readonly ICallGateProvider<uint, int, bool> _depositItem;
    private readonly ICallGateProvider<Dictionary<uint, int>, bool> _depositItems;

    private readonly ICallGateProvider<bool> _withdrawAll;
    private readonly ICallGateProvider<bool> _withdrawCustom;
    private readonly ICallGateProvider<string, bool> _withdrawGil;
    private readonly ICallGateProvider<uint, int, bool> _withdrawItem;
    private readonly ICallGateProvider<Dictionary<uint, int>, bool> _withdrawItems;
    private readonly ICallGateProvider<Dictionary<uint, int>, bool> _withdrawMissingItems;
    private readonly ICallGateProvider<bool> _withdrawWorkshop;

    private readonly ICallGateProvider<bool> _stop;

    public IPCProvider(IDalamudPluginInterface pi, ChestHelper chestHelper, GilManager gilManager)
    {
        _chestHelper = chestHelper;
        _gilManager = gilManager;

        _isAvailable              = pi.GetIpcProvider<bool>(IPCNames.IsAvailable);
        _isBusy                   = pi.GetIpcProvider<bool>(IPCNames.IsBusy);
        _getChestItemCount        = pi.GetIpcProvider<uint, long>(IPCNames.GetChestItemCount);
        _getWithdrawableItemCount = pi.GetIpcProvider<uint, long>(IPCNames.GetWithdrawableItemCount);
        _getPlayerInventoryCount  = pi.GetIpcProvider<uint, long>(IPCNames.GetPlayerInventoryCount);
        _depositAll               = pi.GetIpcProvider<bool>(IPCNames.DepositAll);
        _depositCustom            = pi.GetIpcProvider<bool>(IPCNames.DepositCustom);
        _depositDuplicates        = pi.GetIpcProvider<bool>(IPCNames.DepositDuplicates);
        _depositGil               = pi.GetIpcProvider<string, bool>(IPCNames.DepositGil);
        _depositItem              = pi.GetIpcProvider<uint, int, bool>(IPCNames.DepositItem);
        _depositItems             = pi.GetIpcProvider<Dictionary<uint, int>, bool>(IPCNames.DepositItems);
        _withdrawAll              = pi.GetIpcProvider<bool>(IPCNames.WithdrawAll);
        _withdrawCustom           = pi.GetIpcProvider<bool>(IPCNames.WithdrawCustom);
        _withdrawGil              = pi.GetIpcProvider<string, bool>(IPCNames.WithdrawGil);
        _withdrawItem             = pi.GetIpcProvider<uint, int, bool>(IPCNames.WithdrawItem);
        _withdrawItems            = pi.GetIpcProvider<Dictionary<uint, int>, bool>(IPCNames.WithdrawItems);
        _withdrawMissingItems     = pi.GetIpcProvider<Dictionary<uint, int>, bool>(IPCNames.WithdrawMissingItems);
        _withdrawWorkshop         = pi.GetIpcProvider<bool>(IPCNames.WithdrawWorkshop);
        _stop                     = pi.GetIpcProvider<bool>(IPCNames.Stop);

        _isAvailable.RegisterFunc(IsAvailable);
        _isBusy.RegisterFunc(IsBusy);
        _getChestItemCount.RegisterFunc(_chestHelper.GetItemCountInChest);
        _getWithdrawableItemCount.RegisterFunc(_chestHelper.GetWithdrawableItemCountInChest);
        _getPlayerInventoryCount.RegisterFunc(_chestHelper.GetItemCountInPlayerInventory);
        _depositAll.RegisterFunc(DepositAll);
        _depositCustom.RegisterFunc(DepositCustom);
        _depositDuplicates.RegisterFunc(DepositDuplicates);
        _depositGil.RegisterFunc(DepositGil);
        _depositItem.RegisterFunc(DepositItem);
        _depositItems.RegisterFunc(DepositItems);
        _withdrawAll.RegisterFunc(WithdrawAll);
        _withdrawCustom.RegisterFunc(WithdrawCustom);
        _withdrawGil.RegisterFunc(WithdrawGil);
        _withdrawItem.RegisterFunc(WithdrawItem);
        _withdrawItems.RegisterFunc(WithdrawItems);
        _withdrawMissingItems.RegisterFunc(WithdrawMissingItems);
        _withdrawWorkshop.RegisterFunc(WithdrawWorkshop);
        _stop.RegisterFunc(Stop);
    }

    public void Dispose()
    {
        _isAvailable.UnregisterFunc();
        _isBusy.UnregisterFunc();
        _getChestItemCount.UnregisterFunc();
        _getWithdrawableItemCount.UnregisterFunc();
        _getPlayerInventoryCount.UnregisterFunc();
        _depositAll.UnregisterFunc();
        _depositCustom.UnregisterFunc();
        _depositDuplicates.UnregisterFunc();
        _depositGil.UnregisterFunc();
        _depositItem.UnregisterFunc();
        _depositItems.UnregisterFunc();
        _withdrawAll.UnregisterFunc();
        _withdrawCustom.UnregisterFunc();
        _withdrawGil.UnregisterFunc();
        _withdrawItem.UnregisterFunc();
        _withdrawItems.UnregisterFunc();
        _withdrawMissingItems.UnregisterFunc();
        _withdrawWorkshop.UnregisterFunc();
        _stop.UnregisterFunc();
    }

    private bool IsAvailable() => true;

    private bool IsBusy() => !_chestHelper.CanAcceptCommand().CanRun;

    private bool CanQueue() => _chestHelper.CanAcceptCommand().CanRun;

    private bool DepositAll() => Queue("DepositAll", _chestHelper.DepositAll);
    private bool DepositCustom() => Queue("DepositCustom", _chestHelper.DepositCustomItems);
    private bool DepositDuplicates() => Queue("DepositDuplicates", _chestHelper.DepositDuplicates);
    private bool WithdrawAll() => Queue("WithdrawAll", _chestHelper.WithdrawAll);
    private bool WithdrawCustom() => Queue("WithdrawCustom", _chestHelper.WithdrawCustomItems);
    private bool WithdrawWorkshop() => Queue("WithdrawWorkshop", _chestHelper.WithdrawWorkshopItems);

    private bool DepositGil(string amount) => QueueGil("DepositGil", amount, _gilManager.HandleDepositCommand);
    private bool WithdrawGil(string amount) => QueueGil("WithdrawGil", amount, _gilManager.HandleWithdrawCommand);

    private bool DepositItem(uint itemId, int quantity) => QueueItem("DepositItem", itemId, quantity, _chestHelper.DepositMaterials);
    private bool WithdrawItem(uint itemId, int quantity) => QueueItem("WithdrawItem", itemId, quantity, _chestHelper.WithdrawMaterials);

    private bool DepositItems(Dictionary<uint, int> items) => QueueItems("DepositItems", items, _chestHelper.DepositMaterials);
    private bool WithdrawItems(Dictionary<uint, int> items) => QueueItems("WithdrawItems", items, _chestHelper.WithdrawMaterials);
    private bool WithdrawMissingItems(Dictionary<uint, int> items) => QueueItems("WithdrawMissingItems", items, _chestHelper.WithdrawMissingMaterials);

    private bool Stop()
    {
        _chestHelper.Stop();
        LogAccepted("Stop");
        return true;
    }

    private bool Queue(string name, Action action)
    {
        if (!CanQueue()) { LogRefused(name); return false; }
        _chestHelper.ProcessCommand(action);
        LogAccepted(name);
        return true;
    }

    private bool QueueGil(string name, string amount, Action<string> action)
    {
        if (!CanQueue() || !_gilManager.IsValidAmountSyntax(amount)) { LogRefused(name); return false; }
        _chestHelper.ProcessCommand(() => action(amount));
        LogAccepted(name);
        return true;
    }

    private bool QueueItem(string name, uint itemId, int quantity, Action<Dictionary<uint, int>> action)
    {
        if (itemId == 0 || quantity <= 0) { LogRefused(name); return false; }
        return QueueItems(name, new Dictionary<uint, int> { [itemId] = quantity }, action);
    }

    private bool QueueItems(string name, Dictionary<uint, int>? items, Action<Dictionary<uint, int>> action)
    {
        if (!CanQueue()) { LogRefused(name); return false; }
        var sanitized = NormalizeItems(items);
        if (sanitized.Count == 0) { LogRefused(name); return false; }
        _chestHelper.ProcessCommand(() => action(sanitized));
        LogAccepted(name);
        return true;
    }

    private static Dictionary<uint, int> NormalizeItems(Dictionary<uint, int>? items)
    {
        var result = new Dictionary<uint, int>();
        if (items == null) return result;
        foreach (var (id, qty) in items)
        {
            if (id == 0 || qty <= 0) continue;
            result[id] = qty;
        }
        return result;
    }

    private static void LogAccepted(string name)
    {
        try { FCCHLog.Info($"[FCCH.IPC] {name} accepted."); } catch { }
    }

    private static void LogRefused(string name)
    {
        try { FCCHLog.Info($"[FCCH.IPC] {name} refused."); } catch { }
    }
}
