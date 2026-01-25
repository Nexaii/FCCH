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

        public GeneralTab(Configuration configuration, FileDialogManager fileDialogManager)
        {
            _configuration = configuration;
            _fileDialogManager = fileDialogManager;
        }

        public void Draw()
        {
            if (ImGui.BeginChild("GeneralTabScroll", new Vector2(0, 0), true))
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Audio", ImGuiTreeNodeFlags.Leaf);
                
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
                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Confirmations", ImGuiTreeNodeFlags.Leaf);

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
                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Behavior Rules", ImGuiTreeNodeFlags.Leaf);

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
                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Timing", ImGuiTreeNodeFlags.Leaf);

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
                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Debug", ImGuiTreeNodeFlags.Leaf);

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
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 35);
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
                    _configuration.MoveDelayInMs = 700;
                    _configuration.WithdrawDelayInMs = 700;
                    _configuration.DebugMode = false;
                    _configuration.DebugLogPath = "";
                    _configuration.VerboseMode = false;
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
    }
}
