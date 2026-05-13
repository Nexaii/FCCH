using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    internal static class ItemListEligibility
    {
        private static readonly HashSet<string> AlwaysAllowedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ceruleum Tank",
            "Magitek Repair Materials",
        };

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
            var name = item.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (AlwaysAllowedNames.Contains(name))
                return true;

            return !item.IsUntradable
                && item.RowId != 1
                && !(item.RowId >= 2 && item.RowId <= 19);
        }
    }
}
