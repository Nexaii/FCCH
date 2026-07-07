using System;
using System.Collections.Generic;
using System.Linq;
using FCCH.Common;
using Lumina.Excel.Sheets;

namespace FCCH.Managers.Organizer
{
    public static class OrgFilters
    {
        private static readonly HashSet<uint> EquipmentCategoryIds = new()
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
            11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
            32, 33, 34, 35, 36, 37, 38,
            40, 41, 42, 43,
            84, 87, 88, 89,
            96, 97, 98,
            105, 106, 107, 108, 109, 110,
            111, 112
        };

        private static readonly HashSet<uint> MedicinesMealsCategoryIds = new() { 44, 46 };

        private static readonly HashSet<uint> MaterialsCategoryIds = new()
        {
            45, 47, 48, 49, 50, 51, 52, 53, 54, 60, 63, 83
        };

        private static readonly HashSet<uint> MateriaCategoryIds = new() { 58 };

        private static readonly HashSet<uint> RegistrableCategoryIds = new() { 81, 86, 90, 91, 92, 93, 101, 102, 103, 104 };

        private static readonly HashSet<uint> DyeCategoryIds = new() { 55 };

        private static readonly HashSet<uint> HousingCategoryIds = new()
        {
            56, 57,
            64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76,
            77, 78, 79, 80,
            94, 95
        };

        private static readonly HashSet<uint> GardeningCategoryIds = new() { 82 };

        private static readonly HashSet<uint> MiscCategoryIds = new() { 61 };

        private static readonly HashSet<uint> BlockedCategoryIds = new() { 62, 85, 99, 100 };

        private static readonly Dictionary<uint, uint> _itemCategoryCache = new();
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var itemSheet = Plugin.Data.GetExcelSheet<Item>();
                if (itemSheet == null) return;

                foreach (var item in itemSheet)
                {
                    _itemCategoryCache[item.RowId] = item.ItemUICategory.RowId;
                }
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[OrgFilters] Item category cache build failed; category filters degraded.");
            }
        }

        private static uint GetItemUICategory(uint itemId)
        {
            Initialize();
            return _itemCategoryCache.TryGetValue(itemId, out var cat) ? cat : 0;
        }

        private static bool IsBlocked(uint categoryId)
        {
            return BlockedCategoryIds.Contains(categoryId);
        }

        public static Func<ChestManager.ScannedSlot, bool> GetMultiPredicate(HashSet<OrgFilterCategory> filters)
        {
            Initialize();

            return slot =>
            {
                var categoryId = GetItemUICategory(slot.ItemId);

                if (IsBlocked(categoryId) && !Constants.UntradableFcStorableItemIds.Contains(slot.ItemId)) return false;

                if (filters.Contains(OrgFilterCategory.AllItems)) return true;
                if (filters.Count == 0) return true;

                bool match = false;
                if (filters.Contains(OrgFilterCategory.Equipment) && EquipmentCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.MedicinesMeals) && MedicinesMealsCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Materials) && MaterialsCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Materia) && MateriaCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Registrable) && RegistrableCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Dye) && DyeCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Housing) && HousingCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Gardening) && GardeningCategoryIds.Contains(categoryId)) match = true;
                if (!match && filters.Contains(OrgFilterCategory.Miscellaneous) && MiscCategoryIds.Contains(categoryId)) match = true;

                return match;
            };
        }

        public static string GetDisplayName(OrgFilterCategory category)
        {
            return category switch
            {
                OrgFilterCategory.AllItems => "All Items",
                OrgFilterCategory.Equipment => "Equipment",
                OrgFilterCategory.MedicinesMeals => "Medicine/Meals",
                OrgFilterCategory.Materials => "Materials",
                OrgFilterCategory.Materia => "Materia",
                OrgFilterCategory.Registrable => "Registrable",
                OrgFilterCategory.Dye => "Dye",
                OrgFilterCategory.Housing => "Housing",
                OrgFilterCategory.Gardening => "Gardening",
                OrgFilterCategory.Miscellaneous => "Misc",
                _ => "Unknown"
            };
        }

        public static string GetSortOrderName(OrgSortOrder order)
        {
            return order switch
            {
                OrgSortOrder.ById => "By ID (Standard Order)",
                OrgSortOrder.ByName => "By Name (A-Z)",
                OrgSortOrder.ByQuantity => "By Quantity (Desc)",
                OrgSortOrder.ByCategory => "By Category",
                _ => "Unknown"
            };
        }

        public static IEnumerable<ChestManager.ScannedSlot> ApplySortOrder(
            IEnumerable<ChestManager.ScannedSlot> items,
            OrgSortOrder order,
            bool descending = false)
        {
            return order switch
            {
                OrgSortOrder.ById => descending
                    ? items.OrderByDescending(x => x.ItemId)
                    : items.OrderBy(x => x.ItemId),
                OrgSortOrder.ByName => descending
                    ? items.OrderByDescending(x => GetItemName(x.ItemId))
                    : items.OrderBy(x => GetItemName(x.ItemId)),
                OrgSortOrder.ByQuantity => descending
                    ? items.OrderBy(x => x.Quantity).ThenByDescending(x => x.ItemId)
                    : items.OrderByDescending(x => x.Quantity).ThenBy(x => x.ItemId),
                OrgSortOrder.ByCategory => descending
                    ? items.OrderByDescending(x => GetItemUICategory(x.ItemId)).ThenByDescending(x => x.ItemId)
                    : items.OrderBy(x => GetItemUICategory(x.ItemId)).ThenBy(x => x.ItemId),
                _ => items
            };
        }

        private static string GetItemName(uint itemId) => Common.ItemNames.Get(itemId);

        public static string GetCategoryName(uint itemId)
        {
            Initialize();
            var categoryId = GetItemUICategory(itemId);

            if (EquipmentCategoryIds.Contains(categoryId)) return "Equipment";
            if (MedicinesMealsCategoryIds.Contains(categoryId)) return "Medicine/Meals";
            if (MaterialsCategoryIds.Contains(categoryId)) return "Materials";
            if (MateriaCategoryIds.Contains(categoryId)) return "Materia";
            if (RegistrableCategoryIds.Contains(categoryId)) return "Registrable";
            if (DyeCategoryIds.Contains(categoryId)) return "Dye";
            if (HousingCategoryIds.Contains(categoryId)) return "Housing";
            if (GardeningCategoryIds.Contains(categoryId)) return "Gardening";
            if (MiscCategoryIds.Contains(categoryId)) return "Misc";

            return "Unknown";
        }
    }
}
