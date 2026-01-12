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
using FC_Chest_Helper.Common;
using FC_Chest_Helper.Models;

namespace FC_Chest_Helper.UI
{

    public class OverlayManager : IDisposable
    {
        private readonly FCChestHelper _helper;
        private readonly IGameGui _gameGui;
        private readonly Configuration _configuration;
        
        private readonly ToolbarWindow _toolbarWindow;
        private readonly StopWindow _stopWindow;

        public OverlayManager(FCChestHelper helper, IGameGui gameGui, Configuration configuration, WindowSystem windowSystem)
        {
            _helper = helper;
            _gameGui = gameGui;
            _configuration = configuration;

            _toolbarWindow = new ToolbarWindow(helper, configuration);
            _stopWindow = new StopWindow(helper);

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

            // Base Position
            float scale = addon->Scale;
            var baseX = addon->X + (5 * scale); // Moved 5px more left (was +10, now +5), scaled
            var baseY = addon->Y - (40 * scale); // Above chest, scaled

            // Pass scale to window for spacer sizing
            _toolbarWindow.CurrentScale = scale;

            if (_helper.IsProcessing)
            {
                // Show ONLY Stop Window
                _toolbarWindow.IsOpen = false;
                
                _stopWindow.IsOpen = true;
                _stopWindow.Position = new Vector2(baseX, baseY);
            }
            else
            {
                // Show Operations (Toolbar)
                _stopWindow.IsOpen = false;

                _toolbarWindow.IsOpen = true;
                _toolbarWindow.Position = new Vector2(baseX, baseY);
            }
        }

        public void Dispose() { }
    }

    // Unified Toolbar Window
    public class ToolbarWindow : Window, IDisposable
    {
        private readonly FCChestHelper _helper;
        private readonly Configuration _configuration;
        
        // State for Confirmations
        private bool _showDepositConfirm = false;
        private bool _showWithdrawConfirm = false;
        private bool _dontShowAgain = false;
        
        // Dynamic Confirmation State
        private Action? _pendingConfirmAction;
        private string _confirmMessage = "";

        public float CurrentScale { get; set; } = 1.0f;

        public ToolbarWindow(FCChestHelper helper, Configuration configuration) : base("FCCH Toolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove)
        {
            _helper = helper;
            _configuration = configuration;
            RespectCloseHotkey = false;
        }

        public override void Draw()
        {
            // Set Spacing: 5px between items (buttons)
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5 * CurrentScale, 5 * CurrentScale));

            // SETTINGS SECTION
            ImGui.AlignTextToFramePadding(); // Align icon
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString()))
            {
                // Toggle via Helper property, triggering toggle in System loop
                _helper.IsSettingsVisible = !_helper.IsSettingsVisible;
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Settings");

            // SECTION SPACER (12px total: 4px ItemSpacing + Dummy(4px) + 4px ItemSpacing)
            ImGui.SameLine();
            ImGui.Dummy(new Vector2(7 * CurrentScale, 0));

            // DEPOSIT SECTION
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), FontAwesomeIcon.ArrowCircleDown.ToIconString());
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Deposit");
            
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.LayerGroup.ToIconString() + "##DepAll"))
            {
                RequestDeposit("Deposit ALL allowed items?", () => _helper.DepositAll());
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Deposit All");
            
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.Clone.ToIconString() + "##Dupes"))
            {
                RequestDeposit("Deposit all duplicates?", () => _helper.DepositDuplicates());
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Deposit Duplicates");

            // SECTION SPACER (12px total)
            ImGui.SameLine();
            ImGui.Dummy(new Vector2(7 * CurrentScale, 0));

            // WITHDRAW SECTION
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(1f, 0.64f, 0f, 1f), FontAwesomeIcon.ArrowCircleUp.ToIconString());
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Withdraw");
            
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.LayerGroup.ToIconString() + "##WithAll"))
            {
                RequestWithdraw("Withdraw ALL items?", () => _helper.WithdrawAll());
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Withdraw All");
            
            ImGui.SameLine();
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.FileAlt.ToIconString() + "##Singles"))
            {
                RequestWithdraw("Withdraw Single Items list?", () => 
                {
                    var list = new Dictionary<uint, int>();
                    foreach(var item in _configuration.WithdrawItems) 
                    {
                        if (!list.ContainsKey(item.ItemId)) list[item.ItemId] = 0;
                        list[item.ItemId] += item.Quantity;
                    }
                    _helper.WithdrawMaterials(list);
                });
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Withdraw Singles List");

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.ListUl.ToIconString() + "##Workshop"))
            {
                RequestWithdraw("Withdraw Workshop Projects?", () => 
                {
                    var list = new Dictionary<uint, int>();
                    foreach(var shopItem in _helper.ShoppingList)
                    {
                        var mats = shopItem.Craft.Phases
                            .SelectMany(p => p.Items)
                            .Select(x => new { Item = x, Required = x.TotalQuantity * shopItem.Quantity });

                        foreach(var mat in mats)
                        {
                            if (!list.ContainsKey(mat.Item.ItemId)) list[mat.Item.ItemId] = 0;
                            list[mat.Item.ItemId] += mat.Required;
                        }
                    }
                    _helper.WithdrawMaterials(list);
                });
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Withdraw Workshop Projects");

            // CONFIRMATION MODALS
            DrawConfirmationModal("Confirm Deposit", _confirmMessage, ref _showDepositConfirm, true);
            DrawConfirmationModal("Confirm Withdraw", _confirmMessage, ref _showWithdrawConfirm, false);
            
            ImGui.PopStyleVar(); // Pop ItemSpacing
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

    // Stop Window (Kept separate as it acts as a modal overlay/status indicator)
    public class StopWindow : Window, IDisposable
    {
        private readonly FCChestHelper _helper;

        public StopWindow(FCChestHelper helper) : base("StopWindow", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove)
        {
            _helper = helper;
            RespectCloseHotkey = false;
        }

        public override void Draw()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.HealerGreen, "Processing...");
            ImGui.SameLine();
            
            // "X" button style (Transparent normal, Red Hover)
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Times.ToIconString()))
            {
                _helper.Stop();
            }
            ImGui.PopFont();
        }
        public void Dispose() { }
    }
}
