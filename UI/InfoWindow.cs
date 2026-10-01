using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using FCCH.Common;
using FCCH.Diagnostics;

namespace FCCH.UI
{
    public enum InfoTab
    {
        Changelog,
        Logs,
        Credits
    }

    public class InfoWindow : Window
    {
        public const string SupportUrl = "https://www.patreon.com/c/Nexairi";
        public const string IssuesUrl = "https://github.com/Nexaii/FCCH/issues";

        private static readonly string[] KindLabels = { "Fixed", "Added", "Changed" };
        private const int VersionsPerPage = 10;

        private readonly FileDialogManager _fileDialogManager;
        private readonly bool[] _versionOpen;
        private readonly string[] _creditRoleCaps;
        private InfoTab? _pendingTab;
        private int _changelogPage;
        private float _kindColumnWidth;
        private float _separatorWidth;

        private static string WindowTitle()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? "FCCH###FCCHInfo"
                : $"FCCH {version.Major}.{version.Minor}.{version.Build}.{version.Revision}###FCCHInfo";
        }

        public InfoWindow()
            : base(WindowTitle())
        {
            _fileDialogManager = new FileDialogManager();

            _versionOpen = new bool[Changelog.Versions.Length];
            if (_versionOpen.Length > 0) _versionOpen[0] = true;

            _creditRoleCaps = new string[Credits.Rows.Length];
            for (int i = 0; i < Credits.Rows.Length; i++)
                _creditRoleCaps[i] = Credits.Rows[i].Role.ToUpperInvariant();

            Size = new Vector2(640, 460);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void OpenTo(InfoTab tab)
        {
            _pendingTab = tab;
            IsOpen = true;
        }

        public override void PreDraw()
        {
            _fileDialogManager.Draw();
        }

        public override void Draw()
        {
            if (!ImGui.BeginTabBar("FCCHInfoTabs", ImGuiTabBarFlags.None)) return;

            DrawTab("Changelog", InfoTab.Changelog, DrawChangelogTab);
            DrawTab("Logs", InfoTab.Logs, DrawLogsTab);
            DrawTab("Credits", InfoTab.Credits, DrawCreditsTab);

            ImGui.EndTabBar();
            _pendingTab = null;
        }

        private void DrawTab(string label, InfoTab tab, System.Action body)
        {
            var flags = _pendingTab == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem(label, flags))
            {
                body();
                ImGui.EndTabItem();
            }
        }

        private void DrawChangelogTab()
        {
            _kindColumnWidth = 0f;
            foreach (var label in KindLabels)
                _kindColumnWidth = System.Math.Max(_kindColumnWidth, ImGui.CalcTextSize(label).X);
            _separatorWidth = ImGui.CalcTextSize("·").X;

            int total = Changelog.Versions.Length;
            int pageCount = System.Math.Max(1, (total + VersionsPerPage - 1) / VersionsPerPage);
            _changelogPage = System.Math.Clamp(_changelogPage, 0, pageCount - 1);

            int start = _changelogPage * VersionsPerPage;
            int end = System.Math.Min(start + VersionsPerPage, total);

            float footerHeight = pageCount > 1
                ? ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y
                : 0f;

            if (ImGui.BeginChild("ChangelogVersions", new Vector2(0f, -footerHeight), false))
            {
                ImGui.Dummy(new Vector2(0f, 2f));
                for (int i = start; i < end; i++)
                    DrawVersionFold(i);
            }
            ImGui.EndChild();

            if (pageCount > 1)
                DrawChangelogPager(pageCount);
        }

        private void DrawChangelogPager(int pageCount)
        {
            var style = ImGui.GetStyle();
            ImGui.Separator();

            bool hasPrev = _changelogPage > 0;
            bool hasNext = _changelogPage < pageCount - 1;

            if (!hasPrev) ImGui.BeginDisabled();
            if (ImGui.ArrowButton("clPrev", ImGuiDir.Left) && hasPrev) _changelogPage--;
            if (!hasPrev) ImGui.EndDisabled();

            string label = $"Page {_changelogPage + 1} of {pageCount}";
            float labelWidth = ImGui.CalcTextSize(label).X;
            float center = (ImGui.GetContentRegionAvail().X - labelWidth) * 0.5f;

            ImGui.SameLine(0f, center > 0f ? center : style.ItemSpacing.X);
            ImGui.AlignTextToFramePadding();
            ImGui.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();

            float nextX = ImGui.GetWindowContentRegionMax().X - ImGui.GetFrameHeight();
            ImGui.SameLine();
            ImGui.SetCursorPosX(nextX);
            if (!hasNext) ImGui.BeginDisabled();
            if (ImGui.ArrowButton("clNext", ImGuiDir.Right) && hasNext) _changelogPage++;
            if (!hasNext) ImGui.EndDisabled();
        }

        private static Vector2 BeginBand()
        {
            var start = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSplit(2);
            drawList.ChannelsSetCurrent(1);
            ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y * 0.5f));
            return start;
        }

        private static void EndBand(Vector2 start, float width)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y * 0.5f));

            var drawList = ImGui.GetWindowDrawList();
            float end = ImGui.GetCursorScreenPos().Y;
            drawList.ChannelsSetCurrent(0);
            drawList.AddRectFilled(start, new Vector2(start.X + width, end),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.03f)), 4f);
            drawList.ChannelsMerge();
        }

        private void DrawKindGroup(Changelog.Entry[] entries, int start, int count)
        {
            var style = ImGui.GetStyle();
            float startX = ImGui.GetCursorPosX();
            float bodyX = startX + _kindColumnWidth + _separatorWidth + style.ItemInnerSpacing.X * 4f;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[start + i];

                if (i == 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
                    ImGui.TextUnformatted(KindLabels[(int)entry.Kind]);
                    ImGui.PopStyleColor();
                    ImGui.SameLine(0f, 0f);
                }

                ImGui.SetCursorPosX(startX + _kindColumnWidth + style.ItemInnerSpacing.X * 2f);
                ImGui.TextUnformatted("·");

                ImGui.SameLine(0f, 0f);
                ImGui.SetCursorPosX(bodyX);
                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(entry.Text);
                ImGui.PopTextWrapPos();
            }
        }

        private void DrawVersionFold(int index)
        {
            var version = Changelog.Versions[index];
            var style = ImGui.GetStyle();
            var drawList = ImGui.GetWindowDrawList();

            float width = ImGui.GetContentRegionAvail().X;
            float height = ImGui.GetFrameHeight();
            float fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();

            ImGui.PushID(version.Label);
            ImGui.InvisibleButton("fold", new Vector2(width, height));
            bool hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked()) _versionOpen[index] = !_versionOpen[index];
            ImGui.PopID();

            bool open = _versionOpen[index];

            var bandColor = open
                ? new Vector4(0.30f, 0.32f, 0.44f, 0.55f)
                : (index % 2 == 0 ? new Vector4(1f, 1f, 1f, 0.04f) : new Vector4(0f, 0f, 0f, 0f));

            if (hovered) bandColor = style.Colors[(int)ImGuiCol.HeaderHovered];

            if (bandColor.W > 0f)
                drawList.AddRectFilled(origin, new Vector2(origin.X + width, origin.Y + height),
                    ImGui.ColorConvertFloat4ToU32(bandColor), 4f);

            uint caretColor = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.TextDisabled]);
            float caretCenterX = origin.X + style.FramePadding.X + fontSize * 0.5f;
            float caretCenterY = origin.Y + height * 0.5f;
            float arrow = fontSize * 0.42f;
            if (open)
                drawList.AddTriangleFilled(
                    new Vector2(caretCenterX - arrow * 0.55f, caretCenterY - arrow * 0.35f),
                    new Vector2(caretCenterX + arrow * 0.55f, caretCenterY - arrow * 0.35f),
                    new Vector2(caretCenterX, caretCenterY + arrow * 0.5f), caretColor);
            else
                drawList.AddTriangleFilled(
                    new Vector2(caretCenterX - arrow * 0.35f, caretCenterY - arrow * 0.55f),
                    new Vector2(caretCenterX - arrow * 0.35f, caretCenterY + arrow * 0.55f),
                    new Vector2(caretCenterX + arrow * 0.5f, caretCenterY), caretColor);

            uint accentColor = ImGui.ColorConvertFloat4ToU32(ImGuiColors.DalamudOrange);
            float textX = origin.X + style.FramePadding.X + fontSize + style.ItemInnerSpacing.X;
            float textY = origin.Y + style.FramePadding.Y;
            drawList.AddText(new Vector2(textX, textY), accentColor, version.Label);

            float dateX = textX + ImGui.CalcTextSize(version.Label).X + style.ItemInnerSpacing.X * 2f;
            drawList.AddText(new Vector2(dateX, textY),
                ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.TextDisabled]), version.Date);

            ImGui.Separator();

            if (open)
            {
                float indent = style.FramePadding.X + fontSize;
                var bodyStart = BeginBand();

                ImGui.Indent(indent);
                for (int i = 0; i < version.Entries.Length;)
                {
                    int run = 1;
                    while (i + run < version.Entries.Length
                        && version.Entries[i + run].Kind == version.Entries[i].Kind) run++;
                    DrawKindGroup(version.Entries, i, run);
                    i += run;
                }
                ImGui.Unindent(indent);

                EndBand(bodyStart, width);
            }

            ImGui.Dummy(new Vector2(0f, style.ItemSpacing.Y));
        }

        private void DrawLogsTab()
        {
            LogPanel.Draw(_fileDialogManager);
        }

        private void DrawCreditsTab()
        {
            var style = ImGui.GetStyle();
            ImGui.Dummy(new Vector2(0f, 2f));

            const float sidebarWidth = 190f;
            if (ImGui.BeginChild("CreditsSidebar", new Vector2(sidebarWidth, 0f), false))
            {
                DrawPluginIcon(sidebarWidth - style.WindowPadding.X * 2f);
                ImGui.Dummy(new Vector2(0f, style.ItemSpacing.Y * 2f));

                float buttonWidth = ImGui.GetContentRegionAvail().X;
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                if (ImGui.Button("Support the plugin", new Vector2(buttonWidth, 0f)))
                    OpenLink(SupportUrl);
                if (ImGui.Button("Report an issue", new Vector2(buttonWidth, 0f)))
                    OpenLink(IssuesUrl);
                ImGui.PopStyleColor();
            }
            ImGui.EndChild();

            ImGui.SameLine();
            if (ImGui.BeginChild("CreditsBody", new Vector2(0f, 0f), false))
            {
                for (int i = 0; i < Credits.Rows.Length; i++)
                    DrawCreditCard(i);
            }
            ImGui.EndChild();
        }

        private void DrawPluginIcon(float maxWidth)
        {
            var icon = Plugin.TextureProvider
                .GetFromFile(System.IO.Path.Combine(
                    Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Assets", "icon.png"))
                .GetWrapOrDefault();

            if (icon == null) return;

            float scale = System.Math.Min(1f, maxWidth / icon.Size.X);
            var drawSize = new Vector2(icon.Size.X * scale, icon.Size.Y * scale);

            float offset = (ImGui.GetContentRegionAvail().X - drawSize.X) * 0.5f;
            if (offset > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
            ImGui.Image(icon.Handle, drawSize);
        }

        public static void OpenLink(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                    Verb = string.Empty,
                });
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[InfoWindow] Failed to open {url}: {ex.Message}");
            }
        }

        private void DrawCreditCard(int index)
        {
            var row = Credits.Rows[index];
            var style = ImGui.GetStyle();

            if (index > 0)
            {
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0f, style.ItemSpacing.Y));
            }

            ImGui.TextColored(ImGuiColors.DalamudOrange, _creditRoleCaps[index]);

            float width = ImGui.GetContentRegionAvail().X;
            float indent = style.FramePadding.X + ImGui.GetFontSize();
            var bandStart = BeginBand();

            ImGui.Indent(indent);
            ImGui.TextUnformatted(row.Who);
            ImGui.PushTextWrapPos(0f);
            ImGui.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextUnformatted(row.Note);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            ImGui.Unindent(indent);

            EndBand(bandStart, width);

            ImGui.Dummy(new Vector2(0f, style.ItemSpacing.Y));
        }
    }
}
