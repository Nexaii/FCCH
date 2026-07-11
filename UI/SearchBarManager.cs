using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;
using FCCH.Managers;

namespace FCCH.UI
{
    public unsafe class SearchBarManager : IDisposable
    {
        private const uint FirstSlotNodeId = 23;
        private const int SlotCount = 50;
        private const uint FirstTabNodeIndex = 97;
        private const uint LastTabNodeIndex = 101;
        private const byte BrightAlpha = 255;
        private const byte DimAlpha = 80;

        private const float BarLogicalWidth = 220f;
        private const float PillHeight = 24f;
        private const long TooltipHideHoldMs = 250;
        private const GamepadButtons FocusButton = GamepadButtons.L1;

        private readonly ChestHelper _chestHelper;
        private readonly IGameGui _gameGui;
        private readonly IKeyState _keyState;
        private readonly Configuration _configuration;
        private readonly SearchBarWindow _window;
        private readonly IFontHandle _fontHandle;

        private readonly System.Collections.Generic.HashSet<InventoryType> _matchPages = new();
        private ulong _appliedSignature;
        private long _tooltipHideUntil;
        private bool _filterActive;
        private bool _disposed;

        public SearchBarManager(ChestHelper chestHelper, IGameGui gameGui, IKeyState keyState, Configuration configuration, WindowSystem windowSystem)
        {
            _chestHelper = chestHelper;
            _gameGui = gameGui;
            _keyState = keyState;
            _configuration = configuration;

            var inputTexture = Plugin.TextureProvider.GetFromGame("ui/uld/TextInputA.tex");
            _fontHandle = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));

            _window = new SearchBarWindow(inputTexture, _fontHandle);
            windowSystem.AddWindow(_window);
        }

        public void Update()
        {
            var addon = (AtkUnitBase*)_gameGui.GetAddonByName<AtkUnitBase>(Constants.FreeCompanyChestAddonName, 1);
            if (!_configuration.SearchBarEnabled || addon == null || !addon->IsVisible || addon->RootNode == null)
            {
                if (_filterActive && addon != null)
                {
                    ApplyFilter(addon, InventoryType.Invalid, "");
                    _filterActive = false;
                    _appliedSignature = 0;
                }
                _window.ClearQuery();
                _window.IsOpen = false;
                return;
            }

            if (CtrlFPressed() || ControllerFocusPressed())
                _window.FocusInput();

            _window.IsOpen = true;
            var barRect = PositionWindow(addon);
            if (barRect.HasValue && TooltipOverlaps(barRect.Value))
                _tooltipHideUntil = Environment.TickCount64 + TooltipHideHoldMs;
            if (Environment.TickCount64 < _tooltipHideUntil)
                _window.IsOpen = false;

            var page = _chestHelper.ChestManager.GetCurrentFCPage(addon);
            var query = _window.Query;
            var signature = ComputeSignature(addon, page, query);

            if (signature != _appliedSignature || (!_filterActive && !string.IsNullOrWhiteSpace(query)))
            {
                ApplyFilter(addon, page, query);
                _appliedSignature = signature;
                _filterActive = !string.IsNullOrWhiteSpace(query);
            }
        }

        private bool CtrlFPressed()
        {
            if (!_keyState.IsVirtualKeyValid(VirtualKey.CONTROL) || !_keyState.IsVirtualKeyValid(VirtualKey.F))
                return false;
            if (!_keyState[VirtualKey.CONTROL] || !_keyState[VirtualKey.F])
                return false;
            _keyState[VirtualKey.F] = false;
            return true;
        }

        private bool ControllerFocusPressed()
        {
            if (!_configuration.SearchBarControllerFocus)
                return false;
            return Plugin.GamepadState.Pressed(FocusButton) != 0;
        }

        private Vector4? PositionWindow(AtkUnitBase* addon)
        {
            float scale = addon->Scale;
            var header = addon->WindowHeaderCollisionNode;
            if (header == null)
            {
                _window.IsOpen = false;
                return null;
            }

            float headerWidth = header->Width * scale;
            float barWidth = MathF.Min(BarLogicalWidth * scale, headerWidth * 0.5f);
            float windowHeight = PillHeight * scale;

            var root = addon->RootNode;
            float centerX = root != null && root->Width > 0
                ? addon->X + root->Width / 2f * scale
                : addon->X + (header->X + header->Width / 2f) * scale;
            float centerY = addon->Y + (header->Y + header->Height / 2f) * scale;

            float x = centerX - barWidth / 2f;
            float y = centerY - windowHeight / 2f + 1f;

            _window.NextWidth = barWidth;
            _window.NextHeight = windowHeight;
            _window.NextScale = scale;
            _window.Position = new Vector2(x, y);
            _window.PositionCondition = ImGuiCond.Always;

            return new Vector4(x, y, barWidth, windowHeight);
        }

        private bool TooltipOverlaps(Vector4 bar)
        {
            return AddonOverlaps(Constants.ItemDetailAddonName, bar)
                || AddonOverlaps(Constants.TooltipAddonName, bar);
        }

        private bool AddonOverlaps(string addonName, Vector4 bar)
        {
            var tooltip = (AtkUnitBase*)_gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
            if (tooltip == null || !tooltip->IsVisible || tooltip->RootNode == null)
                return false;

            float scale = tooltip->Scale;
            float tx = tooltip->X;
            float ty = tooltip->Y;
            float tw = tooltip->RootNode->Width * scale;
            float th = tooltip->RootNode->Height * scale;
            if (tw <= 0f || th <= 0f)
                return false;

            return bar.X < tx + tw && tx < bar.X + bar.Z
                && bar.Y < ty + th && ty < bar.Y + bar.W;
        }

        private ulong ComputeSignature(AtkUnitBase* addon, InventoryType page, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return 0;
            if (page < InventoryType.FreeCompanyPage1 || page > InventoryType.FreeCompanyPage5)
                return 0;

            var container = _chestHelper.ChestManager.GetContainer(page);
            if (container == null)
                return 0;

            ulong hash = 14695981039346656037UL;
            foreach (char c in query)
                hash = (hash ^ c) * 1099511628211UL;
            for (int i = 0; i < SlotCount; i++)
            {
                var item = container->GetInventorySlot(i);
                uint id = item != null ? item->ItemId : 0;
                uint qty = item != null ? (uint)item->Quantity : 0;
                hash = (hash ^ id) * 1099511628211UL;
                hash = (hash ^ qty) * 1099511628211UL;
            }
            return hash;
        }

        private void ApplyFilter(AtkUnitBase* addon, InventoryType page, string query)
        {
            bool empty = string.IsNullOrWhiteSpace(query);
            bool pageIsItems = page >= InventoryType.FreeCompanyPage1 && page <= InventoryType.FreeCompanyPage5;
            var container = pageIsItems ? _chestHelper.ChestManager.GetContainer(page) : null;

            for (uint i = 0; i < SlotCount; i++)
            {
                var node = addon->GetNodeById(FirstSlotNodeId + i);
                if (node == null)
                    continue;

                byte alpha = BrightAlpha;
                if (!empty && container != null)
                {
                    var item = container->GetInventorySlot((int)i);
                    bool filled = item != null && item->ItemId != 0;
                    if (filled)
                    {
                        bool match = _chestHelper.GetItemName(item->ItemId).Contains(query, StringComparison.OrdinalIgnoreCase);
                        alpha = match ? BrightAlpha : DimAlpha;
                    }
                }
                node->Color.A = alpha;
            }

            HighlightTabs(addon, empty, query);
        }

        private void HighlightTabs(AtkUnitBase* addon, bool empty, string query)
        {
            _matchPages.Clear();
            if (!empty)
            {
                foreach (var slot in _chestHelper.ChestManager.CachedItems)
                {
                    if (slot.ItemId == 0 || _matchPages.Contains(slot.Page))
                        continue;
                    if (_chestHelper.GetItemName(slot.ItemId).Contains(query, StringComparison.OrdinalIgnoreCase))
                        _matchPages.Add(slot.Page);
                }
            }

            for (uint index = FirstTabNodeIndex; index <= LastTabNodeIndex; index++)
            {
                if (index >= addon->UldManager.NodeListCount)
                    continue;
                var tab = addon->UldManager.NodeList[index];
                if (tab == null)
                    continue;

                var page = (InventoryType)((int)InventoryType.FreeCompanyPage1 + (int)(LastTabNodeIndex - index));
                tab->Color.A = empty || _matchPages.Contains(page) ? BrightAlpha : DimAlpha;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            var addon = (AtkUnitBase*)_gameGui.GetAddonByName<AtkUnitBase>(Constants.FreeCompanyChestAddonName, 1);
            if (_filterActive && addon != null && addon->IsVisible)
                ApplyFilter(addon, InventoryType.Invalid, "");

            _fontHandle.Dispose();
        }
    }

    public class SearchBarWindow : Window
    {
        private const float TileSize = 24f;
        private const float CornerInset = 10f;
        private static readonly Vector2 BackgroundTileOrigin = new(24f, 0f);
        private static readonly Vector2 FocusTileOrigin = new(0f, 0f);

        private readonly ISharedImmediateTexture _inputTexture;
        private readonly IFontHandle _fontHandle;

        private string _query = "";
        private bool _focusRequested;

        public float NextWidth { get; set; }
        public float NextHeight { get; set; }
        public float NextScale { get; set; } = 1f;
        public string Query => _query;

        public SearchBarWindow(ISharedImmediateTexture inputTexture, IFontHandle fontHandle) : base("FCCH Search", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove)
        {
            _inputTexture = inputTexture;
            _fontHandle = fontHandle;
            RespectCloseHotkey = false;
        }

        public void FocusInput() => _focusRequested = true;

        public void ClearQuery() => _query = "";

        public override void PreDraw()
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        }

        public override void PostDraw()
        {
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor();
        }

        public override void Draw()
        {
            float width = NextWidth > 0 ? NextWidth : 220f;
            float height = NextHeight > 0 ? NextHeight : TileSize * NextScale;

            IDalamudTextureWrap? wrap = null;
            bool textured = _fontHandle.Available && _inputTexture.TryGetWrap(out wrap, out _);
            if (textured)
                DrawNativePill(wrap!, width, height);
            else
                DrawFallbackPill(width, height);
        }

        private void DrawNativePill(IDalamudTextureWrap wrap, float width, float height)
        {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var size = new Vector2(width, height);

            DrawNinegrid(drawList, wrap, BackgroundTileOrigin, origin, size, 0xFFFFFFFF);

            bool inputActive;
            using (_fontHandle.Push())
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(CornerInset * NextScale, (height - ImGui.GetFontSize()) / 2f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0f, 0f));

                inputActive = DrawInput(width);

                ImGui.PopStyleColor();
                ImGui.PopStyleVar(3);
            }

            if (inputActive)
                DrawNinegrid(drawList, wrap, FocusTileOrigin, origin, size, 0xFFFFFFFF);

            BlurOnOutsideClick(inputActive);
        }

        private void DrawFallbackPill(float width, float height)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, (height - ImGui.GetFontSize()) / 2f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, ImGui.GetColorU32(ImGuiCol.FrameBg));
            ImGui.PushStyleColor(ImGuiCol.Border, ImGuiColors.DalamudWhite);

            bool inputActive = DrawInput(width);

            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(3);

            BlurOnOutsideClick(inputActive);
        }

        private bool DrawInput(float width)
        {
            if (_focusRequested)
            {
                ImGui.SetKeyboardFocusHere();
                _focusRequested = false;
            }

            ImGui.SetNextItemWidth(width);
            ImGui.InputTextWithHint("##fcchsearch", "Search or Ctrl+F", ref _query, 100, ImGuiInputTextFlags.AutoSelectAll);
            return ImGui.IsItemActive();
        }

        private void BlurOnOutsideClick(bool inputActive)
        {
            if (inputActive && !ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                ImGui.SetWindowFocus("");
        }

        private void DrawNinegrid(ImDrawListPtr drawList, IDalamudTextureWrap wrap, Vector2 tileOrigin, Vector2 origin, Vector2 size, uint color)
        {
            var texSize = wrap.Size;
            float capPx = CornerInset * NextScale;
            float midWidth = MathF.Max(0f, size.X - capPx * 2f);

            float uTileLeft = tileOrigin.X / texSize.X;
            float uTileRight = (tileOrigin.X + TileSize) / texSize.X;
            float uTileCapLeft = (tileOrigin.X + CornerInset) / texSize.X;
            float uTileCapRight = (tileOrigin.X + TileSize - CornerInset) / texSize.X;
            float vTop = tileOrigin.Y / texSize.Y;
            float vBottom = (tileOrigin.Y + TileSize) / texSize.Y;

            float xLeft = origin.X;
            float xMid = origin.X + capPx;
            float xRight = origin.X + capPx + midWidth;
            float yTop = origin.Y;
            float yBottom = origin.Y + size.Y;

            drawList.AddImage(wrap.Handle, new Vector2(xLeft, yTop), new Vector2(xMid, yBottom), new Vector2(uTileLeft, vTop), new Vector2(uTileCapLeft, vBottom), color);
            drawList.AddImage(wrap.Handle, new Vector2(xMid, yTop), new Vector2(xRight, yBottom), new Vector2(uTileCapLeft, vTop), new Vector2(uTileCapRight, vBottom), color);
            drawList.AddImage(wrap.Handle, new Vector2(xRight, yTop), new Vector2(xRight + capPx, yBottom), new Vector2(uTileCapRight, vTop), new Vector2(uTileRight, vBottom), color);
        }
    }
}
