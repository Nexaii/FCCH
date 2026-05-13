using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;

namespace FCCH.UI
{
    internal static class CellActionButton
    {
        public const float ColumnWidth = 30f;

        public static bool DrawText(string text, string id, string tooltip, Action onClick, bool active = false)
        {
            return Draw(text, id, tooltip, onClick, active, false, false);
        }

        public static bool DrawIcon(FontAwesomeIcon icon, string id, string tooltip, Action onClick, bool danger = false, bool active = false)
        {
            return Draw(icon.ToIconString(), id, tooltip, onClick, active, danger, true);
        }

        private static bool Draw(string text, string id, string tooltip, Action onClick, bool active, bool danger, bool icon)
        {
            var drawList = ImGui.GetWindowDrawList();
            var cursor = ImGui.GetCursorScreenPos();
            var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight());
            var clicked = ImGui.InvisibleButton($"##{id}", size);
            var hovered = ImGui.IsItemHovered();
            var held = ImGui.IsItemActive();
            var style = ImGui.GetStyle();

            if (hovered || held)
            {
                var color = danger
                    ? new Vector4(0.8f, 0.2f, 0.2f, held ? 1f : 0.75f)
                    : style.Colors[(int)(held ? ImGuiCol.ButtonActive : ImGuiCol.TabHovered)];
                drawList.AddRectFilled(cursor, cursor + size, ImGui.ColorConvertFloat4ToU32(color));
            }

            if (icon) ImGui.PushFont(UiBuilder.IconFont);

            var textSize = ImGui.CalcTextSize(text);
            var textPos = cursor + (size - textSize) * 0.5f;
            var textColor = active ? ImGui.GetColorU32(ImGuiColors.HealerGreen) : ImGui.GetColorU32(ImGuiCol.Text);
            drawList.AddText(textPos, textColor, text);

            if (icon) ImGui.PopFont();

            if (clicked)
                onClick();

            if (hovered)
                ImGui.SetTooltip(tooltip);

            return clicked;
        }
    }
}
