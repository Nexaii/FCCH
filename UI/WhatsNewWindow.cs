using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using FCCH.Common;

namespace FCCH.UI
{
    public class WhatsNewWindow : Window
    {
        private const float WrapWidth = 360f;
        private const float DetailIndent = 8f;

        public Action? OpenSettings { get; set; }

        public WhatsNewWindow()
            : base("FCCH - What's New###FCCHWhatsNew",
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking)
        {
        }

        public override void Draw()
        {
            float wrap = WrapWidth * ImGuiHelpers.GlobalScale;

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
            ImGui.TextDisabled(WhatsNew.ScopeNote);
            ImGui.PopTextWrapPos();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            for (int i = 0; i < WhatsNew.Highlights.Length; i++)
            {
                var entry = WhatsNew.Highlights[i];
                if (i > 0)
                    ImGui.Spacing();

                ImGui.TextUnformatted(entry.Title);

                float indent = DetailIndent * ImGuiHelpers.GlobalScale;
                ImGui.Indent(indent);
                foreach (var detail in entry.Details)
                {
                    if (detail.Length == 0)
                    {
                        ImGui.Spacing();
                        continue;
                    }

                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
                    ImGui.TextUnformatted("· " + detail);
                    ImGui.PopTextWrapPos();
                }
                ImGui.Unindent(indent);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
            ImGui.TextUnformatted(WhatsNew.SettingsHint);
            ImGui.PopTextWrapPos();

            ImGui.Spacing();
            if (ImGui.Button("Settings"))
            {
                OpenSettings?.Invoke();
                IsOpen = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Shoo!"))
                IsOpen = false;
        }
    }
}
