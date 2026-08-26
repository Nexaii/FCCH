using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Common
{
    public static class Constants
    {
        public static readonly InventoryType[] PlayerInventoryTypes =
        {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        public static readonly HashSet<uint> UntradableFcStorableItemIds = new()
        {
            10155, // Ceruleum Tank
            10373, // Magitek Repair Materials
        };

        public const uint CurrencySortCategoryId = 3;

        public const string FreeCompanyChestAddonName = "FreeCompanyChest";
        public const string InputNumericAddonName = "InputNumeric";
        public const string ItemDetailAddonName = "ItemDetail";
        public const string TooltipAddonName = "Tooltip";

        public const int FreeCompanyChestCallbackId = 2;
        public const int NumericInputCallbackIndex = 3;

        public static class FCPermissions
        {
            public const byte NoAccess = 1;
            public const byte ViewOnly = 2;
            public const byte FullAccess = 4;
            public const byte DepositOnly = 8;
        }

        public const int FreeCompanyChestPageSize = 50;
        public const int PlayerInventoryPageSize = 35;
    }
}
