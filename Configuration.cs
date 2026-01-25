using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace FCCH
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
        public double IndexingTimeoutSeconds { get; set; } = 3.0;

        public GilDepositMode GilMode { get; set; } = GilDepositMode.Disabled;
        public int GilPercentage { get; set; } = 100;
        public uint GilFixedAmount { get; set; } = 0;
        public uint GilAlwaysKeep { get; set; } = 0;
        
        public CrystalConfig CrystalConfig { get; set; } = new();


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

    public enum GilDepositMode
    {
        Disabled = 0,
        Percentage = 1,
        FixedAmount = 2
    }

    public class CrystalConfig
    {
        public int GlobalKeepAmount { get; set; } = 0;
        public bool IncludeInDepositAll { get; set; } = true;
        public bool IncludeInWithdrawAll { get; set; } = false;
        public HashSet<uint> EnabledIds { get; set; } = new();
        public Dictionary<uint, int> CustomKeepAmounts { get; set; } = new();
    }
}
