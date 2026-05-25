using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using FCCH;
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

                if (DrawSection("Confirmations"))
                {
                    DrawSettingRow("Skip Deposit Confirm", () =>
                    {
                        bool disableDep = _configuration.DisableAskDepositAll;
                        if (ImGui.Checkbox("##skipDep", ref disableDep))
                        {
                            _configuration.DisableAskDepositAll = disableDep;
                            _configuration.Save();
                        }
                    });

                    DrawSettingRow("Skip Withdraw Confirm", () =>
                    {
                        bool disableWith = _configuration.DisableAskWithdrawAll;
                        if (ImGui.Checkbox("##skipWith", ref disableWith))
                        {
                            _configuration.DisableAskWithdrawAll = disableWith;
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

                if (DrawSection("Behavior Rules"))
                {
                    DrawSettingRow("Lower Quality on Deposit", () =>
                    {
                        bool lowerQuality = _configuration.LowerQualityOnDeposit;
                        if (ImGui.Checkbox("##lowerQual", ref lowerQuality))
                        {
                            _configuration.LowerQualityOnDeposit = lowerQuality;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Automatically convert HQ items to NQ before depositing.");
                    });

                    DrawSettingRow("Leave One per Stack", () =>
                    {
                        bool leaveOne = _configuration.LeaveOneItemPerStack;
                        if (ImGui.Checkbox("##leaveOne", ref leaveOne))
                        {
                            _configuration.LeaveOneItemPerStack = leaveOne;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Always leave at least 1 item in the FC Chest when withdrawing.");
                    });

                    DrawSettingRow("Compact Item Names", () =>
                    {
                        bool compactNames = _configuration.CompactItemNames;
                        if (ImGui.Checkbox("##compactItemNames", ref compactNames))
                        {
                            _configuration.CompactItemNames = compactNames;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Shorten supported item names in Custom, Ignore, and Organizer lists.");
                    });

                    DrawSettingRow("Item Context Menu", () =>
                    {
                        bool enabled = _configuration.EnableItemContextMenuEntries;
                        if (ImGui.Checkbox("##itemContextMenu", ref enabled))
                        {
                            _configuration.EnableItemContextMenuEntries = enabled;
                            _configuration.Save();
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("(?)");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add FCCH entries to supported item right-click menus.");
                    });
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
                    float diagnosticBoxHeight = ImGui.GetTextLineHeightWithSpacing() * 5 + ImGui.GetStyle().WindowPadding.Y * 2;
                    if (ImGui.BeginChild("InternalDiagnosticsBox", new Vector2(ImGui.GetContentRegionAvail().X, diagnosticBoxHeight), true))
                    {
                        ImGui.TextColored(ImGuiColors.DalamudOrange, "debug - toggle debug logging");
                        ImGui.TextColored(ImGuiColors.DalamudOrange, "gildebug - trace gil callbacks");
                        ImGui.TextColored(ImGuiColors.DalamudOrange, "accessprobe - dump live chest addon permission state");
                        ImGui.TextColored(ImGuiColors.DalamudOrange, "fcperms [row] - dump raw FC rank permission bytes");
                        ImGui.TextColored(ImGuiColors.DalamudOrange, "ipctest - invoke FCCH IPC surface and report pass/fail to /xllog");
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                }
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

        private void DrawSettingRow(string label, System.Action drawControl)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);
            ImGui.SameLine(180);
            drawControl();
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

            DrawSettingRow("Toolbar Buttons", () =>
            {
                if (ImGui.Button("Reset##toolbarButtonsReset", new Vector2(120, 0)))
                {
                    _configuration.ResetToolbarButtons();
                    _configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Restore the default toolbar button order and visibility.");
            });

            var tableWidth = CalculateToolbarButtonTableWidth();
            if (!ImGui.BeginTable("ToolbarButtonLayout", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX, new Vector2(tableWidth, 0)))
                return;

            ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthFixed, 22);
            ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed, 22);
            ImGui.TableSetupColumn("Button", ImGuiTableColumnFlags.WidthFixed, tableWidth - 44);

            _toolbarButtonDrag.Begin();
            var visibleCount = CountVisibleToolbarButtons();
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
                DrawToolbarButtonToggle(button, visibleCount);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(GetToolbarButtonLabel(button.Id));

                ImGui.PopID();
            }

            ImGui.EndTable();
            _toolbarButtonDrag.End();
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
                _ => id.ToString()
            };
        }
    }
}
