using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    public static class ItemNames
    {
        public static string Get(uint itemId)
        {
            try
            {
                var sheet = Plugin.Data.GetExcelSheet<Item>();
                if (sheet == null) return $"Item #{itemId}";
                var row = sheet.GetRowOrDefault(itemId);
                return row != null ? row.Value.Name.ToString() : $"Item #{itemId}";
            }
            catch { return $"Item #{itemId}"; }
        }
    }
}
