using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    internal static class ItemListEligibility
    {
        public static bool TryGetAllowedItem(uint itemId, out Item item)
        {
            item = default;

            var sheet = Plugin.Data.GetExcelSheet<Item>();
            if (sheet == null)
                return false;

            var row = sheet.GetRowOrDefault(itemId);
            if (row == null)
                return false;

            item = row.Value;
            return IsAllowed(item);
        }

        public static bool IsAllowed(Item item)
        {
            if (Constants.UntradableFcStorableItemIds.Contains(item.RowId))
                return true;

            var name = item.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (item.ItemSortCategory.RowId == Constants.CurrencySortCategoryId)
                return false;

            return !item.IsUntradable
                && item.RowId != 1
                && !(item.RowId >= 2 && item.RowId <= 19);
        }

        public static bool IsIneligible(uint itemId)
        {
            var sheet = Plugin.Data.GetExcelSheet<Item>();
            return sheet != null && sheet.TryGetRow(itemId, out var row) && !IsAllowed(row);
        }

        public static List<string> RemoveIneligible(Configuration config)
        {
            var removed = new List<string>();
            config.WithdrawItems.RemoveAll(x => Reject(x.ItemId, removed));
            config.IgnoreList.RemoveAll(x => Reject(x.ItemId, removed));
            return removed;
        }

        private static bool Reject(uint itemId, List<string> removed)
        {
            if (!IsIneligible(itemId))
                return false;

            removed.Add(ItemNames.Get(itemId));
            return true;
        }
    }
}
