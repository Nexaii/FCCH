using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FC_Chest_Helper.Common;
using FC_Chest_Helper.UI;
using System.Linq;

namespace FC_Chest_Helper
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "FCCH";

        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IClientState ClientState { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static IGameGui GameGui { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] public static IDataManager Data { get; private set; } = null!;
        [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] public static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] public static IChatGui Chat { get; private set; } = null!;
        [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
        [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
        [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

        private FCChestHelper ChestHelper { get; init; }
        private OverlayManager OverlayManager { get; init; }
        private SettingsWindow SettingsWindow { get; init; }
        private FC_Chest_Helper.GameData.WorkshopCache WorkshopCache { get; init; }
        private Dalamud.Interface.Windowing.WindowSystem WindowSystem { get; init; }
        private FC_Chest_Helper.Managers.OpLockManager OpLockManager { get; init; }

        public Configuration Configuration { get; init; }

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            WorkshopCache = new FC_Chest_Helper.GameData.WorkshopCache(Data, PluginLog);
            ChestHelper = new FCChestHelper(Configuration);
            
            OpLockManager = new FC_Chest_Helper.Managers.OpLockManager();
            
            WindowSystem = new Dalamud.Interface.Windowing.WindowSystem("FCCH");
            
            OverlayManager = new OverlayManager(ChestHelper, GameGui, Configuration, WindowSystem);
            
            SettingsWindow = new SettingsWindow(ChestHelper, WorkshopCache, GameGui, Configuration);
            
            WindowSystem.AddWindow(SettingsWindow);
            
            CommandManager.AddHandler("/fcch", new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens settings.\n/fcch da - Deposits All\n/fcch dd - Deposit Duplicates\n/fcch wa - Withdraw All\n/fcch ws - Withdraw Singles\n/fcch wp - Withdraw Workshop\n/fcch info - FC Permissions"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfig;
            PluginInterface.UiBuilder.OpenMainUi += DrawMain;
            
            Framework.Update += OnUpdate;
        }

        private bool _wasSettingsOpen = false;
        private bool _wasChestOpen = false;

        private unsafe void OnUpdate(IFramework framework)
        {
            OverlayManager.Update();
            
            if (_wasSettingsOpen && !SettingsWindow.IsOpen)
            {
                ChestHelper.IsSettingsVisible = false;
            }
            
            if (ChestHelper.IsSettingsVisible && !SettingsWindow.IsOpen)
            {
                SettingsWindow.IsOpen = true;
            }

            _wasSettingsOpen = SettingsWindow.IsOpen;

            var addon = (AtkUnitBase*)GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
            bool isChestOpen = addon != null && addon->IsVisible;

            if (_wasChestOpen && !isChestOpen)
            {
                ChestHelper.IsSettingsVisible = false;
                SettingsWindow.IsOpen = false;
            }
            _wasChestOpen = isChestOpen;
        }

        private void OnCommand(string command, string args)
        {
            ChestHelper.DebugLog($"[Cmd] User executed command: {command} {args}");
            var parts = args.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                ChestHelper.IsSettingsVisible = !ChestHelper.IsSettingsVisible;
                return;
            }

            string subCommand = parts[0].ToLower();
            switch (subCommand)
            {
                case "da":
                    ChestHelper.DepositAll();
                    break;
                case "dd":
                    ChestHelper.DepositDuplicates();
                    break;
                case "wa":
                    ChestHelper.WithdrawAll();
                    break;
                case "ws":
                    if (Configuration.WithdrawItems.Count > 0)
                    {
                        var dict = new System.Collections.Generic.Dictionary<uint, int>();
                        foreach(var item in Configuration.WithdrawItems) dict[item.ItemId] = item.Quantity;
                        ChestHelper.WithdrawMaterials(dict);
                    }
                    else ChatHelper.Info("Singles list is empty.");
                    break;
                case "wp":
                    if (ChestHelper.ShoppingList.Count > 0)
                    {
                        var list = new System.Collections.Generic.Dictionary<uint, int>();
                        foreach (var shopItem in ChestHelper.ShoppingList)
                        {
                            var mats = shopItem.Craft.Phases
                                .SelectMany(p => p.Items)
                                .Select(x => new { Item = x, Required = x.TotalQuantity * shopItem.Quantity });
                            foreach (var mat in mats)
                            {
                                if (!list.ContainsKey(mat.Item.ItemId)) list[mat.Item.ItemId] = 0;
                                list[mat.Item.ItemId] += mat.Required;
                            }
                        }
                        ChestHelper.WithdrawMaterials(list);
                    }
                    else ChatHelper.Info("Workshop list is empty.");
                    break;

                case "info":
                    var rank = ChestHelper.GetFCRank();
                    var tabs = ChestHelper.GetAvailableTabs();
                    
                    var tabString = string.Join(", ", System.Linq.Enumerable.Select(tabs, t => t.ToString().Replace("FreeCompanyPage", "")));
                    ChatHelper.Info($"FC Rank: {rank}. Available Tabs: {tabString}");

                    var sb = new System.Text.StringBuilder();
                    sb.Append($"Permissions: ");
                    
                    foreach (var tab in tabs)
                    {
                        var access = ChestHelper.GetChestAccess(tab);
                        string tabName = tab.ToString().Replace("FreeCompanyPage", "");

                        
                        string accessStr = ((byte)access) switch
                        {
                            0 => "Full Access",
                            1 => "Deposit/View",
                            2 => "Deposit Only",
                            3 => "View Only",
                            4 => "No Access",
                            _ => $"Unknown ({access})"
                        };

                        sb.Append($"{tabName}: {accessStr} | ");
                    }
                    
                    // Remove trailing separator
                    if (sb.Length > 3) sb.Length -= 3;
                    
                    ChatHelper.Info(sb.ToString());
                    break;
                case "debug":
                    DebugEnums.PrintValues();
                    break;
                default:
                    ChatHelper.Info("Unknown command. Available: da, dd, wa, ws, wp, info, debug");
                    break;
            }
        }

        private void DrawUI()
        {
            WindowSystem.Draw();
        }

        private void DrawConfig()
        {
            ChestHelper.IsSettingsVisible = true;
        }

        private void DrawMain()
        {
            ChestHelper.IsSettingsVisible = true;
        }

        public void Dispose()
        {
            OpLockManager?.Dispose();
            
            CommandManager.RemoveHandler("/fcch");
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= DrawConfig;
            PluginInterface.UiBuilder.OpenMainUi -= DrawMain;
            Framework.Update -= OnUpdate;
            
            WindowSystem.RemoveAllWindows();
            OverlayManager.Dispose();
            SettingsWindow.Dispose();
            ChestHelper.Dispose();
        }
    }
}
