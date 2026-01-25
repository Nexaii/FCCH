
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Common
{
    public class DebugEnums
    {
        public static string GetDebugInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"FreeCompanyPage1: {(int)InventoryType.FreeCompanyPage1}");
            sb.AppendLine($"FreeCompanyPage2: {(int)InventoryType.FreeCompanyPage2}");
            sb.AppendLine($"FreeCompanyPage3: {(int)InventoryType.FreeCompanyPage3}");
            sb.AppendLine($"FreeCompanyPage4: {(int)InventoryType.FreeCompanyPage4}");
            sb.AppendLine($"FreeCompanyPage5: {(int)InventoryType.FreeCompanyPage5}");
            sb.AppendLine($"FreeCompanyGil: {(int)InventoryType.FreeCompanyGil}");
            sb.AppendLine($"FreeCompanyCrystals: {(int)InventoryType.FreeCompanyCrystals}");
            return sb.ToString().TrimEnd();
        }
    }
}
