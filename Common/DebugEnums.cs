
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FC_Chest_Helper.Common
{
    public class DebugEnums
    {
        public static void PrintValues()
        {
            ChatHelper.Debug($"FreeCompanyPage1: {(int)InventoryType.FreeCompanyPage1}");
            ChatHelper.Debug($"FreeCompanyPage2: {(int)InventoryType.FreeCompanyPage2}");
            ChatHelper.Debug($"FreeCompanyPage3: {(int)InventoryType.FreeCompanyPage3}");
            ChatHelper.Debug($"FreeCompanyPage4: {(int)InventoryType.FreeCompanyPage4}");
            ChatHelper.Debug($"FreeCompanyPage5: {(int)InventoryType.FreeCompanyPage5}");
        }
    }
}
