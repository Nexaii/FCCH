using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Common;
using FCCH.Models;

namespace FCCH.Managers.Gil
{
    public unsafe class GilExecutor
    {
        private readonly Configuration _configuration;
        private readonly MoveManager _moveManager;
        private readonly Action<PendingGilTransaction> _setPendingTransaction;

        public GilExecutor(Configuration configuration, MoveManager moveManager, Action<PendingGilTransaction> setPendingTransaction)
        {
            _configuration = configuration;
            _moveManager = moveManager;
            _setPendingTransaction = setPendingTransaction;
        }

        public void ExecuteDeposit(uint amount)
        {
            if (!GilValidator.IsChestOpen())
            {
                ChatHelper.Error("Company Chest must be open to deposit Gil.");
                return;
            }

            if (!GilValidator.CanAccessGilTab())
            {
                ChatHelper.Error("Gil tab is not available. You may not have permission.");
                return;
            }

            var validationResult = GilValidator.ValidateDeposit(amount, GilValidator.GetPlayerGil(), GilValidator.GetFCGilHeader(), _configuration.GilAlwaysKeep);
            if (!validationResult.IsValid)
            {
                ChatHelper.Error(validationResult.ErrorMessage);
                return;
            }

            var finalAmount = validationResult.AdjustedAmount;
            if (finalAmount == 0)
            {
                ChatHelper.Info("No Gil to deposit after applying constraints.");
                return;
            }

            _setPendingTransaction(new PendingGilTransaction
            {
                Amount = finalAmount,
                IsDeposit = true,
                Timestamp = DateTime.Now
            });

            var moveOp = new MoveOperation
            {
                SrcInv = InventoryType.Currency,
                SrcSlot = 0,
                DstInv = InventoryType.FreeCompanyGil,
                DstSlot = 0,
                ItemId = 1,
                Amount = finalAmount,
                IsNativeMove = true
            };

            _moveManager.Enqueue(moveOp);
            ChatHelper.Info($"Queued deposit of {finalAmount:N0} Gil.");
        }

        public void ExecuteWithdraw(uint amount)
        {
            if (!GilValidator.IsChestOpen())
            {
                ChatHelper.Error("Company Chest must be open to withdraw Gil.");
                return;
            }

            if (!GilValidator.CanAccessGilTab())
            {
                ChatHelper.Error("Gil tab is not available. You may not have permission.");
                return;
            }

            var validationResult = GilValidator.ValidateWithdraw(amount, GilValidator.GetPlayerGil(), GilValidator.GetFCGilHeader(), GilValidator.GetFCGilContainerQuantity);
            if (!validationResult.IsValid)
            {
                ChatHelper.Error(validationResult.ErrorMessage);
                return;
            }

            var finalAmount = validationResult.AdjustedAmount;
            if (finalAmount == 0)
            {
                ChatHelper.Info("No Gil to withdraw after applying constraints.");
                return;
            }

            _setPendingTransaction(new PendingGilTransaction
            {
                Amount = finalAmount,
                IsDeposit = false,
                Timestamp = DateTime.Now
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
            ChatHelper.Info($"Queued withdrawal of {finalAmount:N0} Gil.");
        }

        public void AutoDeposit()
        {
            if (_configuration.GilMode == GilDepositMode.Disabled) return;
            if (!GilValidator.CanAccessGilTab()) return;

            uint amount = 0;
            uint playerGil = GilValidator.GetPlayerGil();

            if (_configuration.GilMode == GilDepositMode.Percentage)
            {
                var pct = Math.Clamp(_configuration.GilPercentage, 1, 100);
                amount = (uint)(playerGil * pct / 100);
            }
            else if (_configuration.GilMode == GilDepositMode.FixedAmount)
            {
                amount = _configuration.GilFixedAmount;
            }

            if (amount == 0) return;

            var validationResult = GilValidator.ValidateDeposit(amount, playerGil, GilValidator.GetFCGilHeader(), _configuration.GilAlwaysKeep);
            if (!validationResult.IsValid || validationResult.AdjustedAmount == 0) return;

            _setPendingTransaction(new PendingGilTransaction
            {
                Amount = validationResult.AdjustedAmount,
                IsDeposit = true,
                Timestamp = DateTime.Now
            });

            var moveOp = new MoveOperation
            {
                SrcInv = InventoryType.Currency,
                SrcSlot = 0,
                DstInv = InventoryType.FreeCompanyGil,
                DstSlot = 0,
                ItemId = 1,
                Amount = validationResult.AdjustedAmount,
                IsNativeMove = true
            };

            _moveManager.Enqueue(moveOp);
        }
    }

    public struct PendingGilTransaction
    {
        public uint Amount;
        public bool IsDeposit;
        public DateTime Timestamp;
    }
}
