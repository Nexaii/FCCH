using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using FCCH;
using FCCH.Common;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Colors;

namespace FCCH.UI
{
    public class GeneralTab
    {
        private readonly Configuration _configuration;
        private readonly FileDialogManager _fileDialogManager;
        private readonly DragDropHelper<ToolbarButtonConfig> _toolbarButtonDrag = new("FCCHToolbarButton", x => x.Id.ToString());

        public GeneralTab(Configuration configuration, FileDialogManager fileDialogManager)
        {
            _configuration = configuration;
            _fileDialogManager = fileDialogManager;
        }

        public void Draw()
        {
            if (ImGui.BeginChild("GeneralTabScroll", new Vector2(0, 0), true))
            {
                if (DrawSection("Audio"))
                {
                    DrawSettingRow("Completion Sound", () =>
                    {
                        bool playSound = _configuration.PlayCompletionSound;
                        if (ImGui.Checkbox("##complSound", ref playSound))
                        {
                            _configuration.PlayCompletionSound = playSound;
                            _configuration.Save();
                        }
                    });

                    DrawSettingRow("Custom Sound Path", () =>
                    {
                        string path = _configuration.CustomSoundPath;
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 35);
                        if (ImGui.InputTextWithHint("##soundPath", "Default: Assets\\Completion.mp3", ref path, 1000))
                        {
                            _configuration.CustomSoundPath = path;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button(FontAwesomeIcon.Folder.ToIconString() + "##soundBrowse"))
                        {
                            _fileDialogManager.OpenFileDialog("Select Sound File", "Audio Files{.mp3,.wav}", (success, selectedPath) =>
                            {
                                if (success)
                                {
                                    _configuration.CustomSoundPath = selectedPath;
                                    _configuration.Save();
                                }
                            });
                        }
                        ImGui.PopFont();
                    });
                }
                ImGui.Spacing();

                if (DrawSection("Behavior Rules"))
                {
                    DrawToggleGrid(new[]
                    {
                        new Toggle("Skip Deposit Confirm", "", "skipDep",
                            () => _configuration.DisableAskDepositAll, v => _configuration.DisableAskDepositAll = v),
                        new Toggle("Skip Withdraw Confirm", "", "skipWith",
                            () => _configuration.DisableAskWithdrawAll, v => _configuration.DisableAskWithdrawAll = v),
                        new Toggle("Lower Quality on Deposit", "Automatically convert HQ items to NQ before depositing.", "lowerQual",
                            () => _configuration.LowerQualityOnDeposit, v => _configuration.LowerQualityOnDeposit = v),
                        new Toggle("Leave One per Stack", "Always leave at least 1 item in the FC Chest when withdrawing.", "leaveOne",
                            () => _configuration.LeaveOneItemPerStack, v => _configuration.LeaveOneItemPerStack = v),
                        new Toggle("Compact Item Names", "Shorten supported item names in Custom, Ignore, and Organizer lists.", "compactItemNames",
                            () => _configuration.CompactItemNames, v => _configuration.CompactItemNames = v),
                        new Toggle("Search Bar", "Show the search bar overlay on the FC Chest header. Press Ctrl+F to focus it.", "searchBar",
                            () => _configuration.SearchBarEnabled, v => _configuration.SearchBarEnabled = v),
                        new Toggle("Item Context Menu", "Add FCCH entries to supported item right-click menus.", "itemContextMenu",
                            () => _configuration.EnableItemContextMenuEntries, v => _configuration.EnableItemContextMenuEntries = v),
                        new Toggle("Fast Move", "Modifier + right-click: deposit an inventory item or withdraw a chest item.\nDeposits to the open tab or hold 1-5 to pick a specific tab.", "fastMove",
                            () => _configuration.FastMoveEnabled, v => _configuration.FastMoveEnabled = v),
                    });

                    if (_configuration.FastMoveEnabled)
                    {
                        DrawSettingRow("  Modifier + Right-Click", DrawModifierDropdown);
                        DrawFastMoveConflictNote();
                    }
                }
                ImGui.Spacing();

                if (DrawSection("Timing"))
                {
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.2f, 0.1f, 0.5f));
                    if (ImGui.BeginChild("TimingWarning", new Vector2(ImGui.GetContentRegionAvail().X, 40), true))
                    {
                        ImGui.TextColored(ImGuiColors.DalamudOrange, "Low delay may cause desync on slow connections.");
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleColor();

                    ImGui.Spacing();

                    DrawSettingRow("Deposit Delay", () =>
                    {
                        int depositDelay = _configuration.MoveDelayInMs;
                        ImGui.SetNextItemWidth(180);
                        if (ImGui.SliderInt("##depDelay", ref depositDelay, 700, 1500, "%d ms"))
                        {
                            _configuration.MoveDelayInMs = depositDelay;
                            _configuration.Save();
                        }
                    });

                    DrawSettingRow("Withdraw Delay", () =>
                    {
                        int withdrawDelay = _configuration.WithdrawDelayInMs;
                        ImGui.SetNextItemWidth(180);
                        if (ImGui.SliderInt("##withDelay", ref withdrawDelay, 700, 1500, "%d ms"))
                        {
                            _configuration.WithdrawDelayInMs = withdrawDelay;
                            _configuration.Save();
                        }
                    });
                }
                ImGui.Spacing();

                if (DrawSection("Toolbar"))
                {
                    DrawSettingRow("Lock Toolbar Position", () =>
                    {
                        bool locked = _configuration.ToolbarLocked;
                        if (ImGui.Checkbox("##toolbarLocked", ref locked))
                        {
                            _configuration.ToolbarLocked = locked;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("When locked, the toolbar stays at its current position. When unlocked, you can drag it freely.");
                    });

                    DrawSettingRow("Snap to Chest", () =>
                    {
                        if (ImGui.Button("Snap##toolbarSnap", new Vector2(120, 0)))
                        {
                            _configuration.ToolbarPosX = -1f;
                            _configuration.ToolbarPosY = -1f;
                            _configuration.Save();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset toolbar position back to its attached spot above the Company Chest.");
                    });

                    DrawSettingRow("Snap to Grid", () =>
                    {
                        bool snapGrid = _configuration.ToolbarSnapToGrid;
                        if (ImGui.Checkbox("##toolbarSnapGrid", ref snapGrid))
                        {
                            _configuration.ToolbarSnapToGrid = snapGrid;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("While unlocked, snap toolbar position to a 10px grid when dragging.");
                    });

                    DrawToolbarButtonLayout();
                }
                ImGui.Spacing();

#if DEBUG
                if (DrawSection("Diagnostics"))
                {
                    DrawSettingRow("Enable Debug Mode", () =>
                    {
                        bool debug = _configuration.DebugMode;
                        if (ImGui.Checkbox("##debugMode", ref debug))
                        {
                            _configuration.DebugMode = debug;
                            _configuration.Save();
                        }
                    });

                    DrawSettingRow("Custom Debug Path", () =>
                    {
                        string logPath = _configuration.DebugLogPath;
                        ImGui.SetNextItemWidth(220f);
                        if (ImGui.InputTextWithHint("##logPath", "Default: FCCH_Debug.log", ref logPath, 256))
                        {
                            _configuration.DebugLogPath = logPath;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button(FontAwesomeIcon.Folder.ToIconString() + "##logBrowse"))
                        {
                            _fileDialogManager.SaveFileDialog("Select Log File", ".log", "FCCH_Debug.log", ".log", (success, selectedPath) =>
                            {
                                if (success)
                                {
                                    _configuration.DebugLogPath = selectedPath;
                                    _configuration.Save();
                                }
                            });
                        }
                        ImGui.PopFont();
                    });

                    DrawSettingRow("Verbose Logging", () =>
                    {
                        bool verbose = _configuration.VerboseMode;
                        if (ImGui.Checkbox("##verbose", ref verbose))
                        {
                            _configuration.VerboseMode = verbose;
                            _configuration.Save();
                        }
                    });

                    ImGui.Spacing();
                    ImGui.TextDisabled("Internal diagnostic commands");
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.2f, 0.1f, 0.5f));
                    string[] diagnosticCommands =
                    {
                        "accessprobe - dump live chest addon permission state",
                        "debug - toggle debug logging",
                        "fcperms [row] - dump raw FC rank permission bytes",
                        "gildebug - trace gil callbacks",
                        "info - dump FC rank + per-tab access (chest must be open)",
                        "ipctest - invoke FCCH IPC surface and report pass/fail to /xllog",
                    };
                    float diagnosticBoxHeight = ImGui.GetTextLineHeightWithSpacing() * diagnosticCommands.Length + ImGui.GetStyle().WindowPadding.Y * 2;
                    if (ImGui.BeginChild("InternalDiagnosticsBox", new Vector2(ImGui.GetContentRegionAvail().X, diagnosticBoxHeight), true))
                    {
                        foreach (var command in diagnosticCommands)
                            ImGui.TextColored(ImGuiColors.DalamudOrange, command);
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                }
#endif
                ImGui.Spacing();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 130;
                ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f + ImGui.GetCursorPosX());
                var style = ImGui.GetStyle();
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                if (ImGui.Button("Reset to Defaults", new Vector2(buttonWidth, 0)))
                {
                    _configuration.PlayCompletionSound = false;
                    _configuration.CustomSoundPath = "";
                    _configuration.DisableAskDepositAll = false;
                    _configuration.DisableAskWithdrawAll = false;
                    _configuration.LowerQualityOnDeposit = false;
                    _configuration.LeaveOneItemPerStack = false;
                    _configuration.ToolbarLocked = true;
                    _configuration.ToolbarPosX = -1f;
                    _configuration.ToolbarPosY = -1f;
                    _configuration.ToolbarSnapToGrid = false;
                    _configuration.ResetToolbarButtons();
                    _configuration.MoveDelayInMs = 700;
                    _configuration.WithdrawDelayInMs = 700;
                    _configuration.DebugMode = false;
                    _configuration.DebugLogPath = "";
                    _configuration.VerboseMode = false;
                    _configuration.CompactItemNames = true;
                    _configuration.EnableItemContextMenuEntries = false;
                    _configuration.Save();
                }
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset all General settings to their default values.");

                ImGui.EndChild();
            }
        }

        private static readonly VirtualKey[] FastMoveModifiers = { VirtualKey.MENU, VirtualKey.CONTROL, VirtualKey.SHIFT };

        private void DrawModifierDropdown()
        {
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##fastMoveMod", _configuration.FastMoveModifier.GetFancyName()))
            {
                foreach (var key in FastMoveModifiers)
                {
                    if (ImGui.Selectable(key.GetFancyName(), _configuration.FastMoveModifier == key))
                    {
                        _configuration.FastMoveModifier = key;
                        _configuration.Save();
                    }
                }
                ImGui.EndCombo();
            }
        }

        private static void DrawFastMoveConflictNote()
        {
            const string note = "Fast Move may conflict with other plugins. If it misfires or items go to the wrong destination, disable the other plugin or change the modifier above.";

            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.2f, 0.1f, 0.5f));
            float width = ImGui.GetContentRegionAvail().X;
            float textHeight = ImGui.CalcTextSize(note, false, width - ImGui.GetStyle().WindowPadding.X * 2).Y;
            float boxHeight = textHeight + ImGui.GetStyle().WindowPadding.Y * 2;
            if (ImGui.BeginChild("FastMoveConflictNote", new Vector2(width, boxHeight), true))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                ImGui.TextWrapped(note);
                ImGui.PopStyleColor();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private void DrawSettingRow(string label, System.Action drawControl)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);
            ImGui.SameLine(180);
            drawControl();
        }

        private readonly struct Toggle
        {
            public readonly string Label;
            public readonly string Tooltip;
            public readonly string Id;
            public readonly System.Func<bool> Get;
            public readonly System.Action<bool> Set;

            public Toggle(string label, string tooltip, string id, System.Func<bool> get, System.Action<bool> set)
            {
                Label = label;
                Tooltip = tooltip;
                Id = id;
                Get = get;
                Set = set;
            }
        }

        private void DrawToggleGrid(Toggle[] toggles)
        {
            float half = ImGui.GetContentRegionAvail().X * 0.5f;
            for (int i = 0; i < toggles.Length; i++)
            {
                if ((i & 1) == 1)
                    ImGui.SameLine(half);
                DrawToggleCell(toggles[i]);
            }
        }

        private void DrawToggleCell(Toggle toggle)
        {
            bool value = toggle.Get();
            if (ImGui.Checkbox($"{toggle.Label}##{toggle.Id}", ref value))
            {
                toggle.Set(value);
                _configuration.Save();
            }
            if (toggle.Tooltip.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(toggle.Tooltip);
            }
        }

        private static bool DrawSection(string label)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
            return ImGui.CollapsingHeader(label);
        }

        private void DrawToolbarButtonLayout()
        {
            if (_configuration.EnsureToolbarButtons())
                _configuration.Save();

            var visibleButtons = CountVisibleToolbarButtons();
            var totalButtons = _configuration.ToolbarButtons.Count;
            if (!ImGui.CollapsingHeader($"Toolbar Buttons ({visibleButtons} / {totalButtons} shown)###toolbarButtons"))
                return;

            var tableWidth = CalculateToolbarButtonTableWidth();
            if (!ImGui.BeginTable("ToolbarButtonLayout", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX, new Vector2(tableWidth, 0)))
                return;

            ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthFixed, 22);
            ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed, 22);
            ImGui.TableSetupColumn("Button", ImGuiTableColumnFlags.WidthFixed, tableWidth - 44);

            _toolbarButtonDrag.Begin();
            for (var i = 0; i < _configuration.ToolbarButtons.Count; i++)
            {
                var button = _configuration.ToolbarButtons[i];
                ImGui.PushID($"toolbar_button_{button.Id}");
                _toolbarButtonDrag.NextRow();
                ImGui.TableNextRow();
                _toolbarButtonDrag.SetRowColor(button);

                ImGui.TableNextColumn();
                _toolbarButtonDrag.DrawButtonDummy(button, _configuration.ToolbarButtons, i, _ => _configuration.Save());

                ImGui.TableNextColumn();
                DrawToolbarButtonToggle(button, visibleButtons);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(GetToolbarButtonLabel(button.Id));

                ImGui.PopID();
            }

            ImGui.EndTable();
            _toolbarButtonDrag.End();

            const float resetWidth = 120f;
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - resetWidth) * 0.5f);
            if (ImGui.Button("Reset##toolbarButtonsReset", new Vector2(resetWidth, 0)))
            {
                _configuration.ResetToolbarButtons();
                _configuration.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Restore the default toolbar button order and visibility.");
        }

        private float CalculateToolbarButtonTableWidth()
        {
            var maxLabelWidth = 0f;
            foreach (var button in _configuration.ToolbarButtons)
            {
                var labelWidth = ImGui.CalcTextSize(GetToolbarButtonLabel(button.Id)).X;
                if (labelWidth > maxLabelWidth) maxLabelWidth = labelWidth;
            }

            var style = ImGui.GetStyle();
            return 44f + maxLabelWidth + style.CellPadding.X * 4f + 4f;
        }

        private void DrawToolbarButtonToggle(ToolbarButtonConfig button, int visibleCount)
        {
            var visible = button.IsVisible;
            var mustKeepVisible = visible && visibleCount <= 1;
            if (mustKeepVisible) ImGui.BeginDisabled();

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (visible) ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            if (ImGui.SmallButton($"{(visible ? FontAwesomeIcon.ToggleOn : FontAwesomeIcon.ToggleOff).ToIconString()}##visible"))
            {
                button.IsVisible = !visible;
                _configuration.Save();
            }
            if (visible) ImGui.PopStyleColor();
            ImGui.PopStyleColor(2);
            ImGui.PopFont();

            if (mustKeepVisible) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(mustKeepVisible ? "At least one toolbar button must remain visible." : visible ? "Shown on toolbar" : "Hidden from toolbar");
        }

        private int CountVisibleToolbarButtons()
        {
            var count = 0;
            foreach (var button in _configuration.ToolbarButtons)
            {
                if (button.IsVisible) count++;
            }
            return count;
        }

        private static string GetToolbarButtonLabel(ToolbarButtonId id)
        {
            return id switch
            {
                ToolbarButtonId.Settings => "Settings",
                ToolbarButtonId.Deposit => "Deposit",
                ToolbarButtonId.DepositCustom => "Deposit Custom List",
                ToolbarButtonId.DepositDuplicates => "Deposit Duplicates",
                ToolbarButtonId.Crystals => "Crystals",
                ToolbarButtonId.Withdraw => "Withdraw",
                ToolbarButtonId.WithdrawCustom => "Withdraw Custom List",
                ToolbarButtonId.WithdrawWorkshop => "Withdraw Workshop List",
                ToolbarButtonId.Sort => "Sort/Merge",
                _ => id.ToString()
            };
        }
    }
}
