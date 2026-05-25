using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;

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
        public bool CompactItemNames { get; set; } = true;
        public bool EnableItemContextMenuEntries { get; set; } = false;
        public bool ListsOnRightSide { get; set; } = false;
        public bool IsWindowLocked { get; set; } = true;
        public float SettingsPosX { get; set; } = -1f;
        public float SettingsPosY { get; set; } = -1f;

        public bool ToolbarLocked { get; set; } = true;
        public float ToolbarPosX { get; set; } = -1f;
        public float ToolbarPosY { get; set; } = -1f;
        public bool ToolbarSnapToGrid { get; set; } = false;
        public List<ToolbarButtonConfig> ToolbarButtons { get; set; } = CreateDefaultToolbarButtons();

        public string DebugLogPath { get; set; } = "";

        public bool LowerQualityOnDeposit { get; set; } = false;
        public bool PlayCompletionSound { get; set; } = false;
        public string CustomSoundPath { get; set; } = "";

        public int MoveDelayInMs { get; set; } = 700;
        public int IndexingDelayInMs { get; set; } = 100;
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

        public bool EnsureToolbarButtons()
        {
            var defaults = CreateDefaultToolbarButtons();
            var validIds = new HashSet<ToolbarButtonId>(defaults.Select(x => x.Id));
            var seen = new HashSet<ToolbarButtonId>();
            var normalized = new List<ToolbarButtonConfig>();
            ToolbarButtons ??= new List<ToolbarButtonConfig>();

            foreach (var button in ToolbarButtons)
            {
                if (!validIds.Contains(button.Id)) continue;
                if (!seen.Add(button.Id)) continue;
                normalized.Add(button);
            }

            foreach (var button in defaults)
            {
                if (seen.Add(button.Id))
                    normalized.Add(button);
            }

            var changed = normalized.Count != ToolbarButtons.Count
                || normalized.Where((button, index) => button.Id != ToolbarButtons[index].Id || button.IsVisible != ToolbarButtons[index].IsVisible).Any();

            if (changed)
                ToolbarButtons = normalized;

            return changed;
        }

        public void ResetToolbarButtons()
        {
            ToolbarButtons = CreateDefaultToolbarButtons();
        }

        public static List<ToolbarButtonConfig> CreateDefaultToolbarButtons()
        {
            return new List<ToolbarButtonConfig>
            {
                new() { Id = ToolbarButtonId.Settings, IsVisible = true },
                new() { Id = ToolbarButtonId.Deposit, IsVisible = true },
                new() { Id = ToolbarButtonId.DepositCustom, IsVisible = true },
                new() { Id = ToolbarButtonId.DepositDuplicates, IsVisible = true },
                new() { Id = ToolbarButtonId.Crystals, IsVisible = true },
                new() { Id = ToolbarButtonId.Withdraw, IsVisible = true },
                new() { Id = ToolbarButtonId.WithdrawCustom, IsVisible = true },
                new() { Id = ToolbarButtonId.WithdrawWorkshop, IsVisible = true },
            };
        }
    }

    public class ToolbarButtonConfig
    {
        public ToolbarButtonId Id { get; set; }
        public bool IsVisible { get; set; } = true;
    }

    public enum ToolbarButtonId
    {
        Settings = 0,
        Deposit = 1,
        DepositCustom = 2,
        DepositDuplicates = 3,
        Crystals = 4,
        Withdraw = 5,
        WithdrawCustom = 6,
        WithdrawWorkshop = 7
    }

    public class WithdrawItem
    {
        public uint ItemId { get; set; }
        public int Quantity { get; set; } = 1;
        public CustomItemMode Mode { get; set; } = CustomItemMode.Withdraw;
        public bool AlwaysMax { get; set; } = false;

        public bool CanDeposit => Mode == CustomItemMode.Deposit || Mode == CustomItemMode.Both;
        public bool CanWithdraw => Mode == CustomItemMode.Withdraw || Mode == CustomItemMode.Both;

        public void CycleMode()
        {
            Mode = Mode switch
            {
                CustomItemMode.Withdraw => CustomItemMode.Deposit,
                CustomItemMode.Deposit => CustomItemMode.Both,
                _ => CustomItemMode.Withdraw
            };
        }

        public WithdrawItem Clone()
        {
            return new WithdrawItem
            {
                ItemId = ItemId,
                Quantity = Quantity,
                Mode = Mode,
                AlwaysMax = AlwaysMax
            };
        }
    }

    public enum CustomItemMode
    {
        Withdraw = 0,
        Deposit = 1,
        Both = 2
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
