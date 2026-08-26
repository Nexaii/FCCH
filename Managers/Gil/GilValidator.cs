using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Common;

namespace FCCH.Managers.Gil
{
    public struct GilValidationResult
    {
        public bool IsValid;
        public uint AdjustedAmount;
        public string ErrorMessage;
    }

    public unsafe static class GilValidator
    {
        public const uint MaxGil = 999_999_999;

        public static GilValidationResult ValidateDeposit(uint requestedAmount, uint playerGil, uint fcGil, uint alwaysKeep)
        {
            if (requestedAmount == 0)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "Amount must be greater than 0." };

            if (playerGil < alwaysKeep)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = $"Your Gil ({playerGil:N0}) is below the 'Always Keep' threshold ({alwaysKeep:N0}). Deposit blocked." };

            uint maxDepositable = playerGil - alwaysKeep;
            if (maxDepositable == 0)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "No Gil available to deposit after 'Always Keep' threshold." };

            uint fcRoomLeft = MaxGil - fcGil;
            if (fcRoomLeft == 0)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "FC chest is at maximum Gil capacity." };

            uint finalAmount = (uint)Math.Min((long)requestedAmount, (long)maxDepositable);
            finalAmount = (uint)Math.Min((long)finalAmount, (long)fcRoomLeft);

            return new GilValidationResult { IsValid = true, AdjustedAmount = finalAmount, ErrorMessage = "" };
        }

        public static GilValidationResult ValidateWithdraw(uint requestedAmount, uint playerGil, uint fcGilHeader, Func<uint> getContainerQuantity)
        {
            if (requestedAmount == 0)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "Amount must be greater than 0." };

            uint actualContainerGil = getContainerQuantity();
            
            if (actualContainerGil == 0 && fcGilHeader > 0)
            {
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "FC Gil inventory is not fully loaded yet. Please try again." };
            }

            if (actualContainerGil == 0)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "FC chest has no Gil to withdraw." };

            uint playerRoomLeft = MaxGil - playerGil;
            if (playerRoomLeft == 0)
                return new GilValidationResult { IsValid = false, AdjustedAmount = 0, ErrorMessage = "Your inventory is at maximum Gil capacity." };

            uint finalAmount = (uint)Math.Min((long)requestedAmount, (long)actualContainerGil);
            finalAmount = (uint)Math.Min((long)finalAmount, (long)playerRoomLeft);

            if (finalAmount < requestedAmount)
            {
                ChatHelper.Verbose($"Amount clamped from {requestedAmount:N0} to {finalAmount:N0} due to constraints.");
            }

            return new GilValidationResult { IsValid = true, AdjustedAmount = finalAmount, ErrorMessage = "" };
        }

        public static bool IsChestOpen()
        {
            var addon = Plugin.GameGui.GetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>(Constants.FreeCompanyChestAddonName, 1);
            return addon != null && addon->IsVisible;
        }

        public static bool CanAccessGilTab()
        {
            var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)Plugin.GameGui.GetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>(Constants.FreeCompanyChestAddonName, 1);
            if (addon == null || !addon->IsVisible) return false;

            var gilNode = addon->GetNodeById(16);
            if (gilNode == null) return false;

            return gilNode->NodeFlags.HasFlag(FFXIVClientStructs.FFXIV.Component.GUI.NodeFlags.Enabled);
        }

        public static uint GetPlayerGil()
        {
            var invManager = InventoryManager.Instance();
            return invManager != null ? invManager->GetGil() : 0;
        }

        public static uint GetFCGilHeader()
        {
            var invManager = InventoryManager.Instance();
            return invManager != null ? invManager->GetFreeCompanyGil() : 0;
        }

        public static uint GetFCGilContainerQuantity()
        {
            var invManager = InventoryManager.Instance();
            if (invManager == null) return 0;
            
            var container = invManager->GetInventoryContainer(InventoryType.FreeCompanyGil);
            if (container == null || !container->IsLoaded) return 0;
            
            var item = container->GetInventorySlot(0);
            return item != null ? (uint)item->Quantity : 0u;
        }

        public static string GetPermissionString(ChestManager chestManager)
        {
            if (!IsChestOpen()) return "Chest Closed";
            return ChestManager.NameAccess(chestManager.GetChestAccess(InventoryType.FreeCompanyGil));
        }
    }
}
