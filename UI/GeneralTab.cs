using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using FC_Chest_Helper;
using Dalamud.Interface.ImGuiFileDialog;

namespace FC_Chest_Helper.UI
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
            if (ImGui.BeginChild("GeneralTabScroll", new Vector2(0, 0), false))
            {
            if (ImGui.CollapsingHeader("Audio", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Framed))
            {
                bool playSound = _configuration.PlayCompletionSound;
                if (ImGui.Checkbox("Completion Sound", ref playSound))
                {
                    _configuration.PlayCompletionSound = playSound;
                    _configuration.Save();
                }
                
                string path = _configuration.CustomSoundPath;
                ImGui.SetNextItemWidth(200);
                if (ImGui.InputTextWithHint("##soundPath", "Sound path...", ref path, 1000))
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
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Browse for sound file");
                
                if (string.IsNullOrWhiteSpace(path))
                {
                     ImGui.TextDisabled("Default: Assets\\Completion.mp3");
                }
                else
                {
                     ImGui.TextDisabled($"Custom: {System.IO.Path.GetFileName(path)}");
                     if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);
                }
            }

            if (ImGui.CollapsingHeader("Confirmations", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Framed))
            {


                bool disableDep = _configuration.DisableAskDepositAll;
                if (ImGui.Checkbox("Skip Deposit Confirmation", ref disableDep))
                {
                    _configuration.DisableAskDepositAll = disableDep;
                    _configuration.Save();
                }
                
                bool disableWith = _configuration.DisableAskWithdrawAll;
                if (ImGui.Checkbox("Skip Withdraw Confirmation", ref disableWith))
                {
                    _configuration.DisableAskWithdrawAll = disableWith;
                    _configuration.Save();
                }
            }



            // Deposit Rules
            if (ImGui.CollapsingHeader("Deposit Rules", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Framed))
            {
                bool lowerQuality = _configuration.LowerQualityOnDeposit;
                if (ImGui.Checkbox("Lower the quality of items for deposit.", ref lowerQuality))
                {
                    _configuration.LowerQualityOnDeposit = lowerQuality;
                    _configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Automatically convert HQ items to NQ before depositing if enabled.");
            }

            // Withdraw Rules
            if (ImGui.CollapsingHeader("Withdraw Rules", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Framed))
            {
                bool leaveOne = _configuration.LeaveOneItemPerStack;
                if (ImGui.Checkbox("Leave one item per stack.", ref leaveOne))
                {
                    _configuration.LeaveOneItemPerStack = leaveOne;
                    _configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Always leave at least 1 item in the FC Chest when withdrawing");
            }

            // Speed Settings
            if (ImGui.CollapsingHeader("Delay Speeds", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Framed))
            {
                ImGui.TextDisabled("For slow connections, set a higher delay to prevent desync.");
                int depositDelay = _configuration.MoveDelayInMs;
                if (ImGui.SliderInt("Deposit (ms)", ref depositDelay, 700, 1500))
                {
                    _configuration.MoveDelayInMs = depositDelay;
                    _configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delay between deposit moves.");
                
                if (depositDelay < 700)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.0f, 1.0f));
                    ImGui.TextWrapped("Fast mode - may cause sync issues on slow connections.");
                    ImGui.PopStyleColor();
                }
                
                ImGui.Separator();
                
                int withdrawDelay = _configuration.WithdrawDelayInMs;
                if (ImGui.SliderInt("Withdraw (ms)", ref withdrawDelay, 700, 1500))
                {
                    _configuration.WithdrawDelayInMs = withdrawDelay;
                    _configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delay between withdrawal moves. Slower is safer.");
                
                if (withdrawDelay < 700)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.0f, 1.0f));
                    ImGui.TextWrapped("Low risk: Fast withdrawals may cause ghost items (desync). Fixed by zoning.");
                    ImGui.PopStyleColor();
                }
                
                ImGui.Separator();
            }

            // Debug
            if (ImGui.CollapsingHeader("Debug", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Framed))
            {
                bool debug = _configuration.DebugMode;
                if (ImGui.Checkbox("Enable Debug Mode", ref debug))
                {
                    _configuration.DebugMode = debug;
                    _configuration.Save();
                }

                string logPath = _configuration.DebugLogPath;
                ImGui.SetNextItemWidth(200);
                if (ImGui.InputTextWithHint("##logPath", "Log path...", ref logPath, 256))
                {
                    _configuration.DebugLogPath = logPath;
                    _configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Debug log file path");
                
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
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Browse save location");

                bool verbose = _configuration.VerboseMode;
                if (ImGui.Checkbox("Verbose Logging", ref verbose))
                {
                    _configuration.VerboseMode = verbose;
                    _configuration.Save();
                }
            }
            }
            ImGui.EndChild();
        }
    }
}
