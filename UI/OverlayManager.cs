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

            float scale = addon->Scale;
            var baseX = addon->X + (5 * scale);
            var baseY = addon->Y - (40 * scale);

            _toolbarWindow.CurrentScale = scale;

            bool isOperationActive = _helper.IsUserOperationActive || _orgService.JobStatus == OrgJobStatus.Running;

            if (isOperationActive)
            {
                _toolbarWindow.IsOpen = false;
                
                _stopWindow.IsOpen = true;
                _stopWindow.Position = new Vector2(baseX, baseY);
            }
            else
            {
                _stopWindow.IsOpen = false;

                _toolbarWindow.IsOpen = true;
                _toolbarWindow.Position = new Vector2(baseX, baseY);
            }
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

        public float CurrentScale { get; set; } = 1.0f;

        public ToolbarWindow(ChestHelper helper, Configuration configuration) : base("FCCH Toolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove)
        {
            _helper = helper;
            _configuration = configuration;
            RespectCloseHotkey = false;
        }

        public override void Draw()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5 * CurrentScale, 5 * CurrentScale));

            ImGui.AlignTextToFramePadding();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString()))
            {
                _helper.IsSettingsVisible = !_helper.IsSettingsVisible;
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Settings");

            ImGui.SameLine();
            ImGui.Dummy(new Vector2(7 * CurrentScale, 0));

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

            ImGui.SameLine();
            ImGui.Dummy(new Vector2(7 * CurrentScale, 0));

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            var crystalColor = _lastCrystalAction switch
            {
                1 => new Vector4(0f, 1f, 1f, 1f),
                2 => new Vector4(1f, 0.64f, 0f, 1f),
                _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)
            };
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
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
            ImGui.PopStyleColor(3);
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Crystals\nLeft Click: Deposit\nRight Click: Withdraw");

            ImGui.SameLine();
            ImGui.Dummy(new Vector2(7 * CurrentScale, 0));

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
            if (ImGui.Button(FontAwesomeIcon.FileAlt.ToIconString() + "##Custom"))
            {
                RequestWithdraw("Withdraw Custom list?", () => 
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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Withdraw Custom List");

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

            DrawConfirmationModal("Confirm Deposit", _confirmMessage, ref _showDepositConfirm, true);
            DrawConfirmationModal("Confirm Withdraw", _confirmMessage, ref _showWithdrawConfirm, false);
            
            ImGui.PopStyleVar();
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

        public StopWindow(ChestHelper helper, OrgService orgService) : base("StopWindow", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove)
        {
            _helper = helper;
            _orgService = orgService;
            RespectCloseHotkey = false;
        }

        public override void Draw()
        {
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
        }
        public void Dispose() { }
    }
}
