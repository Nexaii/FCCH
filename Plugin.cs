using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;
using FCCH.Managers;
using FCCH.UI;
using FCCH.GameData;
using FCCH.Managers.Gil;
using FCCH.Managers.Organizer;
using System.Linq;

namespace FCCH
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

        private ChestHelper ChestHelper { get; init; }
        private OverlayManager OverlayManager { get; init; }
        private SettingsWindow SettingsWindow { get; init; }
        private WorkshopCache WorkshopCache { get; init; }
        private Dalamud.Interface.Windowing.WindowSystem WindowSystem { get; init; }
        private OpLockManager OpLockManager { get; init; }
        private GilManager GilManager { get; init; }
        private OrgService OrgService { get; init; }
        private Common.WorkshoppaIPC WorkshoppaIpc { get; init; }

        public static Configuration Configuration { get; private set; } = null!;

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            WorkshopCache = new WorkshopCache(Data, PluginLog);
            ChestHelper = new ChestHelper(Configuration);
            WorkshoppaIpc = new Common.WorkshoppaIPC(PluginInterface);
            
            OpLockManager = new OpLockManager();
            GilManager = new GilManager(Configuration, ChestHelper.MoveManager);
            
            WindowSystem = new Dalamud.Interface.Windowing.WindowSystem("FCCH");

            OrgService = new OrgService(ChestHelper.ChestManager, ChestHelper.MoveManager, Configuration, () => ChestHelper.StartIndexing(autoDump: false));
            
            OverlayManager = new OverlayManager(ChestHelper, GameGui, Configuration, WindowSystem, OrgService);

            SettingsWindow = new SettingsWindow(ChestHelper, WorkshopCache, GameGui, Configuration, OrgService, WorkshoppaIpc);
            
            WindowSystem.AddWindow(SettingsWindow);
            
            CommandManager.AddHandler("/fcch", new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens settings.\n— Deposit: da (All) | dd (Dupes) | dc (Crystals)\n— Withdraw: wa (All) | ws (Custom) | wp (Workshop) | wc (Crystals)\n— Gil: gd (Deposit) | gw (Withdraw) — e.g. 5k, 1m, all\n— Info: info"
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
                    ChestHelper.ProcessCommand(() => ChestHelper.DepositAll());
                    break;
                case "dd":
                    ChestHelper.ProcessCommand(() => ChestHelper.DepositDuplicates());
                    break;
                case "wa":
                    ChestHelper.ProcessCommand(() => ChestHelper.WithdrawAll());
                    break;
                case "ws":
                    ChestHelper.ProcessCommand(() =>
                    {
                        if (Configuration.WithdrawItems.Count > 0)
                        {
                            var dict = new System.Collections.Generic.Dictionary<uint, int>();
                            foreach(var item in Configuration.WithdrawItems) dict[item.ItemId] = item.Quantity;
                            ChestHelper.WithdrawMaterials(dict);
                        }
                        else ChatHelper.Info("Custom list is empty.");
                    });
                    break;
                case "dc":
                    ChestHelper.ProcessCommand(() => ChestHelper.CrystalMgr.Deposit(true));
                    break;
                case "wc":
                    ChestHelper.ProcessCommand(() => ChestHelper.CrystalMgr.Withdraw(true));
                    break;
                case "wp":
                    ChestHelper.ProcessCommand(() =>
                    {
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
                    });
                    break;

                case "info":
                    ChestHelper.ProcessCommand(() =>
                    {
                        var rank = ChestHelper.GetFCRank();
                        var tabs = ChestHelper.GetAvailableTabs();
                        
                        var tabString = string.Join(", ", System.Linq.Enumerable.Select(tabs, 
                            t => t.ToString().Replace("FreeCompanyPage", "")));
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
                        
                        if (sb.Length > 3) sb.Length -= 3;
                        
                        ChatHelper.Info(sb.ToString());
                        ChatHelper.Info($"Gil: {GilManager.GetPermissionString()}");
                    });
                    break;
                case "gd":
                    ChestHelper.ProcessCommand(() => GilManager.HandleDepositCommand(parts.Length > 1 ? parts[1] : ""));
                    break;
                case "gw":
                    ChestHelper.ProcessCommand(() => GilManager.HandleWithdrawCommand(parts.Length > 1 ? parts[1] : ""));
                    break;
                case "gildebug":
                    GilManager.EnableDebugMode();
                    break;
                case "debug":
                    Configuration.DebugMode = !Configuration.DebugMode;
                    Configuration.Save();
                    ChatHelper.Info($"Debug Mode: {(Configuration.DebugMode ? "ON" : "OFF")}");
                    break;
                default:
                    ChatHelper.Info("Unknown command. Available: da, dd, wa, ws, wp, gd, gw, gildebug, info, debug");
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
            GilManager?.Dispose();
            OpLockManager?.Dispose();
            OrgService?.Dispose();

            CommandManager.RemoveHandler("/fcch");
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= DrawConfig;
            PluginInterface.UiBuilder.OpenMainUi -= DrawMain;
            Framework.Update -= OnUpdate;

            WindowSystem?.RemoveAllWindows();
            OverlayManager?.Dispose();
            SettingsWindow?.Dispose();
            ChestHelper?.Dispose();
        }
    }
}
