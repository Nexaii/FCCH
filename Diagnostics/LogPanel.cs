using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;

namespace FCCH.Diagnostics;

public static class LogPanel
{
    private static readonly List<LogEntry> Rows = new();

    private static string _search = string.Empty;
    private static string _builtSearch = string.Empty;
    private static int _builtVersion = -1;

    private static readonly string CopyGlyph = FontAwesomeIcon.Copy.ToIconString();
    private static readonly string ClearGlyph = FontAwesomeIcon.Minus.ToIconString();
    private static readonly string SaveGlyph = FontAwesomeIcon.Save.ToIconString();

    private static readonly string CopyLabel = CopyGlyph + "##logCopy";
    private static readonly string ClearLabel = ClearGlyph + "##logClear";
    private static readonly string SaveLabel = SaveGlyph + "##logSave";

    public static void Draw(FileDialogManager dialogs)
    {
        Rebuild();
        DrawToolbar(dialogs);
        ImGui.Separator();
        DrawRows();
    }

    private static void Rebuild()
    {
        var version = LogRing.Version;
        if (version == _builtVersion && string.Equals(_search, _builtSearch, StringComparison.Ordinal)) return;

        LogRing.Snapshot(Rows, _search);
        _builtVersion = version;
        _builtSearch = _search;
    }

    private static void DrawToolbar(FileDialogManager dialogs)
    {
        var style = ImGui.GetStyle();
        float spacing = style.ItemSpacing.X;
        var btn = new Vector2(MaxIconWidth() + style.FramePadding.X * 2f, ImGui.GetFrameHeight());

        float cluster = 3f * (btn.X + spacing);
        float searchWidth = ImGui.GetContentRegionAvail().X - cluster;
        if (searchWidth < 100f) searchWidth = 100f;

        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##logSearch", "Filter...", ref _search, 128);

        ImGui.SameLine();
        if (IconButton(CopyLabel, "Copy all", btn))
            ImGui.SetClipboardText(LogRing.CopyToString(Log.Header, _search));

        ImGui.SameLine();
        if (IconButton(ClearLabel, "Clear", btn))
            LogRing.Clear();

        ImGui.SameLine();
        if (IconButton(SaveLabel, "Save to file", btn))
        {
            dialogs.SaveFileDialog("Save log", ".log", $"{Log.PluginName}.log", ".log", (ok, chosen) =>
            {
                if (!ok) return;
                var error = LogFileSink.Save(chosen, LogRing.CopyToString(Log.Header, null));
                if (error != null) Log.Warning($"[Log] {error}");
            });
        }
    }

    private static bool IconButton(string label, string tooltip, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
        ImGui.PushFont(UiBuilder.IconFont);
        bool clicked = ImGui.Button(label, size);
        ImGui.PopFont();
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private static float MaxIconWidth()
    {
        ImGui.PushFont(UiBuilder.IconFont);
        float w = ImGui.CalcTextSize(CopyGlyph).X;
        w = Math.Max(w, ImGui.CalcTextSize(ClearGlyph).X);
        w = Math.Max(w, ImGui.CalcTextSize(SaveGlyph).X);
        ImGui.PopFont();
        return w;
    }

    private static void DrawRows()
    {
        if (Rows.Count == 0)
        {
            ImGui.TextDisabled(_search.Length == 0
                ? "No log entries yet."
                : $"No entries match \"{_search}\".");
            return;
        }

        if (!ImGui.BeginTable("DiagnosticsLog", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY))
            return;

        var padding = ImGui.GetStyle().FramePadding.X * 2;
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00:00:00 - 00:00:00").X + padding);
        ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Warning").X + padding);
        ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(Rows.Count);
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i < 0) continue;
                DrawRow(Rows[i]);
            }
        }

        clipper.End();
        clipper.Destroy();

        ImGui.EndTable();
    }

    private static void DrawRow(in LogEntry entry)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(entry.Count > 1
            ? $"{entry.First:HH:mm:ss} - {entry.Last:HH:mm:ss}"
            : entry.First.ToString("HH:mm:ss"));

        ImGui.TableNextColumn();
        ImGui.TextColored(LevelColor(entry.Level), LevelName(entry.Level));

        ImGui.TableNextColumn();
        if (entry.Category != null)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"[{entry.Category}]");
            ImGui.SameLine();
        }

        ImGui.TextUnformatted(entry.Message);
        if (entry.Count > 1)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, $"x{entry.Count}");
        }
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Verbose => "Verbose",
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        LogLevel.Warning => "Warning",
        _ => "Error",
    };

    private static Vector4 LevelColor(LogLevel level) => level switch
    {
        LogLevel.Verbose => ImGuiColors.DalamudGrey3,
        LogLevel.Debug => ImGuiColors.DalamudGrey,
        LogLevel.Info => ImGuiColors.DalamudWhite,
        LogLevel.Warning => ImGuiColors.DalamudYellow,
        _ => ImGuiColors.DalamudRed,
    };
}
