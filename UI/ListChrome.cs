using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;

namespace FCCH.UI
{
    internal static class ListChrome
    {
        private const string CountSample = "Items: 9,999 / 9,999";
        private const string InListLabel = "in list";

        public const float ClearButtonWidth = 80f;

        public static float CountColumnWidth()
        {
            return ImGui.CalcTextSize(CountSample).X + ImGui.GetStyle().CellPadding.X * 2;
        }

        public static string CountText(int shown, int total)
        {
            return shown == total ? $"Items: {total:N0}" : $"Items: {shown:N0} / {total:N0}";
        }

        public static void DrawCount(string label)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(label);
        }

        public static void DrawInListMarker()
        {
            ImGui.PushFont(UiBuilder.IconFont);
            var iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
            ImGui.PopFont();

            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var labelWidth = ImGui.CalcTextSize(InListLabel).X;

            ImGui.SameLine(ImGui.GetContentRegionMax().X - iconWidth - labelWidth - spacing);

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextDisabled(FontAwesomeIcon.Check.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine(0, spacing);
            ImGui.TextDisabled(InListLabel);
        }

        public static void DrawTrailingLabel(string label)
        {
            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(label).X);
            ImGui.TextDisabled(label);
        }

        public static bool DrawUndoButton<T>(UndoStack<T> undo)
        {
            var empty = undo.Count == 0;

            if (empty) ImGui.BeginDisabled();

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            var pressed = ImGui.Button(empty ? "Undo" : $"Undo ({undo.Count})", new Vector2(-1, 0));
            ImGui.PopStyleColor();

            if (empty)
            {
                ImGui.EndDisabled();
                return false;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(undo.BuildTooltip());

            return pressed;
        }

        public static bool DrawConfirm(string popupId, string body, string confirmLabel, ref bool open)
        {
            if (open)
                ImGui.OpenPopup(popupId);

            var confirmed = false;

            if (ImGui.BeginPopupModal(popupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text(body);
                ImGui.Spacing();

                if (ImGui.Button(confirmLabel, new Vector2(120, 0)))
                {
                    confirmed = true;
                    open = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    open = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            return confirmed;
        }
    }
}
