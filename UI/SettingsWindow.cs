using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using FC_Chest_Helper.GameData;
using FC_Chest_Helper.UI;

using Dalamud.Interface.ImGuiFileDialog;

namespace FC_Chest_Helper.UI
{
    public unsafe class SettingsWindow : Window, IDisposable
    {
        private readonly FCChestHelper _helper;
        private readonly Configuration _configuration;
        private readonly IGameGui _gameGui;
        private readonly FileDialogManager _fileDialogManager;

        // UI Tabs
        private readonly GeneralTab _generalTab;
        private readonly IgnoreTab _ignoreTab;
        private readonly SingleItemsTab _singleItemsTab;
        private readonly WorkshopTab _workshopTab;

        public SettingsWindow(FCChestHelper helper, WorkshopCache cache, IGameGui gameGui, Configuration configuration) 
            : base("FCCH Settings###SettingsWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _helper = helper;
            _gameGui = gameGui;
            _configuration = configuration;
            _fileDialogManager = new FileDialogManager();
            RespectCloseHotkey = false;
            
            this.Size = new Vector2(450, 600);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(380, 450),
                MaximumSize = new Vector2(800, 1000)
            };

            _generalTab = new GeneralTab(configuration, _fileDialogManager); // Passed manager
            _ignoreTab = new IgnoreTab(helper, configuration);
            _singleItemsTab = new SingleItemsTab(helper, configuration);
            _workshopTab = new WorkshopTab(helper, configuration, cache);
        }

        public override void PreDraw()
        {
            _fileDialogManager.Draw();
            if (_configuration.IsWindowLocked)
            {
                this.Flags |= ImGuiWindowFlags.NoMove;
            }
            else
            {
                this.Flags &= ~ImGuiWindowFlags.NoMove;
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
            
            // Positioning logic only if Addon is visible and we want to attach
            if (addon != null && addon->IsVisible)
            {
                var myWidth = ImGui.GetWindowSize().X;
                float targetX;
                float targetY = addon->Y + 4;

                if (_configuration.ListsOnRightSide)
                {
                    float rootWidth = addon->RootNode != null ? addon->RootNode->Width : 0;
                    targetX = addon->X + (rootWidth * addon->Scale) + 10;
                }
                else
                {
                    targetX = addon->X - myWidth - 10;
                }
                
                 ImGui.SetWindowPos(new Vector2(targetX, targetY), ImGuiCond.Always);
            }

            DrawContent();
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

                if (ImGui.BeginTabItem("Singles")) 
                {
                    _singleItemsTab.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Workshop")) 
                {
                    _workshopTab.Draw();
                    ImGui.EndTabItem();
                }

                // Switch Side Tab
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
            // No unmanaged resources to dispose in tabs atm
        }
    }
}
