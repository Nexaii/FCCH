using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
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

            _toolbarWindow = new ToolbarWindow(helper, configuration);
            _stopWindow = new StopWindow(helper, orgService);

            windowSystem.AddWindow(_toolbarWindow);
            windowSystem.AddWindow(_stopWindow);
        }

        public unsafe void Update()
        {
            var addon = _gameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
            if (addon == null || !addon->IsVisible)
            {
                _toolbarWindow.IsOpen = false;
                _stopWindow.IsOpen = false;
                return;
            }

            bool isOperationActive = _helper.IsUserOperationActive || _orgService.JobStatus == OrgJobStatus.Running;

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

        private bool _showDepositConfirm = false;
        private bool _showWithdrawConfirm = false;
        private bool _dontShowAgain = false;

        private Action? _pendingConfirmAction;
        private string _confirmMessage = "";
        private int _lastCrystalAction = 0;

        private bool _depositMenuOpen = false;
        private bool _withdrawMenuOpen = false;
        private Vector2 _depositMenuAnchor;
        private Vector2 _withdrawMenuAnchor;

        private const string DepositPopupId = "##DepositPopup";
        private const string WithdrawPopupId = "##WithdrawPopup";

        private static readonly Vector2 SubmenuItemSize = new(160, 22);
        private const float SubmenuItemSpacingY = 6f;
        private const float SubmenuPadding = 8f;
        private const float SubmenuAlpha = 0.85f;

        private const float SubmenuGap = 8f;

        public float LastHeight { get; private set; }

        public ToolbarWindow(ChestHelper helper, Configuration configuration) : base("FCCH Toolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
        {
            _helper = helper;
            _configuration = configuration;
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

            DrawIconButton(FontAwesomeIcon.Cog, "Settings", "Settings",
                onClick: () => _helper.IsSettingsVisible = !_helper.IsSettingsVisible);

            ImGui.SameLine();
            DrawSplitButtonWithDropdown(
                FontAwesomeIcon.ArrowDown, "DepMenu",
                "Deposit\nClick for tab options",
                ref _depositMenuOpen, ref _depositMenuAnchor, DepositPopupId,
                DrawDepositMenuItems);

            ImGui.SameLine();
            DrawIconButton(FontAwesomeIcon.Clone, "Dupes", "Deposit Duplicates",
                onClick: () => RequestDeposit("Deposit all duplicates?", () => _helper.DepositDuplicates()));

            ImGui.SameLine();
            DrawIconButton(FontAwesomeIcon.FileAlt, "DepCustom", "Deposit Custom List",
                onClick: () => RequestDeposit("Deposit Custom list?", () => _helper.DepositCustomItems()));

            ImGui.SameLine();
            DrawCrystalButton();

            ImGui.SameLine();
            DrawSplitButtonWithDropdown(
                FontAwesomeIcon.ArrowUp, "WithMenu",
                "Withdraw\nClick for tab options",
                ref _withdrawMenuOpen, ref _withdrawMenuAnchor, WithdrawPopupId,
                DrawWithdrawMenuItems);

            ImGui.SameLine();
            DrawIconButton(FontAwesomeIcon.FileAlt, "Custom", "Withdraw Custom List",
                onClick: () => RequestWithdraw("Withdraw Custom list?", () => _helper.WithdrawCustomItems()));

            ImGui.SameLine();
            DrawIconButton(FontAwesomeIcon.ListUl, "Workshop", "Withdraw Workshop List",
                onClick: () => RequestWithdraw("Withdraw Workshop List?", () => _helper.WithdrawWorkshopItems()));

            DrawConfirmationModal("Confirm Deposit", _confirmMessage, ref _showDepositConfirm, true);
            DrawConfirmationModal("Confirm Withdraw", _confirmMessage, ref _showWithdrawConfirm, false);

            LastHeight = ImGui.GetWindowSize().Y;
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

            if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);

            if (clicked && onClick != null) onClick();
            return clicked;
        }

        private void DrawCrystalButton()
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

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Crystals\nLeft Click: Deposit\nRight Click: Withdraw");
        }

        private void DrawSplitButtonWithDropdown(
            FontAwesomeIcon icon,
            string id,
            string tooltip,
            ref bool openFlag,
            ref Vector2 anchor,
            string popupId,
            Action drawItems)
        {
            var cursorScreen = ImGui.GetCursorScreenPos();
            var pendingAnchor = new Vector2(cursorScreen.X, cursorScreen.Y + ImGui.GetFrameHeight() + SubmenuGap);

            bool clicked = DrawIconButton(icon, id, tooltip);

            if (clicked)
            {
                anchor = pendingAnchor;
                openFlag = true;
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

        private void DrawDepositMenuItems()
        {
            if (ImGui.Selectable("Deposit All", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                RequestDeposit("Deposit ALL allowed items?", () => _helper.DepositAll());

            ImGui.Separator();

            for (int t = 1; t <= 5; t++)
            {
                int tab = t;
                if (ImGui.Selectable($"Deposit Tab {tab}", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                    RequestDeposit($"Deposit eligible items to Tab {tab}?", () => _helper.DepositToTab(tab));
            }
        }

        private void DrawWithdrawMenuItems()
        {
            if (ImGui.Selectable("Withdraw All", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                RequestWithdraw("Withdraw ALL items?", () => _helper.WithdrawAll());

            ImGui.Separator();

            for (int t = 1; t <= 5; t++)
            {
                int tab = t;
                if (ImGui.Selectable($"Withdraw Tab {tab}", false, ImGuiSelectableFlags.None, SubmenuItemSize))
                    RequestWithdraw($"Withdraw all items from Tab {tab}?", () => _helper.WithdrawFromTab(tab));
            }
        }

        private void RequestDeposit(string message, Action action)
        {
            if (_configuration.DisableAskDepositAll)
            {
                action();
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
            if (_configuration.DisableAskWithdrawAll)
            {
                action();
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
                    _pendingConfirmAction?.Invoke();
                    showFlag = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(120, 0))) { showFlag = false; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
        }
        public void Dispose() { }
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
                _helper.Stop();
            }
            ImGui.PopStyleColor(2);

            ImGui.PopStyleVar(2);

            LastHeight = ImGui.GetWindowSize().Y;
        }
        public void Dispose() { }
    }
}
