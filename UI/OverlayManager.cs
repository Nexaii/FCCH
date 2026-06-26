using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using FCCH.Common;
using FCCH.Models;
using FCCH.Managers;
using FCCH.Managers.Organizer;

namespace FCCH.UI
{

    public class OverlayManager : IDisposable
    {
        private readonly ChestHelper _helper;
        private readonly IGameGui _gameGui;
        private readonly Configuration _configuration;
        private readonly OrgService _orgService;

        private readonly ToolbarWindow _toolbarWindow;
        private readonly StopWindow _stopWindow;

        private const float MinVisible = 60f;

        public OverlayManager(ChestHelper helper, IGameGui gameGui, Configuration configuration, WindowSystem windowSystem, OrgService orgService)
        {
            _helper = helper;
            _gameGui = gameGui;
            _configuration = configuration;
            _orgService = orgService;

            _toolbarWindow = new ToolbarWindow(helper, configuration, orgService);
            _stopWindow = new StopWindow(helper, orgService);

            windowSystem.AddWindow(_toolbarWindow);
            windowSystem.AddWindow(_stopWindow);
        }

        public unsafe void Update()
        {
            var addon = Common.ChestAddon.GetOpen();
            if (addon == null)
            {
                _toolbarWindow.IsOpen = false;
                _stopWindow.IsOpen = false;
                return;
            }

            bool isOperationActive = _helper.IsUserOperationActive || _orgService.JobStatus == OrgJobStatus.Running || _orgService.IsSortRunning || _orgService.IsMergeRunning;

            _toolbarWindow.UpdateMoveFlag();

            if (isOperationActive)
            {
                _toolbarWindow.IsOpen = false;

                _stopWindow.IsOpen = true;
                ApplyOverlayPosition(_stopWindow, addon, _stopWindow.LastHeight);
            }
            else
            {
                _stopWindow.IsOpen = false;

                _toolbarWindow.IsOpen = true;
                ApplyOverlayPosition(_toolbarWindow, addon, _toolbarWindow.LastHeight);
            }
        }

        private unsafe void ApplyOverlayPosition(Window window, AtkUnitBase* addon, float lastHeight)
        {
            var attached = ComputeAttachedPosition(addon, lastHeight);

            bool useAttached = _configuration.ToolbarPosX < 0 && _configuration.ToolbarPosY < 0;

            if (useAttached)
            {
                window.Position = attached;
                window.PositionCondition = ImGuiCond.Always;
                return;
            }

            var vp = ImGui.GetMainViewport();
            if (_configuration.ToolbarPosX < 0 || _configuration.ToolbarPosX > vp.Size.X - MinVisible
                || _configuration.ToolbarPosY < 0 || _configuration.ToolbarPosY > vp.Size.Y - MinVisible)
            {
                _configuration.ToolbarPosX = attached.X;
                _configuration.ToolbarPosY = attached.Y;
                _configuration.Save();
                window.Position = attached;
                window.PositionCondition = ImGuiCond.Always;
                return;
            }

            window.Position = new Vector2(_configuration.ToolbarPosX, _configuration.ToolbarPosY);
            window.PositionCondition = ImGuiCond.Appearing;
        }

        private static unsafe Vector2 ComputeAttachedPosition(AtkUnitBase* addon, float lastHeight)
        {
            float x = addon->X + 5;
            float y = addon->Y - lastHeight;

            if (y < 0)
            {
                float addonHeight = addon->RootNode != null ? addon->RootNode->Height * addon->Scale : 0f;
                y = addon->Y + addonHeight + 4f;
            }

            return new Vector2(x, y);
        }

        public void Dispose() { }
    }

    public class ToolbarWindow : Window, IDisposable
    {
        private readonly ChestHelper _helper;
        private readonly Configuration _configuration;
        private readonly OrgService _orgService;

        private bool _showDepositConfirm = false;
        private bool _showWithdrawConfirm = false;
        private bool _dontShowAgain = false;

        private Action? _pendingConfirmAction;
        private string _confirmMessage = "";
        private int _lastCrystalAction = 0;

        private bool _depositMenuOpen = false;
        private bool _withdrawMenuOpen = false;
        private bool _sortMenuOpen = false;
        private Vector2 _depositMenuAnchor;
        private Vector2 _withdrawMenuAnchor;
        private Vector2 _sortMenuAnchor;
        private InventoryType _sortMenuPage = InventoryType.Invalid;

        private const string DepositPopupId = "##DepositPopup";
        private const string WithdrawPopupId = "##WithdrawPopup";
        private const string SortPopupId = "##SortPopup";

        private static readonly Vector2 SubmenuItemSize = new(160, 22);
        private const float SubmenuItemSpacingY = 6f;
        private const float SubmenuPadding = 8f;
        private const float SubmenuAlpha = 0.85f;

        private const float SubmenuGap = 8f;

        public float LastHeight { get; private set; }

        public ToolbarWindow(ChestHelper helper, Configuration configuration, OrgService orgService) : base("FCCH Toolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
        {
            _helper = helper;
            _configuration = configuration;
            _orgService = orgService;
            RespectCloseHotkey = false;
            UpdateMoveFlag();
        }

        public void UpdateMoveFlag()
        {
            if (_configuration.ToolbarLocked)
                Flags |= ImGuiWindowFlags.NoMove;
            else
                Flags &= ~ImGuiWindowFlags.NoMove;
        }

        public override void Draw()
        {
            PersistDriftIfUnlocked();
            var gate = _helper.CanStartUserAction();
            _configuration.EnsureToolbarButtons();

            var first = true;
            foreach (var button in _configuration.ToolbarButtons)
            {
                if (!button.IsVisible) continue;
                if (!first) ImGui.SameLine();
                DrawToolbarButton(button.Id, gate);
                first = false;
            }

            DrawConfirmationModal("Confirm Deposit", _confirmMessage, ref _showDepositConfirm, true);
            DrawConfirmationModal("Confirm Withdraw", _confirmMessage, ref _showWithdrawConfirm, false);

            LastHeight = ImGui.GetWindowSize().Y;
        }

        private void DrawToolbarButton(ToolbarButtonId id, ActionGateResult gate)
        {
            var gated = id != ToolbarButtonId.Settings;
            if (gated && !gate.CanRun) ImGui.BeginDisabled();

            switch (id)
            {
                case ToolbarButtonId.Settings:
                    DrawIconButton(FontAwesomeIcon.Cog, "Settings", "Settings",
                        onClick: () => _helper.IsSettingsVisible = !_helper.IsSettingsVisible);
                    break;
                case ToolbarButtonId.Deposit:
                    DrawSplitButtonWithDropdown(
                        FontAwesomeIcon.ArrowDown, "DepMenu",
                        ActionTooltip("Deposit\nClick for tab options", gate),
                        ref _depositMenuOpen, ref _depositMenuAnchor, DepositPopupId,
                        () => DrawDepositMenuItems(gate));
                    break;
                case ToolbarButtonId.DepositCustom:
                    DrawIconButton(FontAwesomeIcon.FileAlt, "DepCustom", ActionTooltip("Deposit Custom List", gate),
                        onClick: () => RequestDeposit("Deposit Custom list?", () => _helper.DepositCustomItems()));
                    break;
                case ToolbarButtonId.DepositDuplicates:
                    DrawIconButton(FontAwesomeIcon.Clone, "Dupes", ActionTooltip("Deposit Duplicates", gate),
                        onClick: () => RequestDeposit("Deposit all duplicates?", () => _helper.DepositDuplicates()));
                    break;
                case ToolbarButtonId.Crystals:
                    DrawCrystalButton(gate);
                    break;
                case ToolbarButtonId.Withdraw:
                    DrawSplitButtonWithDropdown(
                        FontAwesomeIcon.ArrowUp, "WithMenu",
                        ActionTooltip("Withdraw\nClick for tab options", gate),
                        ref _withdrawMenuOpen, ref _withdrawMenuAnchor, WithdrawPopupId,
                        () => DrawWithdrawMenuItems(gate));
                    break;
                case ToolbarButtonId.WithdrawCustom:
                    DrawIconButton(FontAwesomeIcon.FileAlt, "Custom", ActionTooltip("Withdraw Custom List", gate),
                        onClick: () => RequestWithdraw("Withdraw Custom list?", () => _helper.WithdrawCustomItems()));
                    break;
                case ToolbarButtonId.WithdrawWorkshop:
                    DrawIconButton(FontAwesomeIcon.ListUl, "Workshop", ActionTooltip("Withdraw Workshop List", gate),
                        onClick: () => RequestWithdraw("Withdraw Workshop List?", () => _helper.WithdrawWorkshopItems()));
                    break;
                case ToolbarButtonId.Sort:
                    DrawSplitButtonWithDropdown(
                        FontAwesomeIcon.Sort, "SortMenu",
                        ActionTooltip(SortTooltip(ResolveCurrentItemPage()), gate),
                        ref _sortMenuOpen, ref _sortMenuAnchor, SortPopupId,
                        () => DrawSortMenuItems(gate),
                        onOpen: () => _sortMenuPage = ResolveCurrentItemPage());
                    break;
            }

            if (gated && !gate.CanRun) ImGui.EndDisabled();
        }

        private void PersistDriftIfUnlocked()
        {
            if (_configuration.ToolbarLocked) return;

            var pos = ImGui.GetWindowPos();
            if (Math.Abs(pos.X - _configuration.ToolbarPosX) > 1f
                || Math.Abs(pos.Y - _configuration.ToolbarPosY) > 1f)
            {
                if (_configuration.ToolbarSnapToGrid)
                {
                    pos.X = MathF.Round(pos.X / 10f) * 10f;
                    pos.Y = MathF.Round(pos.Y / 10f) * 10f;
                }
                _configuration.ToolbarPosX = pos.X;
                _configuration.ToolbarPosY = pos.Y;
                _configuration.Save();
            }
        }

        private bool DrawIconButton(FontAwesomeIcon icon, string id, string? tooltip = null, Vector4? iconColor = null, Action? onClick = null)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (iconColor.HasValue) ImGui.PushStyleColor(ImGuiCol.Text, iconColor.Value);

            bool clicked = ImGui.Button(icon.ToIconString() + "##" + id);

            if (iconColor.HasValue) ImGui.PopStyleColor();
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);

            if (clicked && onClick != null) onClick();
            return clicked;
        }

        private void DrawCrystalButton(ActionGateResult gate)
        {
            var crystalColor = _lastCrystalAction switch
            {
                1 => new Vector4(0f, 1f, 1f, 1f),
                2 => new Vector4(1f, 0.64f, 0f, 1f),
                _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)
            };

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            ImGui.PushStyleColor(ImGuiCol.Text, crystalColor);

            if (ImGui.Button(FontAwesomeIcon.Gem.ToIconString() + "##Crystal"))
            {
                _lastCrystalAction = 1;
                _helper.CrystalMgr.Deposit(true);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _lastCrystalAction = 2;
                _helper.CrystalMgr.Withdraw(true);
            }

            ImGui.PopStyleColor(2);
            ImGui.PopFont();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(ActionTooltip("Crystals\nLeft Click: Deposit\nRight Click: Withdraw", gate));
        }

        private void DrawSplitButtonWithDropdown(
            FontAwesomeIcon icon,
            string id,
            string tooltip,
            ref bool openFlag,
            ref Vector2 anchor,
            string popupId,
            Action drawItems,
            Action? onOpen = null)
        {
            var cursorScreen = ImGui.GetCursorScreenPos();
            var pendingAnchor = new Vector2(cursorScreen.X, cursorScreen.Y + ImGui.GetFrameHeight() + SubmenuGap);

            bool clicked = DrawIconButton(icon, id, tooltip);

            if (clicked)
            {
                anchor = pendingAnchor;
                openFlag = true;
                onOpen?.Invoke();
                ImGui.OpenPopup(popupId);
            }

            DrawDropdownSubmenu(popupId, anchor, ref openFlag, drawItems);
        }

        private void DrawDropdownSubmenu(string popupId, Vector2 anchor, ref bool openFlag, Action drawItems)
        {
            if (openFlag)
            {
                ImGui.SetNextWindowPos(anchor);
            }

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(SubmenuPadding, SubmenuPadding));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5f, SubmenuItemSpacingY));
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, SubmenuAlpha);

            if (ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize))
            {
                drawItems();
                ImGui.EndPopup();
            }

            ImGui.PopStyleVar(3);

            if (!ImGui.IsPopupOpen(popupId)) openFlag = false;
        }

        private void DrawDepositMenuItems(ActionGateResult gate)
        {
            if (!gate.CanRun) ImGui.BeginDisabled();

            if (ImGui.Selectable("Deposit All", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                RequestDeposit("Deposit ALL allowed items?", () => _helper.DepositAll());

            ImGui.Separator();

            for (int t = 1; t <= 5; t++)
            {
                int tab = t;
                if (ImGui.Selectable($"Deposit Tab {tab}", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                    RequestDeposit($"Deposit eligible items to Tab {tab}?", () => _helper.DepositToTab(tab));
            }

            if (!gate.CanRun) ImGui.EndDisabled();
        }

        private void DrawWithdrawMenuItems(ActionGateResult gate)
        {
            if (!gate.CanRun) ImGui.BeginDisabled();

            if (ImGui.Selectable("Withdraw All", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                RequestWithdraw("Withdraw ALL items?", () => _helper.WithdrawAll());

            ImGui.Separator();

            for (int t = 1; t <= 5; t++)
            {
                int tab = t;
                if (ImGui.Selectable($"Withdraw Tab {tab}", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                    RequestWithdraw($"Withdraw all items from Tab {tab}?", () => _helper.WithdrawFromTab(tab));
            }

            if (!gate.CanRun) ImGui.EndDisabled();
        }

        private unsafe InventoryType ResolveCurrentItemPage()
        {
            var addon = Common.ChestAddon.GetOpen();
            if (addon == null) return InventoryType.Invalid;

            var page = _helper.ChestManager.GetCurrentFCPage(addon);
            return OrgService.IsItemPage(page) ? page : InventoryType.Invalid;
        }

        private static string SortTooltip(InventoryType page)
        {
            if (!OrgService.IsItemPage(page))
                return "Open an item tab to sort or merge. Use the Organizer to move across tabs.";

            int n = (int)(page - InventoryType.FreeCompanyPage1) + 1;
            return $"Sort or merge Tab {n}. Use the Organizer to move across tabs.";
        }

        private void DrawSortMenuItems(ActionGateResult gate)
        {
            bool pageOk = OrgService.IsItemPage(_sortMenuPage);
            if (!gate.CanRun || !pageOk) ImGui.BeginDisabled();

            DrawSortOption("Sort by Category", OrgSortOrder.ByCategory);
            DrawSortOption("Sort by ID", OrgSortOrder.ById);
            DrawSortOption("Sort by Name", OrgSortOrder.ByName);
            DrawSortOption("Sort by Quantity", OrgSortOrder.ByQuantity);

            ImGui.Separator();

            if (ImGui.Selectable("Merge Stacks", false, ImGuiSelectableFlags.None, SubmenuItemSize))
            {
                var page = _sortMenuPage;
                _helper.TryStartUserAction(() => _orgService.RunMerge(page));
            }

            if (!gate.CanRun || !pageOk) ImGui.EndDisabled();
        }

        private void DrawSortOption(string label, OrgSortOrder order)
        {
            if (!ImGui.Selectable(label, false, ImGuiSelectableFlags.None, SubmenuItemSize)) return;

            var page = _sortMenuPage;
            _helper.TryStartUserAction(() => _orgService.RunSort(page, order, false, new() { OrgFilterCategory.AllItems }));
        }

        private void RequestDeposit(string message, Action action)
        {
            var gate = _helper.CanStartUserAction();
            if (!gate.CanRun)
            {
                ChatHelper.Warning(gate.Reason);
                return;
            }

            if (_configuration.DisableAskDepositAll)
            {
                _helper.TryStartUserAction(action);
                return;
            }
            _confirmMessage = message;
            _pendingConfirmAction = action;
            _showDepositConfirm = true;
            _dontShowAgain = false;
            ImGui.OpenPopup("Confirm Deposit");
        }

        private void RequestWithdraw(string message, Action action)
        {
            var gate = _helper.CanStartUserAction();
            if (!gate.CanRun)
            {
                ChatHelper.Warning(gate.Reason);
                return;
            }

            if (_configuration.DisableAskWithdrawAll)
            {
                _helper.TryStartUserAction(action);
                return;
            }
            _confirmMessage = message;
            _pendingConfirmAction = action;
            _showWithdrawConfirm = true;
            _dontShowAgain = false;
            ImGui.OpenPopup("Confirm Withdraw");
        }

        private void DrawConfirmationModal(string title, string message, ref bool showFlag, bool isDeposit)
        {
            if (ImGui.BeginPopupModal(title, ref showFlag, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text(message);

                ImGui.Separator();
                ImGui.Checkbox("Don't show this again", ref _dontShowAgain);
                if (ImGui.Button("Yes", new Vector2(120, 0)))
                {
                    if (_dontShowAgain)
                    {
                        if (isDeposit) _configuration.DisableAskDepositAll = true;
                        else _configuration.DisableAskWithdrawAll = true;
                        _configuration.Save();
                    }
                    if (_pendingConfirmAction != null)
                        _helper.TryStartUserAction(_pendingConfirmAction);
                    showFlag = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(120, 0))) { showFlag = false; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
        }
        public void Dispose() { }

        private static string ActionTooltip(string readyTooltip, ActionGateResult gate)
            => gate.CanRun ? readyTooltip : gate.Reason;
    }

    public class StopWindow : Window, IDisposable
    {
        private readonly ChestHelper _helper;
        private readonly OrgService _orgService;

        public float LastHeight { get; private set; }

        public StopWindow(ChestHelper helper, OrgService orgService) : base("StopWindow", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove)
        {
            _helper = helper;
            _orgService = orgService;
            RespectCloseHotkey = false;
        }

        public override void Draw()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.HealerGreen, "Processing...");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudRed);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudRed);
            if (ImGui.Button("Stop"))
            {
                _orgService.CancelSort();
                _orgService.CancelMerge();
                _helper.Stop();
            }
            ImGui.PopStyleColor(2);

            ImGui.PopStyleVar(2);

            LastHeight = ImGui.GetWindowSize().Y;
        }
        public void Dispose() { }
    }
}
