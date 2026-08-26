using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Lumina.Excel.Sheets;
using FCCH.GameData;
using FCCH.Managers;
using FCCH.Common;

namespace FCCH.UI
{
    public class CustomTab
    {
        private readonly ChestHelper _helper;
        private readonly Configuration _configuration;
        private const string NumericColumnSample = "12345678";

        private string _searchFilter = "";
        private string _listFilter = "";
        private bool _showClearConfirm = false;

        private Item[] _searchResults = Array.Empty<Item>();
        private string _lastSearch = "";

        private readonly List<CategoryResolver.CategoryMatch> _categoryResults = new();
        private CategoryResolver.CategoryMatch _pendingCategory;
        private CategoryResolver.CategoryMatch? _clickedCategory;
        private bool _showCategoryConfirm = false;

        private readonly List<(WithdrawItem Item, int Index, string Name)> _visible = new();
        private List<WithdrawItem> _viewSource = new();
        private string _viewFilter = "";
        private int _viewCount = -1;
        private string _countText = "";

        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        private readonly UndoStack<WithdrawItem> _undo = new(x => x.Clone());
        private readonly BulkPaint _paint = new();
        private CustomItemMode _paintMode;
        private bool _paintMax;
        private int _paintCount;

        private int _qtyBefore;
        private bool _qtyCaptured;

        private int _modeRevision;
        private int _viewRevision = -1;
        private CustomItemMode _headerMode;
        private bool _headerModeMixed;
        private bool _headerMax;
        private bool _headerMaxMixed;
        private bool _showHeaderModeConfirm;
        private bool _showHeaderMaxConfirm;

        public CustomTab(ChestHelper helper, Configuration configuration)
        {
            _helper = helper;
            _configuration = configuration;
        }

        public void Draw()
        {
            DrawPresets();
            DrawSearchBox();
            ImGui.Separator();

            int itemCount = _configuration.WithdrawItems.Count;
            _paint.BeginFrame();
            RefreshView();

            DrawListHeader(itemCount);

            if (ImGui.BeginChild("CustomItemsList", new Vector2(0, ImGui.GetContentRegionAvail().Y), true))
            {
                if (itemCount == 0)
                {
                    ImGui.TextDisabled("No items in custom list. Use the search box above to add items.");
                }
                else if (_visible.Count == 0)
                {
                    ImGui.TextDisabled($"No item in your list matches \"{_listFilter}\". Use the search box above to add it.");
                }
                else if (ImGui.BeginTable("CustomItemsTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    float numericColumnWidth = ImGui.CalcTextSize(NumericColumnSample).X + ImGui.GetStyle().FramePadding.X * 2;

                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, numericColumnWidth);
                    ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, numericColumnWidth);
                    ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                    ImGui.TableSetupColumn("Max", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                    ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                    DrawTableHeaders();

                    var clipper = ImGui.ImGuiListClipper();
                    clipper.Begin(_visible.Count);
                    while (clipper.Step())
                    {
                        for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            if (i < 0) continue;

                            var row = _visible[i];
                            ImGui.PushID($"c_item_{row.Index}");
                            DrawItemRow(row.Item, row.Index, row.Name, i);
                            ImGui.PopID();
                        }
                    }
                    clipper.End();
                    clipper.Destroy();

                    ImGui.EndTable();

                    FinishPaint();
                }
            }
            ImGui.EndChild();

            DrawSavePresetModal();

            if (_showCategoryConfirm)
            {
                var body = $"Add all {_pendingCategory.Count:N0} items in {_pendingCategory.Name} to your custom list?\n\nThey will be set to Withdraw, quantity 1.";
                if (ListChrome.DrawConfirm("Add category", body, $"Add {_pendingCategory.Count:N0}", ref _showCategoryConfirm))
                    AddCategory(_pendingCategory);
            }

            if (_showHeaderModeConfirm)
            {
                var target = GetModeLabel(NextHeaderMode());
                if (ListChrome.DrawConfirm("Set mode", $"Set all {_visible.Count:N0} shown items to {target}?", $"Set {_visible.Count:N0}", ref _showHeaderModeConfirm))
                    ApplyHeaderMode();
            }

            if (_showHeaderMaxConfirm)
            {
                var target = NextHeaderMax() ? "Max" : "a set quantity";
                if (ListChrome.DrawConfirm("Set max", $"Set all {_visible.Count:N0} shown items to {target}?", $"Set {_visible.Count:N0}", ref _showHeaderMaxConfirm))
                    ApplyHeaderMax();
            }

            if (ListChrome.DrawConfirm("Clear Custom List", $"Remove all {itemCount} items from the custom list?", "Clear List", ref _showClearConfirm))
            {
                _undo.Capture(_configuration.WithdrawItems, $"Clear list ({itemCount:N0} items)");
                _configuration.WithdrawItems.Clear();
                _listFilter = "";
                _configuration.Save();
            }
        }

        private void Undo()
        {
            _configuration.WithdrawItems = _undo.Pop();
            _configuration.Save();
        }

        private void RefreshView()
        {
            var source = _configuration.WithdrawItems;

            if (ReferenceEquals(_viewSource, source) && _viewCount == source.Count && _viewFilter == _listFilter && _viewRevision == _modeRevision)
                return;

            _viewSource = source;
            _viewCount = source.Count;
            _viewFilter = _listFilter;
            _viewRevision = _modeRevision;

            _visible.Clear();
            for (var i = 0; i < source.Count; i++)
            {
                var name = _helper.GetItemName(source[i].ItemId);
                if (_listFilter.Length > 0 && !name.Contains(_listFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                _visible.Add((source[i], i, name));
            }

            _visible.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _countText = ListChrome.CountText(_visible.Count, _viewCount);
            RefreshHeaderState();
        }

        private void DrawTableHeaders()
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            ImGui.TableSetColumnIndex(0);
            ImGui.TableHeader("Item");

            ImGui.TableSetColumnIndex(1);
            ImGui.TableHeader("Qty");

            ImGui.TableSetColumnIndex(2);
            ImGui.TableHeader("Have");

            ImGui.TableSetColumnIndex(3);
            ImGui.TableHeader("Mode");
            if (ImGui.IsItemClicked())
                RequestHeaderMode();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(BulkHeader.Tooltip(_visible.Count, _viewCount, GetModeLabel(NextHeaderMode()), _headerModeMixed));

            ImGui.TableSetColumnIndex(4);
            ImGui.TableHeader("Max");
            if (ImGui.IsItemClicked())
                RequestHeaderMax();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(BulkHeader.Tooltip(_visible.Count, _viewCount, NextHeaderMax() ? "Max" : "a set quantity", _headerMaxMixed));

            ImGui.TableSetColumnIndex(5);
            ImGui.TableHeader("");
        }

        private CustomItemMode NextHeaderMode()
        {
            return _headerModeMixed ? CustomItemMode.Withdraw : WithdrawItem.NextMode(_headerMode);
        }

        private bool NextHeaderMax()
        {
            return _headerMaxMixed || !_headerMax;
        }

        private void RequestHeaderMode()
        {
            if (_visible.Count > BulkHeader.ConfirmThreshold)
                _showHeaderModeConfirm = true;
            else
                ApplyHeaderMode();
        }

        private void RequestHeaderMax()
        {
            if (_visible.Count > BulkHeader.ConfirmThreshold)
                _showHeaderMaxConfirm = true;
            else
                ApplyHeaderMax();
        }

        private void ApplyHeaderMode()
        {
            var target = NextHeaderMode();
            _undo.Capture(_configuration.WithdrawItems, "");

            var changed = 0;
            for (var i = 0; i < _visible.Count; i++)
            {
                if (_visible[i].Item.Mode == target)
                    continue;

                _visible[i].Item.Mode = target;
                changed++;
            }

            if (changed == 0)
            {
                _undo.Discard();
                return;
            }

            _undo.Relabel($"Set {changed:N0} items to {GetModeLabel(target)}");
            _modeRevision++;
            _configuration.Save();
        }

        private void ApplyHeaderMax()
        {
            var target = NextHeaderMax();
            _undo.Capture(_configuration.WithdrawItems, "");

            var changed = 0;
            for (var i = 0; i < _visible.Count; i++)
            {
                if (_visible[i].Item.AlwaysMax == target)
                    continue;

                _visible[i].Item.AlwaysMax = target;
                changed++;
            }

            if (changed == 0)
            {
                _undo.Discard();
                return;
            }

            _undo.Relabel($"Set {changed:N0} items to {(target ? "Max" : "a set quantity")}");
            _modeRevision++;
            _configuration.Save();
        }

        private void RefreshHeaderState()
        {
            _headerModeMixed = false;
            _headerMaxMixed = false;

            if (_visible.Count == 0)
                return;

            _headerMode = _visible[0].Item.Mode;
            _headerMax = _visible[0].Item.AlwaysMax;

            for (var i = 1; i < _visible.Count; i++)
            {
                if (_visible[i].Item.Mode != _headerMode)
                    _headerModeMixed = true;

                if (_visible[i].Item.AlwaysMax != _headerMax)
                    _headerMaxMixed = true;

                if (_headerModeMixed && _headerMaxMixed)
                    return;
            }
        }

        private void DrawListHeader(int total)
        {
            if (!ImGui.BeginTable("##customHeader", 4))
                return;

            ImGui.TableSetupColumn("##count", ImGuiTableColumnFlags.WidthFixed, ListChrome.CountColumnWidth());
            ImGui.TableSetupColumn("##filter", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##undo", ImGuiTableColumnFlags.WidthFixed, ListChrome.ClearButtonWidth);
            ImGui.TableSetupColumn("##clear", ImGuiTableColumnFlags.WidthFixed, ListChrome.ClearButtonWidth);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ListChrome.DrawCount(_countText);

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##customListFilter", "Filter your list...", ref _listFilter, 64);

            ImGui.TableNextColumn();
            if (ListChrome.DrawUndoButton(_undo))
                Undo();

            ImGui.TableNextColumn();
            if (total > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                if (ImGui.Button("Clear List", new Vector2(-1, 0)))
                    _showClearConfirm = true;
                ImGui.PopStyleColor();
            }

            ImGui.EndTable();
        }

        private void DrawSearchBox()
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.2f, 0.2f, 0.2f, 1f));

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##addItemSearch", "Search items or categories...", ImGuiComboFlags.HeightLarge))
            {
                ImGui.PopStyleColor(2);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##searchIn", ref _searchFilter, 64);
                UpdateSearchCache();

                if (_categoryResults.Count > 0)
                {
                    foreach (var category in _categoryResults)
                    {
                        if (ImGui.Selectable($"{category.Name}##cat{category.Id}", false))
                        {
                            _clickedCategory = category;
                            ImGui.CloseCurrentPopup();
                        }

                        ListChrome.DrawTrailingLabel($"add all {category.Count:N0}");
                    }

                    ImGui.Separator();
                }

                if (_searchResults.Length > 0)
                {
                    foreach (var item in _searchResults)
                    {
                        bool alreadyHeld = _configuration.WithdrawItems.Any(x => x.ItemId == item.RowId);

                        if (ImGui.Selectable(item.Name.ToString(), false))
                        {
                            AddItem(item);
                            ImGui.CloseCurrentPopup();
                        }

                        if (alreadyHeld)
                            ListChrome.DrawInListMarker();
                    }
                }
                else if (_categoryResults.Count == 0)
                {
                    ImGui.TextDisabled("No results found");
                }
                ImGui.EndCombo();
            }
            else
            {
                ImGui.PopStyleColor(2);
            }

            if (!_clickedCategory.HasValue)
                return;

            var clicked = _clickedCategory.Value;
            _clickedCategory = null;

            if (clicked.Count > BulkHeader.ConfirmThreshold)
            {
                _pendingCategory = clicked;
                _showCategoryConfirm = true;
                return;
            }

            AddCategory(clicked);
        }

        private void UpdateSearchCache()
        {
            if (_searchFilter == _lastSearch)
                return;

            _lastSearch = _searchFilter;
            CategoryResolver.Match(_searchFilter, _categoryResults);
            var sheet = Plugin.Data.GetExcelSheet<Item>();

            if (sheet != null && !string.IsNullOrEmpty(_searchFilter))
            {
                _searchResults = sheet.Where(i =>
                {
                    var name = i.Name.ToString();
                    if (string.IsNullOrEmpty(name)) return false;
                    if (!name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) return false;
                    return ItemListEligibility.IsAllowed(i);
                }).Take(20).ToArray();
            }
            else
            {
                _searchResults = Array.Empty<Item>();
            }
        }

        private void AddItem(Item item)
        {
            AddItems(new[] { item.RowId });
            ClearSearch();
        }

        private int AddItems(IReadOnlyList<uint> itemIds)
        {
            var held = new HashSet<uint>();
            foreach (var existing in _configuration.WithdrawItems)
                held.Add(existing.ItemId);

            var added = 0;
            foreach (var itemId in itemIds)
            {
                if (!held.Add(itemId))
                    continue;

                _configuration.WithdrawItems.Add(new WithdrawItem { ItemId = itemId, Quantity = 1 });
                added++;
            }

            if (added > 0)
            {
                _configuration.WithdrawItems.Sort((a, b) => string.Compare(_helper.GetItemName(a.ItemId), _helper.GetItemName(b.ItemId), StringComparison.OrdinalIgnoreCase));
                _configuration.Save();
            }

            return added;
        }

        private void ClearSearch()
        {
            _searchFilter = "";
            _listFilter = "";
            UpdateSearchCache();
        }

        private void AddCategory(CategoryResolver.CategoryMatch category)
        {
            var ids = CategoryResolver.GetItemIds(category.Id);
            _undo.Capture(_configuration.WithdrawItems, "");
            var added = AddItems(ids);
            var skipped = ids.Count - added;

            if (added == 0)
                _undo.Discard();
            else
                _undo.Relabel($"Add {added:N0} items from {category.Name}");

            if (added == 0)
                Common.ChatHelper.Info($"All {ids.Count:N0} items from {category.Name} were already in your list.");
            else if (skipped > 0)
                Common.ChatHelper.Info($"Added {added:N0} items from {category.Name} ({skipped:N0} already in list).");
            else
                Common.ChatHelper.Info($"Added {added:N0} items from {category.Name}.");

            ClearSearch();
        }

        private void DrawItemRow(WithdrawItem item, int index, string itemName, int visibleRow)
        {
            ImGui.TableNextRow();

            long chestHave = _helper.GetItemCountInChest(item.ItemId);
            long inventoryHave = _helper.GetItemCountInPlayerInventory(item.ItemId);
            bool insufficient = IsInsufficient(item, chestHave, inventoryHave);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (insufficient)
            {
                ItemNameDisplay.TextColored(item.ItemId, itemName, ImGuiColors.DalamudOrange, _configuration);
            }
            else
            {
                ItemNameDisplay.Text(item.ItemId, itemName, _configuration);
            }

            ImGui.TableNextColumn();
            int maxLimit = 249750;
            var sheetItem = Plugin.Data.GetExcelSheet<Item>()?.GetRow(item.ItemId);
            if (sheetItem != null && sheetItem.Value.StackSize > 999) maxLimit = 9999;

            DrawQuantity(item, maxLimit, itemName);

            ImGui.TableNextColumn();
            DrawHave(item, chestHave, inventoryHave, insufficient);

            ImGui.TableNextColumn();
            DrawModeButton(item, visibleRow);

            ImGui.TableNextColumn();
            DrawMaxToggle(item, index, visibleRow);

            ImGui.TableNextColumn();
            DrawDeleteButton(index);
        }

        private void DrawQuantity(WithdrawItem item, int maxLimit, string itemName)
        {
            if (item.AlwaysMax)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Max");
                return;
            }

            int qty = item.Quantity;
            ImGui.SetNextItemWidth(-1);
            var edited = ImGui.InputInt("##qty", ref qty, 0);

            if (ImGui.IsItemActivated())
            {
                _qtyBefore = item.Quantity;
                _qtyCaptured = false;
            }

            if (edited)
            {
                qty = Math.Max(1, qty);
                qty = Math.Min(maxLimit, qty);

                if (!_qtyCaptured && qty != _qtyBefore)
                {
                    item.Quantity = _qtyBefore;
                    _undo.Capture(_configuration.WithdrawItems, "");
                    _qtyCaptured = true;
                }

                item.Quantity = qty;
                _configuration.Save();
            }

            if (_qtyCaptured && ImGui.IsItemDeactivated())
            {
                _undo.Relabel($"Set {itemName} to {item.Quantity:N0}");
                _qtyCaptured = false;
            }
        }

        private void DrawHave(WithdrawItem item, long chestHave, long inventoryHave, bool insufficient)
        {
            var text = item.Mode switch
            {
                CustomItemMode.Deposit => FormatCount(inventoryHave),
                CustomItemMode.Both => FormatSplitCount(chestHave, inventoryHave),
                _ => FormatCount(chestHave)
            };

            var tooltip = item.Mode switch
            {
                CustomItemMode.Deposit => inventoryHave.ToString(),
                CustomItemMode.Both => $"Chest: {chestHave}\nInventory: {inventoryHave}",
                _ => chestHave.ToString()
            };

            if (insufficient)
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, text);
            }
            else
            {
                ImGui.TextColored(ImGuiColors.HealerGreen, text);
            }

            if (ImGui.IsItemHovered() && (item.Mode == CustomItemMode.Both || text != tooltip))
                ImGui.SetTooltip(tooltip);
        }

        private void DrawModeButton(WithdrawItem item, int visibleRow)
        {
            var tooltip = _paint.IsOrigin(visibleRow)
                ? ""
                : $"{GetModeLabel(item.Mode)}\nClick to cycle mode\nDrag up or down to apply to more rows";

            CellActionButton.DrawIcon(GetModeIcon(item.Mode), "mode", tooltip, () =>
            {
                _undo.Capture(_configuration.WithdrawItems, $"Set {GetRowName(item)} to {GetModeLabel(WithdrawItem.NextMode(item.Mode))}");
                item.CycleMode();
                _modeRevision++;
                _configuration.Save();
            });

            if (ImGui.IsItemActivated())
                StartPaint(PaintColumn.Mode, visibleRow, item);

            if (_paint.ShouldPaint(PaintColumn.Mode, visibleRow) && item.Mode != _paintMode)
            {
                CaptureOnFirstPaint();
                item.Mode = _paintMode;
                _modeRevision++;
                _paintCount++;
            }
        }

        private void DrawMaxToggle(WithdrawItem item, int index, int visibleRow)
        {
            var tooltip = _paint.IsOrigin(visibleRow)
                ? ""
                : "Always use max available\nDrag up or down to apply to more rows";

            CellActionButton.DrawText("M", $"max{index}", tooltip, () =>
            {
                _undo.Capture(_configuration.WithdrawItems, $"Set {GetRowName(item)} to {(item.AlwaysMax ? "a set quantity" : "Max")}");
                item.AlwaysMax = !item.AlwaysMax;
                _modeRevision++;
                _configuration.Save();
            }, item.AlwaysMax);

            if (ImGui.IsItemActivated())
                StartPaint(PaintColumn.Max, visibleRow, item);

            if (_paint.ShouldPaint(PaintColumn.Max, visibleRow) && item.AlwaysMax != _paintMax)
            {
                CaptureOnFirstPaint();
                item.AlwaysMax = _paintMax;
                _modeRevision++;
                _paintCount++;
            }
        }

        private void StartPaint(PaintColumn column, int visibleRow, WithdrawItem item)
        {
            _paint.Start(column, visibleRow);
            _paintMode = item.Mode;
            _paintMax = item.AlwaysMax;
            _paintCount = 0;
        }

        private void CaptureOnFirstPaint()
        {
            if (_paintCount == 0)
                _undo.Capture(_configuration.WithdrawItems, "");
        }

        private void FinishPaint()
        {
            if (!_paint.EndedThisFrame() || _paintCount == 0)
                return;

            _undo.Relabel($"Paint {_paintCount:N0} items");
            _configuration.Save();
        }

        private void DrawDeleteButton(int index)
        {
            CellActionButton.DrawIcon(FontAwesomeIcon.Minus, "delete", "Remove", () =>
            {
                _undo.Capture(_configuration.WithdrawItems, $"Remove {GetRowName(_configuration.WithdrawItems[index])}");
                _configuration.WithdrawItems.RemoveAt(index);
                _configuration.Save();
            }, true);
        }

        private string GetRowName(WithdrawItem item)
        {
            return _helper.GetItemName(item.ItemId);
        }

        private bool IsInsufficient(WithdrawItem item, long chestHave, long inventoryHave)
        {
            if (item.AlwaysMax) return false;

            return item.Mode switch
            {
                CustomItemMode.Deposit => inventoryHave < item.Quantity,
                CustomItemMode.Both => chestHave < item.Quantity || inventoryHave < item.Quantity,
                _ => chestHave < item.Quantity
            };
        }

        private static string GetModeLabel(CustomItemMode mode)
        {
            return mode switch
            {
                CustomItemMode.Deposit => "Deposit",
                CustomItemMode.Both => "Both",
                _ => "Withdraw"
            };
        }

        private static FontAwesomeIcon GetModeIcon(CustomItemMode mode)
        {
            return mode switch
            {
                CustomItemMode.Deposit => FontAwesomeIcon.ArrowDown,
                CustomItemMode.Both => FontAwesomeIcon.ArrowsAltV,
                _ => FontAwesomeIcon.ArrowUp
            };
        }

        private static string FormatCount(long value)
        {
            if (value <= 99999999) return value.ToString();
            if (value < 10000000) return $"{value / 1000000.0:0.#}m";
            if (value < 1000000000) return $"{value / 1000000}m";
            return $"{value / 1000000000.0:0.#}b";
        }

        private static string FormatSplitCount(long left, long right)
        {
            var leftText = left.ToString();
            var rightText = right.ToString();
            if (leftText.Length + rightText.Length <= 8)
                return $"{leftText}/{rightText}";

            return $"{FormatShortCount(left)}/{FormatShortCount(right)}";
        }

        private static string FormatShortCount(long value)
        {
            if (value < 1000) return value.ToString();
            if (value < 1000000) return $"{value / 1000}k";
            if (value < 10000000) return $"{value / 1000000.0:0.#}m";
            if (value < 1000000000) return $"{value / 1000000}m";
            return $"{value / 1000000000.0:0.#}b";
        }

        private void DrawPresets()
        {
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(avail * 0.45f);

            if (ImGui.BeginCombo("##customPresetSel", string.IsNullOrEmpty(_selectedPresetName) ? "Load Preset..." : _selectedPresetName))
            {
                foreach (var presetName in _configuration.SinglePresets.Keys)
                {
                    if (ImGui.Selectable(presetName, _selectedPresetName == presetName))
                    {
                        LoadPreset(presetName);
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine(0, 5);

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Save.ToIconString()))
            {
                _showSavePresetModal = true;
                _presetNameInput = "";
            }
            ImGui.PopFont();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Save Current List as Preset");

            ImGui.SameLine(0, 5);

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()) && !string.IsNullOrEmpty(_selectedPresetName))
            {
                if (_configuration.SinglePresets.ContainsKey(_selectedPresetName))
                {
                    _configuration.SinglePresets.Remove(_selectedPresetName);
                    _configuration.Save();
                    _selectedPresetName = "";
                }
            }
            ImGui.PopFont();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete Preset");

            ImGui.SameLine(0, 15);

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Export"))
            {
                if (Common.ExportHelper.Export(Common.ExportHelper.SinglesListPrefix, _configuration.WithdrawItems))
                {
                    Common.ChatHelper.Info("Custom list exported to clipboard.");
                }
                else
                {
                    Common.ChatHelper.Warning("Failed to export custom list.");
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Export to clipboard");

            ImGui.SameLine(0, 5);

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Import"))
            {
                var (result, data) = Common.ExportHelper.Import<List<WithdrawItem>>(Common.ExportHelper.SinglesListPrefix);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    var skipped = data.RemoveAll(x => Common.ItemListEligibility.IsIneligible(x.ItemId));
                    _configuration.WithdrawItems = data;
                    _configuration.Save();
                    Common.ChatHelper.Info($"Imported {data.Count} items to Custom list.");
                    if (skipped > 0)
                        Common.ChatHelper.Info($"Skipped {skipped} that cannot be stored in an FC chest.");
                }
                else
                {
                    Common.ChatHelper.Warning(Common.ExportHelper.GetErrorMessage(result, "Custom"));
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import from clipboard");
        }

        private void LoadPreset(string name)
        {
            if (_configuration.SinglePresets.TryGetValue(name, out var items))
            {
                _selectedPresetName = name;
                _configuration.WithdrawItems = items
                    .Where(x => !Common.ItemListEligibility.IsIneligible(x.ItemId))
                    .Select(x => x.Clone())
                    .ToList();
                _configuration.Save();
            }
        }

        private void DrawSavePresetModal()
        {
            if (_showSavePresetModal) ImGui.OpenPopup("Save Custom Preset");

            if (ImGui.BeginPopupModal("Save Custom Preset", ref _showSavePresetModal, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter Preset Name:");
                ImGui.InputText("##presetName", ref _presetNameInput, 64);

                ImGui.Spacing();

                if (ImGui.Button("Save", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_presetNameInput))
                    {
                        var listCopy = _configuration.WithdrawItems.Select(x => x.Clone()).ToList();
                        _configuration.SinglePresets[_presetNameInput] = listCopy;
                        _configuration.Save();
                        _selectedPresetName = _presetNameInput;
                        _showSavePresetModal = false;
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    _showSavePresetModal = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }
}
