using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using FCCH.GameData;
using FCCH.UI;
using FCCH.Managers;
using FCCH.Managers.Organizer;

using Dalamud.Interface.ImGuiFileDialog;

namespace FCCH.UI
{
    public unsafe class SettingsWindow : Window, IDisposable
    {
        private readonly ChestHelper _helper;
        private readonly Configuration _configuration;
        private readonly IGameGui _gameGui;
        private readonly FileDialogManager _fileDialogManager;
        private bool _wasChestVisible;

        private readonly GeneralTab _generalTab;
        private readonly IgnoreTab _ignoreTab;
        private readonly CustomTab _customTab;
        private readonly WorkshopTab _workshopTab;
        private readonly CrystalTabUI _crystalsTab;
        private readonly OrganizerTab _organizerTab;

        private readonly TitleBarButton _kofiButton;
        private readonly TitleBarButton _settingsLockButton;
        private readonly TitleBarButton _settingsSnapButton;
        private bool _snapPending;

        public SettingsWindow(ChestHelper helper, WorkshopCache cache, IGameGui gameGui, Configuration configuration, OrgService orgService, Common.WorkshoppaIPC workshoppaIpc)
            : base("FCCH Settings###SettingsWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _helper = helper;
            _gameGui = gameGui;
            _configuration = configuration;
            _fileDialogManager = new FileDialogManager();
            RespectCloseHotkey = false;
            
            this.Size = new Vector2(520, 600);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(480, 450),
                MaximumSize = new Vector2(900, 1000)
            };

            _generalTab = new GeneralTab(configuration, _fileDialogManager);
            _ignoreTab = new IgnoreTab(helper, configuration);
            _customTab = new CustomTab(helper, configuration);
            _workshopTab = new WorkshopTab(helper, configuration, cache, workshoppaIpc);
            _crystalsTab = new CrystalTabUI(configuration, helper.CrystalMgr);
            _organizerTab = new OrganizerTab(orgService, configuration);

            _kofiButton = new TitleBarButton
            {
                Icon = FontAwesomeIcon.Heart,
                ShowTooltip = () => { ImGui.SetTooltip("Support on Ko-Fi"); },
                Priority = int.MinValue,
                IconOffset = new Vector2(1.5f, 1),
                Click = _ => OpenKoFiLink(),
                AvailableClickthrough = true,
            };

            _settingsLockButton = new TitleBarButton
            {
                Icon = _configuration.IsWindowLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                ShowTooltip = () => ImGui.SetTooltip(_configuration.IsWindowLocked
                    ? "Settings window is locked to the Company Chest\nClick to unlock and drag freely"
                    : "Settings window is unlocked\nClick to lock current position"),
                Priority = 0,
                Click = _ => ToggleSettingsLock(),
            };

            _settingsSnapButton = new TitleBarButton
            {
                Icon = FontAwesomeIcon.Crosshairs,
                ShowTooltip = () => ImGui.SetTooltip("Snap Settings window back to the Company Chest"),
                Priority = 1,
                Click = _ => SnapSettingsToChest(),
            };
        }

        private void ToggleSettingsLock()
        {
            _configuration.IsWindowLocked = !_configuration.IsWindowLocked;
            _configuration.Save();
            _settingsLockButton.Icon = _configuration.IsWindowLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
        }

        private void SnapSettingsToChest()
        {
            _configuration.IsWindowLocked = true;
            _configuration.SettingsPosX = -1f;
            _configuration.SettingsPosY = -1f;
            _snapPending = true;
            _configuration.Save();
            _settingsLockButton.Icon = FontAwesomeIcon.Lock;
        }

        private void OpenKoFiLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ko-fi.com/nexai",
                    UseShellExecute = true,
                    Verb = string.Empty,
                });
            }
            catch { }
        }

        public override void PreDraw()
        {
            _organizerTab.Update();
            _fileDialogManager.Draw();

            if (!TitleBarButtons.Contains(_kofiButton))
            {
                TitleBarButtons.Add(_kofiButton);
            }
            if (!TitleBarButtons.Contains(_settingsLockButton))
            {
                TitleBarButtons.Add(_settingsLockButton);
            }
            if (!TitleBarButtons.Contains(_settingsSnapButton))
            {
                TitleBarButtons.Add(_settingsSnapButton);
            }

            var fcChestAddon = _gameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            bool isChestVisible = fcChestAddon != null && fcChestAddon->IsVisible;

            if (isChestVisible && !_wasChestVisible)
            {
                this.Size = new Vector2(520, 600);
                this.SizeCondition = ImGuiCond.Always;
            }
            else if (isChestVisible)
            {
                this.SizeCondition = ImGuiCond.None;
            }
            _wasChestVisible = isChestVisible;

            if (_configuration.IsWindowLocked && isChestVisible)
            {
                this.Flags |= ImGuiWindowFlags.NoMove;
            }
            else
            {
                this.Flags &= ~ImGuiWindowFlags.NoMove;
            }

            this.Flags &= ~ImGuiWindowFlags.AlwaysAutoResize;
            
            if (isChestVisible && _configuration.IsWindowLocked)
            {
                this.SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new Vector2(480, 450),
                    MaximumSize = new Vector2(560, 900)
                };
            }
            else
            {
                this.SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new Vector2(480, 450),
                    MaximumSize = new Vector2(900, 1000)
                };
            }
            base.PreDraw();
        }

        public override bool DrawConditions()
        {
            if (!_helper.IsSettingsVisible) return false;
            return true;
        }

        public override void Draw()
        {
            var addon = _gameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            bool chestVisible = addon != null && addon->IsVisible;

            if (_snapPending)
            {
                if (chestVisible)
                {
                    var attached = ComputeChestAttachedPosition(addon, ImGui.GetWindowSize().X, _configuration.ListsOnRightSide);
                    ImGui.SetWindowPos(attached, ImGuiCond.Always);
                }
                _configuration.SettingsPosX = -1f;
                _configuration.SettingsPosY = -1f;
                _snapPending = false;
            }
            else if (_configuration.IsWindowLocked)
            {
                if (_configuration.SettingsPosX >= 0 && _configuration.SettingsPosY >= 0)
                {
                    var saved = ClampToViewport(new Vector2(_configuration.SettingsPosX, _configuration.SettingsPosY));
                    ImGui.SetWindowPos(saved, ImGuiCond.Always);
                }
                else if (chestVisible)
                {
                    var attached = ComputeChestAttachedPosition(addon, ImGui.GetWindowSize().X, _configuration.ListsOnRightSide);
                    ImGui.SetWindowPos(attached, ImGuiCond.Always);
                }
            }
            else
            {
                if (_configuration.SettingsPosX >= 0 && _configuration.SettingsPosY >= 0)
                {
                    var saved = ClampToViewport(new Vector2(_configuration.SettingsPosX, _configuration.SettingsPosY));
                    ImGui.SetWindowPos(saved, ImGuiCond.Appearing);
                }
                PersistDriftIfUnlocked();
            }

            DrawContent();
        }

        private static unsafe Vector2 ComputeChestAttachedPosition(AtkUnitBase* addon, float myWidth, bool rightSide)
        {
            float scale = addon->Scale;
            float rootWidth = addon->RootNode != null ? addon->RootNode->Width * scale : 0f;
            float targetY = addon->Y + 4;
            float targetX = rightSide
                ? addon->X + rootWidth + 10
                : addon->X - myWidth - 10;

            return new Vector2(targetX, targetY);
        }

        private void PersistDriftIfUnlocked()
        {
            var pos = ImGui.GetWindowPos();
            if (Math.Abs(pos.X - _configuration.SettingsPosX) > 1f
                || Math.Abs(pos.Y - _configuration.SettingsPosY) > 1f)
            {
                _configuration.SettingsPosX = pos.X;
                _configuration.SettingsPosY = pos.Y;
                _configuration.Save();
            }
        }

        private static Vector2 ClampToViewport(Vector2 pos)
        {
            var vp = ImGui.GetMainViewport();
            const float minVisible = 80f;
            float x = Math.Clamp(pos.X, 0f, Math.Max(0f, vp.Size.X - minVisible));
            float y = Math.Clamp(pos.Y, 0f, Math.Max(0f, vp.Size.Y - minVisible));
            return new Vector2(x, y);
        }

        private void DrawContent()
        {
            if (ImGui.BeginTabBar("SettingsTabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("General"))
                {
                     _generalTab.Draw();
                     ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Ignore"))
                {
                    _ignoreTab.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Crystals"))
                {
                    _crystalsTab.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Custom"))
                {
                    _customTab.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Organizer"))
                {
                    _organizerTab.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Workshop"))
                {
                    _workshopTab.Draw();
                    ImGui.EndTabItem();
                }
                else
                {
                    _workshopTab.OnTabDeactivated();
                }

                var switchIcon = _configuration.ListsOnRightSide ? FontAwesomeIcon.AngleLeft.ToIconString() : FontAwesomeIcon.AngleRight.ToIconString();
                
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.TabItemButton(switchIcon, ImGuiTabItemFlags.Trailing))
                {
                    _configuration.ListsOnRightSide = !_configuration.ListsOnRightSide;
                    _configuration.Save();
                }
                ImGui.PopFont(); 
                
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Switch orientation relative to FC Chest");
                
                ImGui.EndTabBar();
            }
        }

        public void Dispose()
        {
            _organizerTab?.Dispose();
        }
    }
}
