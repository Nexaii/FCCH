namespace FC_Chest_Helper.Common
{
    public static class Constants
    {
        public const string FC_CHEST_ADDON_NAME = "FreeCompanyChest";
        public const string INPUT_NUMERIC_ADDON_NAME = "InputNumeric";

        // Callback Indices
        public const int FC_CHEST_CALLBACK_ID = 2;
        public const int NUMERIC_INPUT_CALLBACK_IDX = 3;

        // FC Permission Levels (InfoProxyFreeCompany::RankData::ChestAccess)
        public static class FCPermissions
        {
            public const byte NO_ACCESS = 1;
            public const byte VIEW_ONLY = 2;
            public const byte FULL_ACCESS = 4;
            public const byte DEPOSIT_ONLY = 8;
        }

        // Inventory Sizes
        public const int FC_CHEST_PAGE_SIZE = 50;
        public const int PLAYER_INVENTORY_PAGE_SIZE = 35;
    }
}
