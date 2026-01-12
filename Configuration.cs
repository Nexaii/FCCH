using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace FC_Chest_Helper
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public class IgnoredItem
        {
            public uint ItemId { get; set; }
            public string Name { get; set; } = "";
            public bool IgnoreEntrust { get; set; }
            public bool IgnoreWithdraw { get; set; }
        }

        public List<IgnoredItem> IgnoreList { get; set; } = new();
        public bool LeaveOneItemPerStack { get; set; } = true;
        public bool DisableAskDepositAll { get; set; } = false;
        public bool DisableAskWithdrawAll { get; set; } = false;
        
        public List<WithdrawItem> WithdrawItems { get; set; } = new();
        public List<Models.ShoppingItem> ShoppingItems { get; set; } = new();
        
        public Dictionary<string, List<IgnoredItem>> IgnorePresets { get; set; } = new();
        public Dictionary<string, List<WithdrawItem>> SinglePresets { get; set; } = new();
        public Dictionary<string, List<PresetShoppingItem>> WorkshopPresets { get; set; } = new();
        
        public Dictionary<string, PresetData> WithdrawPresets { get; set; } = new();

        public bool DebugMode { get; set; } = false;
        public bool VerboseMode { get; set; } = false;
        public bool ListsOnRightSide { get; set; } = false;
        public bool IsWindowLocked { get; set; } = true;
        public string DebugLogPath { get; set; } = "FCCH_Debug.log";

        public bool LowerQualityOnDeposit { get; set; } = false;
        public bool PlayCompletionSound { get; set; } = false;
        public string CustomSoundPath { get; set; } = "";
        
        public int MoveDelayInMs { get; set; } = 700;
        public int IndexingDelayInMs { get; set; } = 150;
        public int WithdrawDelayInMs { get; set; } = 700;

        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }

    public class WithdrawItem
    {
        public uint ItemId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class PresetData
    {
        public List<WithdrawItem> WithdrawItems { get; set; } = new();
        public List<PresetShoppingItem> ShoppingList { get; set; } = new();
    }

    public class PresetShoppingItem
    {
        public uint WorkshopItemId { get; set; }
        public int Quantity { get; set; }
    }
}
