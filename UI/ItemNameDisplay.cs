using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FCCH.UI
{
    internal static class ItemNameDisplay
    {
        public static void Text(uint itemId, string fullName, Configuration configuration, string suffix = "", string? extraTooltip = null)
        {
            var name = ItemNameFormatter.Format(itemId, fullName, configuration.CompactItemNames, ImGui.GetContentRegionAvail().X, suffix);
            ImGui.TextUnformatted(name.VisibleName);
            DrawTooltip(name, extraTooltip);
        }

        public static void TextColored(uint itemId, string fullName, Vector4 color, Configuration configuration)
        {
            var name = ItemNameFormatter.Format(itemId, fullName, configuration.CompactItemNames, ImGui.GetContentRegionAvail().X);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(name.VisibleName);
            ImGui.PopStyleColor();
            DrawTooltip(name, null);
        }

        public static void TextDisabled(uint itemId, string fullName, Configuration configuration)
        {
            var name = ItemNameFormatter.Format(itemId, fullName, configuration.CompactItemNames, ImGui.GetContentRegionAvail().X);
            ImGui.TextDisabled(name.VisibleName);
            DrawTooltip(name, null);
        }

        private static void DrawTooltip(ItemDisplayName name, string? extraTooltip)
        {
            if (!ImGui.IsItemHovered() || (!name.ShowTooltip && string.IsNullOrWhiteSpace(extraTooltip)))
                return;

            if (name.ShowTooltip && !string.IsNullOrWhiteSpace(extraTooltip))
                ImGui.SetTooltip($"{name.FullName}\n{extraTooltip}");
            else if (name.ShowTooltip)
                ImGui.SetTooltip(name.FullName);
            else
                ImGui.SetTooltip(extraTooltip);
        }
    }
}
