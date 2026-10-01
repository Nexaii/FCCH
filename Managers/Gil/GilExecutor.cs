using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;
using FCCH.Models;
using FCCH.Diagnostics;

namespace FCCH.Managers.Gil
{
    public unsafe class GilExecutor
    {
        private readonly Configuration _configuration;
        private readonly ChestManager _chestManager;
        private readonly MoveManager _moveManager;
        private readonly Action<PendingGilTransaction> _setPendingTransaction;

        public GilExecutor(Configuration configuration, ChestManager chestManager, MoveManager moveManager, Action<PendingGilTransaction> setPendingTransaction)
        {
            _configuration = configuration;
            _chestManager = chestManager;
            _moveManager = moveManager;
            _setPendingTransaction = setPendingTransaction;
        }

        public void ExecuteDeposit(uint amount, bool quiet = false)
        {
            if (!GilValidator.IsChestOpen())
            {
                if (!quiet) Chat.Error("Open the FC chest to deposit gil.");
                return;
            }

            var access = _chestManager.GetChestAccess(InventoryType.FreeCompanyGil);
            if (access != Constants.FCPermissions.FullAccess && access != Constants.FCPermissions.DepositOnly)
            {
                if (!quiet) Chat.Result("Skipped gd (gil).");
                return;
            }

            var validationResult = GilValidator.ValidateDeposit(amount, GilValidator.GetPlayerGil(), GilValidator.GetFCGilHeader(), _configuration.GilAlwaysKeep);
            if (!validationResult.IsValid)
            {
                if (!quiet) Chat.Error(validationResult.ErrorMessage);
                return;
            }

            var finalAmount = validationResult.AdjustedAmount;
            if (finalAmount == 0)
            {
                if (!quiet) Chat.Result("No gil to deposit (constraints).");
                return;
            }

            if (finalAmount < amount)
                Log.Verbose($"Amount clamped from {amount:N0} to {finalAmount:N0} due to constraints.");

            _setPendingTransaction(new PendingGilTransaction
            {
                Amount = finalAmount,
                RequestedAmount = amount,
                IsDeposit = true,
                TimestampMs = Environment.TickCount64
            });

            var bank = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>("Bank", 1);
            if (bank != null && bank->IsVisible)
            {
                FireBankDeposit(bank, finalAmount);
            }
            else
            {
                SwitchToGilTab();
                Log.Verbose($"Queued deposit of {finalAmount:N0} Gil.");
            }
        }

        public void ExecuteWithdraw(uint amount)
        {
            if (!GilValidator.IsChestOpen())
            {
                Chat.Error("Open the FC chest to withdraw gil.");
                return;
            }

            if (_chestManager.GetChestAccess(InventoryType.FreeCompanyGil) != Constants.FCPermissions.FullAccess)
            {
                Chat.Result("Skipped gw (gil).");
                return;
            }

            var validationResult = GilValidator.ValidateWithdraw(amount, GilValidator.GetPlayerGil(), GilValidator.GetFCGilHeader(), GilValidator.GetFCGilContainerQuantity);
            if (!validationResult.IsValid)
            {
                Chat.Error(validationResult.ErrorMessage);
                return;
            }

            var finalAmount = validationResult.AdjustedAmount;
            if (finalAmount == 0)
            {
                Chat.Result("No gil to withdraw (constraints).");
                return;
            }

            _setPendingTransaction(new PendingGilTransaction
            {
                Amount = finalAmount,
                RequestedAmount = amount,
                IsDeposit = false,
                TimestampMs = Environment.TickCount64
            });

            var moveOp = new MoveOperation
            {
                SrcInv = InventoryType.FreeCompanyGil,
                SrcSlot = 0,
                DstInv = InventoryType.Currency,
                DstSlot = 0,
                ItemId = 1,
                Amount = finalAmount,
                IsNativeMove = true
            };
            
            _moveManager.Enqueue(moveOp);
            Log.Verbose($"Queued withdrawal of {finalAmount:N0} Gil.");
        }

        internal static uint CalculateAutoAmount(Configuration configuration, uint playerGil)
        {
            long baseGil = configuration.GilPercentAboveKeep
                ? Math.Max(0L, (long)playerGil - configuration.GilAlwaysKeep)
                : playerGil;

            long amount = configuration.GilMode == GilDepositMode.Percentage
                ? baseGil * Math.Clamp(configuration.GilPercentage, 1, 100) / 100
                : configuration.GilFixedAmount;

            return amount <= 0 ? 0u : (uint)amount;
        }

        public void AutoDeposit()
        {
            if (_configuration.GilMode == GilDepositMode.Disabled) return;

            uint playerGil = GilValidator.GetPlayerGil();
            uint amount = CalculateAutoAmount(_configuration, playerGil);
            if (amount == 0) return;

            if (_configuration.GilMinimumDeposit > 0)
            {
                var clamped = GilValidator.ValidateDeposit(amount, playerGil, GilValidator.GetFCGilHeader(), _configuration.GilAlwaysKeep);
                if (clamped.AdjustedAmount < _configuration.GilMinimumDeposit) return;
            }

            ExecuteDeposit(amount, quiet: true);
        }

        private static void FireBankDeposit(AtkUnitBase* bank, uint amount)
        {
            Callback.Fire(bank, true, 3, amount);
            Callback.Fire(bank, true, 0);
        }

        private void SwitchToGilTab()
        {
            var addon = Common.ChestAddon.GetOpen();
            if (addon == null) return;

            Callback.Fire(addon, true, 2);
        }
    }

    public struct PendingGilTransaction
    {
        public uint Amount;
        public uint RequestedAmount;
        public bool IsDeposit;
        public long TimestampMs;
    }
}
