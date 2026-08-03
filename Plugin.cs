using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game;
using FCCH.Common;
using FCCH.IPC;
using FCCH.Managers;
using FCCH.UI;
using FCCH.GameData;
using FCCH.Managers.Gil;
using FCCH.Managers.Organizer;

namespace FCCH
{
    public sealed class Plugin : IDalamudPlugin
    {
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
        [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
        [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
        [PluginService] public static IKeyState KeyState { get; private set; } = null!;
        [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] public static IGamepadState GamepadState { get; private set; } = null!;

        private ChestHelper ChestHelper { get; init; }
        private OverlayManager OverlayManager { get; init; }
        private SearchBarManager SearchBarManager { get; init; }
        private SettingsWindow SettingsWindow { get; init; }
        private WorkshopCache WorkshopCache { get; init; }
        private Dalamud.Interface.Windowing.WindowSystem WindowSystem { get; init; }
        private OpLockManager OpLockManager { get; init; }
        private GilManager GilManager { get; init; }
        private OrgService OrgService { get; init; }
        private ContextMenuManager ItemContextMenu { get; init; }
        private WorkshoppaIPC WorkshoppaIPC { get; init; }
        private IPCProvider IPC { get; init; }
        private WhatsNewWindow WhatsNewWindow { get; init; }

        public static Configuration Configuration { get; private set; } = null!;
        private const string CommandHelpMessage = "Opens settings.\n- Deposit: da (All) | da1-da5 (Tabs) | ds (Custom) | dd (Dupes) | dc (Crystals)\n- Withdraw: wa (All) | wa1-wa5 (Tabs) | ws (Custom) | wp (Workshop) | wc (Crystals)\n- Gil: gd (Deposit) | gw (Withdraw) - e.g. 5k, 1m, all";

        public Plugin()
        {
            var savedConfig = PluginInterface.GetPluginConfig() as Configuration;
            bool isFreshInstall = savedConfig == null;
            Configuration = savedConfig ?? new Configuration();
            Configuration.Initialize(PluginInterface);
#if !DEBUG
            if (Configuration.DebugMode || Configuration.VerboseMode)
            {
                Configuration.DebugMode = false;
                Configuration.VerboseMode = false;
                Configuration.Save();
            }
#endif

            WorkshopCache = new WorkshopCache(Data, PluginLog);
            ChestHelper = new ChestHelper(Configuration);
            WorkshoppaIPC = new WorkshoppaIPC(PluginInterface);

            OpLockManager = new OpLockManager(Configuration);
            GilManager = new GilManager(Configuration, ChestHelper.ChestManager, ChestHelper.MoveManager);
            IPC = new IPCProvider(PluginInterface, ChestHelper, GilManager);
            
            WindowSystem = new Dalamud.Interface.Windowing.WindowSystem("FCCH");

            OrgService = new OrgService(ChestHelper.ChestManager, ChestHelper.MoveManager, Configuration,
                tabs => ChestHelper.RefreshTabs(tabs),
                tab => ChestHelper.SwitchToTab(tab));
            ChestHelper.ExternalOperationActive = () => OrgService.JobStatus == OrgJobStatus.Running;
            ChestHelper.CompanyChestClosedDuringOperation += OnCompanyChestClosedDuringOperation;
            ItemContextMenu = new ContextMenuManager(ContextMenu, Configuration, ChestHelper, KeyState);
            
            OverlayManager = new OverlayManager(ChestHelper, GameGui, Configuration, WindowSystem, OrgService);
            SearchBarManager = new SearchBarManager(ChestHelper, GameGui, KeyState, Configuration, WindowSystem);

            SettingsWindow = new SettingsWindow(ChestHelper, WorkshopCache, GameGui, Configuration, OrgService, WorkshoppaIPC);
            WhatsNewWindow = new WhatsNewWindow();
            WhatsNewWindow.OpenSettings = () => ChestHelper.IsSettingsVisible = true;

            WindowSystem.AddWindow(SettingsWindow);
            WindowSystem.AddWindow(WhatsNewWindow);
            
            CommandManager.AddHandler("/fcch", new CommandInfo(OnCommand)
            {
                HelpMessage = CommandHelpMessage
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfig;
            PluginInterface.UiBuilder.OpenMainUi += DrawMain;
            
            Framework.Update += OnUpdate;

            ShowWhatsNewIfUnseen(isFreshInstall);
        }

        private void ShowWhatsNewIfUnseen(bool isFreshInstall)
        {
            if (isFreshInstall)
            {
                Configuration.LastSeenWhatsNewRevision = Common.WhatsNew.Revision;
                Configuration.Save();
                return;
            }

            if (Common.WhatsNew.Revision <= Configuration.LastSeenWhatsNewRevision) return;

            Configuration.LastSeenWhatsNewRevision = Common.WhatsNew.Revision;
            Configuration.Save();
            WhatsNewWindow.IsOpen = true;
        }

        private bool _wasSettingsOpen = false;
        private bool _wasChestOpen = false;

        private void OnCompanyChestClosedDuringOperation()
        {
            OrgService.AbortForClosedChest();
            GilManager.CancelPendingTransaction();
        }

        private unsafe void OnUpdate(IFramework framework)
        {
            Common.DebugFileLogger.Tick();
            OrgService.UpdateSortWatch();
            OrgService.UpdateMergeWatch();
            OverlayManager.Update();
            SearchBarManager.Update();

            if (_wasSettingsOpen && !SettingsWindow.IsOpen)
            {
                ChestHelper.IsSettingsVisible = false;
            }
            
            if (ChestHelper.IsSettingsVisible && !SettingsWindow.IsOpen)
            {
                SettingsWindow.IsOpen = true;
            }

            _wasSettingsOpen = SettingsWindow.IsOpen;

            var addon = Common.ChestAddon.GetOpen();
            bool isChestOpen = addon != null;

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
                case "da1": case "da2": case "da3": case "da4": case "da5":
                {
                    int tab = int.Parse(subCommand.Substring(2));
                    ChestHelper.ProcessCommand(() => ChestHelper.DepositToTab(tab));
                    break;
                }
                case "wa1": case "wa2": case "wa3": case "wa4": case "wa5":
                {
                    int tab = int.Parse(subCommand.Substring(2));
                    ChestHelper.ProcessCommand(() => ChestHelper.WithdrawFromTab(tab));
                    break;
                }
                case "dd":
                    ChestHelper.ProcessCommand(() => ChestHelper.DepositDuplicates());
                    break;
                case "ds":
                    ChestHelper.ProcessCommand(() => ChestHelper.DepositCustomItems());
                    break;
                case "wa":
                    ChestHelper.ProcessCommand(() => ChestHelper.WithdrawAll());
                    break;
                case "ws":
                    ChestHelper.ProcessCommand(() => ChestHelper.WithdrawCustomItems());
                    break;
                case "dc":
                    ChestHelper.ProcessCommand(() => ChestHelper.CrystalMgr.Deposit(true));
                    break;
                case "wc":
                    ChestHelper.ProcessCommand(() => ChestHelper.CrystalMgr.Withdraw(true));
                    break;
                case "wp":
                    ChestHelper.ProcessCommand(() => ChestHelper.WithdrawWorkshopItems());
                    break;
                case "help":
                    ChatHelper.Reply(CommandHelpMessage);
                    break;

#if DEBUG
                case "info":
                    ChestHelper.ProcessCommand(() =>
                    {
                        var rank = ChestHelper.GetFCRank();
                        var tabs = ChestHelper.GetAvailableTabs();
                        
                        var tabString = string.Join(", ", System.Linq.Enumerable.Select(tabs, 
                            t => t.ToString().Replace("FreeCompanyPage", "")));
                        ChatHelper.Reply($"FC Rank: {rank}. Available Tabs: {tabString}");

                        var sb = new System.Text.StringBuilder();
                        sb.Append($"Permissions: ");
                        
                        foreach (var tab in tabs)
                        {
                            var access = ChestHelper.GetChestAccess(tab);
                            string tabName = tab.ToString().Replace("FreeCompanyPage", "");
                            sb.Append($"{tabName}: {ChestManager.NameAccess(access)} | ");
                        }
                        
                        if (sb.Length > 3) sb.Length -= 3;
                        
                        ChatHelper.Reply(sb.ToString());
                        ChatHelper.Reply($"Gil: {GilManager.GetPermissionString()}");
                    });
                    break;
#endif
                case "gd":
                    ChestHelper.ProcessCommand(() => GilManager.HandleDepositCommand(parts.Length > 1 ? parts[1] : ""));
                    break;
                case "gw":
                    ChestHelper.ProcessCommand(() => GilManager.HandleWithdrawCommand(parts.Length > 1 ? parts[1] : ""));
                    break;
#if DEBUG
                case "gildebug":
                    GilManager.EnableDebugMode();
                    break;
                case "fcperms":
                    ChestHelper.ProcessCommand(() =>
                    {
                        byte? overrideRank = null;
                        if (parts.Length > 1 && byte.TryParse(parts[1], out var r)) overrideRank = r;
                        ChestHelper.DumpRawPermissions(overrideRank);
                        ChatHelper.Reply("FC permission dump written to log (/xllog).");
                    });
                    break;
                case "accessprobe":
                case "aprobe":
                    ChatHelper.Reply(ChestHelper.DumpAccessProbe());
                    break;
                case "debug":
                    Configuration.DebugMode = !Configuration.DebugMode;
                    Configuration.Save();
                    ChatHelper.Reply($"Debug Mode: {(Configuration.DebugMode ? "ON" : "OFF")}");
                    break;
                case "whatsnew":
                    WhatsNewWindow.IsOpen = true;
                    break;
#endif
                default:
                    ChatHelper.Reply($"Unknown FCCH command: {subCommand}. Use /fcch help.");
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
            CommandManager.RemoveHandler("/fcch");
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= DrawConfig;
            PluginInterface.UiBuilder.OpenMainUi -= DrawMain;
            Framework.Update -= OnUpdate;

            try { ChestHelper?.Stop(); } catch (System.Exception e) { try { PluginLog.Error(e, "[FCCH] ChestHelper.Stop during dispose threw."); } catch { } }

            if (ChestHelper != null)
                ChestHelper.CompanyChestClosedDuringOperation -= OnCompanyChestClosedDuringOperation;

            OpLockManager?.Dispose();
            IPC?.Dispose();

            ItemContextMenu?.Dispose();
            OverlayManager?.Dispose();
            SearchBarManager?.Dispose();
            SettingsWindow?.Dispose();
            OrgService?.Dispose();
            GilManager?.Dispose();
            ChestHelper?.Dispose();

            WindowSystem?.RemoveAllWindows();
            Common.DebugFileLogger.DrainAndShutdown();
        }
    }
}
