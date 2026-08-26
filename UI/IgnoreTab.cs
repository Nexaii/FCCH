using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Lumina.Excel.Sheets;
using FCCH;
using FCCH.Managers;
using FCCH.Common;

namespace FCCH.UI
{
    public class IgnoreTab
    {
        private readonly ChestHelper _helper;
        private readonly Configuration _configuration;

        private string _ignoreSearchFilter = string.Empty;
        private string _listFilter = string.Empty;
        private bool _showClearConfirm = false;

        private Item[] _filteredItemsCache = Array.Empty<Item>();
        private string _lastSearch = string.Empty;

        private readonly List<CategoryResolver.CategoryMatch> _categoryResults = new();
        private CategoryResolver.CategoryMatch _pendingCategory;
        private bool _showCategoryConfirm = false;

        private readonly List<(Configuration.IgnoredItem Item, int Index, string Name)> _visible = new();
        private List<Configuration.IgnoredItem> _viewSource = new();
        private string _viewFilter = string.Empty;
        private int _viewCount = -1;
        private string _countText = string.Empty;

        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        private readonly UndoStack<Configuration.IgnoredItem> _undo = new(x => x.Clone());
        private readonly BulkPaint _paint = new();
        private bool _paintEntrust;
        private bool _paintWithdraw;
        private int _paintCount;

        private int _modeRevision;
        private int _viewRevision = -1;
        private bool _headerEntrust;
        private bool _headerWithdraw;
        private bool _headerMixed;
        private bool _showHeaderModeConfirm;

        public IgnoreTab(ChestHelper helper, Configuration configuration)
        {
            _helper = helper;
            _configuration = configuration;
        }

        public void Draw()
        {
            DrawPresets();
            DrawSearchBox();
            ImGui.Separator();

            int itemCount = _helper.Configuration.IgnoreList.Count;
            _paint.BeginFrame();
            RefreshView();

            DrawListHeader(itemCount);

            if (ImGui.BeginChild("IgnoreListScroll", new Vector2(0, ImGui.GetContentRegionAvail().Y), true))
            {
                if (itemCount == 0)
                {
                    ImGui.TextDisabled("No items in ignore list. Use the search box above to add items.");
                }
                else if (_visible.Count == 0)
                {
                    ImGui.TextDisabled($"No item in your list matches \"{_listFilter}\". Use the search box above to add it.");
                }
                else if (ImGui.BeginTable("IgnoreListTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
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
                            ImGui.PushID($"ig_item_{row.Index}");
                            ImGui.TableNextRow();

                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            if (IsIgnored(row.Item))
                            {
                                ItemNameDisplay.TextColored(row.Item.ItemId, row.Name, ImGuiColors.DalamudOrange, _configuration);
                            }
                            else
                            {
                                ItemNameDisplay.TextDisabled(row.Item.ItemId, row.Name, _configuration);
                            }

                            ImGui.TableNextColumn();
                            DrawModeButton(row.Item, i);

                            ImGui.TableNextColumn();
                            CellActionButton.DrawIcon(FontAwesomeIcon.Minus, "delete", "Remove", () =>
                            {
                                _undo.Capture(_configuration.IgnoreList, $"Remove {row.Name}");
                                _helper.Configuration.IgnoreList.Remove(row.Item);
                                _helper.Configuration.Save();
                            }, true);

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
                var body = $"Ignore all {_pendingCategory.Count:N0} items in {_pendingCategory.Name}?\n\nDeposit and withdraw will skip every one of them until you remove them from this list.";
                if (ListChrome.DrawConfirm("Ignore category", body, $"Ignore {_pendingCategory.Count:N0}", ref _showCategoryConfirm))
                    AddCategory(_pendingCategory);
            }

            if (_showHeaderModeConfirm)
            {
                var next = NextHeaderMode();
                var body = $"Set all {_visible.Count:N0} shown items to {GetModeLabel(next.Entrust, next.Withdraw)}?" + Environment.NewLine + Environment.NewLine + IgnoreEffect(next.Entrust, next.Withdraw);
                if (ListChrome.DrawConfirm("Set ignore mode", body, $"Set {_visible.Count:N0}", ref _showHeaderModeConfirm))
                    ApplyHeaderMode();
            }

            if (ListChrome.DrawConfirm("Clear Ignore List", $"Remove all {itemCount} items from the ignore list?", "Clear List", ref _showClearConfirm))
            {
                _undo.Capture(_helper.Configuration.IgnoreList, $"Clear list ({itemCount:N0} items)");
                _helper.Configuration.IgnoreList.Clear();
                _listFilter = string.Empty;
                _helper.Configuration.Save();
            }
        }

        private void Undo()
        {
            _configuration.IgnoreList = _undo.Pop();
            _configuration.Save();
        }

        private void RefreshView()
        {
            var source = _configuration.IgnoreList;

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

        private void RefreshHeaderState()
        {
            _headerMixed = false;

            if (_visible.Count == 0)
                return;

            _headerEntrust = _visible[0].Item.IgnoreEntrust;
            _headerWithdraw = _visible[0].Item.IgnoreWithdraw;

            for (var i = 1; i < _visible.Count; i++)
            {
                if (_visible[i].Item.IgnoreEntrust == _headerEntrust && _visible[i].Item.IgnoreWithdraw == _headerWithdraw)
                    continue;

                _headerMixed = true;
                return;
            }
        }

        private void DrawTableHeaders()
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            ImGui.TableSetColumnIndex(0);
            ImGui.TableHeader("Item");

            ImGui.TableSetColumnIndex(1);
            ImGui.TableHeader("Mode");
            if (ImGui.IsItemClicked())
                RequestHeaderMode();
            if (ImGui.IsItemHovered())
            {
                var next = NextHeaderMode();
                ImGui.SetTooltip(BulkHeader.Tooltip(_visible.Count, _viewCount, GetModeLabel(next.Entrust, next.Withdraw), _headerMixed));
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.TableHeader("");
        }

        private static string IgnoreEffect(bool deposit, bool withdraw)
        {
            if (deposit && withdraw) return "Deposit and withdraw will skip every one of them.";
            if (deposit) return "Deposit will skip every one of them.";
            if (withdraw) return "Withdraw will skip every one of them.";
            return "Deposit and withdraw will move them normally.";
        }

        private (bool Entrust, bool Withdraw) NextHeaderMode()
        {
            return _headerMixed ? (false, true) : NextMode(_headerEntrust, _headerWithdraw);
        }

        private void RequestHeaderMode()
        {
            if (_visible.Count > BulkHeader.ConfirmThreshold)
                _showHeaderModeConfirm = true;
            else
                ApplyHeaderMode();
        }

        private void ApplyHeaderMode()
        {
            var target = NextHeaderMode();
            _undo.Capture(_configuration.IgnoreList, "");

            var changed = 0;
            for (var i = 0; i < _visible.Count; i++)
            {
                if (_visible[i].Item.IgnoreEntrust == target.Entrust && _visible[i].Item.IgnoreWithdraw == target.Withdraw)
                    continue;

                _visible[i].Item.IgnoreEntrust = target.Entrust;
                _visible[i].Item.IgnoreWithdraw = target.Withdraw;
                changed++;
            }

            if (changed == 0)
            {
                _undo.Discard();
                return;
            }

            _undo.Relabel($"Set {changed:N0} items to {GetModeLabel(target.Entrust, target.Withdraw)}");
            _modeRevision++;
            _configuration.Save();
        }

        private void DrawListHeader(int total)
        {
            if (!ImGui.BeginTable("##ignoreHeader", 4))
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
            ImGui.InputTextWithHint("##ignoreListFilter", "Filter your list...", ref _listFilter, 64);

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

        private void DrawModeButton(Configuration.IgnoredItem item, int visibleRow)
        {
            var tooltip = _paint.IsOrigin(visibleRow)
                ? ""
                : $"{GetModeLabel(item)}\nClick to cycle mode\nDrag up or down to apply to more rows";

            CellActionButton.DrawIcon(GetModeIcon(item), "mode", tooltip, () =>
            {
                var next = NextMode(item.IgnoreEntrust, item.IgnoreWithdraw);
                _undo.Capture(_configuration.IgnoreList, $"Set {_helper.GetItemName(item.ItemId)} to {GetModeLabel(next.Entrust, next.Withdraw)}");
                CycleMode(item);
                _modeRevision++;
                _helper.Configuration.Save();
            });

            if (ImGui.IsItemActivated())
                StartPaint(visibleRow, item);

            if (!_paint.ShouldPaint(PaintColumn.Mode, visibleRow))
                return;

            if (item.IgnoreEntrust == _paintEntrust && item.IgnoreWithdraw == _paintWithdraw)
                return;

            if (_paintCount == 0)
                _undo.Capture(_configuration.IgnoreList, "");

            item.IgnoreEntrust = _paintEntrust;
            item.IgnoreWithdraw = _paintWithdraw;
            _modeRevision++;
            _paintCount++;
        }

        private void StartPaint(int visibleRow, Configuration.IgnoredItem item)
        {
            _paint.Start(PaintColumn.Mode, visibleRow);
            _paintEntrust = item.IgnoreEntrust;
            _paintWithdraw = item.IgnoreWithdraw;
            _paintCount = 0;
        }

        private void FinishPaint()
        {
            if (!_paint.EndedThisFrame() || _paintCount == 0)
                return;

            _undo.Relabel($"Paint {_paintCount:N0} items");
            _configuration.Save();
        }

        private static void CycleMode(Configuration.IgnoredItem item)
        {
            var next = NextMode(item.IgnoreEntrust, item.IgnoreWithdraw);
            item.IgnoreEntrust = next.Entrust;
            item.IgnoreWithdraw = next.Withdraw;
        }

        private static (bool Entrust, bool Withdraw) NextMode(bool deposit, bool withdraw)
        {
            if (!deposit && withdraw)
                return (true, false);

            if (deposit && !withdraw)
                return (true, true);

            return (false, true);
        }

        private static string GetModeLabel(bool deposit, bool withdraw)
        {
            if (deposit && withdraw) return "Skip Deposit and Withdraw";
            if (deposit) return "Skip Deposit";
            if (withdraw) return "Skip Withdraw";
            return "Not Ignored";
        }

        private static bool IsIgnored(Configuration.IgnoredItem item)
        {
            return item.IgnoreEntrust || item.IgnoreWithdraw;
        }

        private static string GetModeLabel(Configuration.IgnoredItem item)
        {
            return GetModeLabel(item.IgnoreEntrust, item.IgnoreWithdraw);
        }

        private static FontAwesomeIcon GetModeIcon(Configuration.IgnoredItem item)
        {
            if (item.IgnoreEntrust && item.IgnoreWithdraw) return FontAwesomeIcon.ArrowsAltV;
            if (item.IgnoreEntrust) return FontAwesomeIcon.ArrowDown;
            return FontAwesomeIcon.ArrowUp;
        }

        private void DrawSearchBox()
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.2f, 0.2f, 0.2f, 1f));

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##ignoreSearch", "Search items or categories to ignore...", ImGuiComboFlags.HeightLarge))
            {
                ImGui.PopStyleColor(2);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##igSearchIn", ref _ignoreSearchFilter, 64);
                UpdateSearchCache();

                if (_categoryResults.Count > 0)
                {
                    foreach (var category in _categoryResults)
                    {
                        if (ImGui.Selectable($"{category.Name}##cat{category.Id}", false))
                        {
                            _pendingCategory = category;
                            _showCategoryConfirm = true;
                            ImGui.CloseCurrentPopup();
                        }

                        ListChrome.DrawTrailingLabel($"add all {category.Count:N0}");
                    }

                    ImGui.Separator();
                }

                if (_filteredItemsCache.Length > 0)
                {
                    foreach (var item in _filteredItemsCache)
                    {
                        bool alreadyHeld = _helper.Configuration.IgnoreList.Any(x => x.ItemId == item.RowId);

                        if (ImGui.Selectable(item.Name.ToString(), false))
                        {
                            AddItemToIgnore(item);
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
        }

        private void UpdateSearchCache()
        {
            if (_ignoreSearchFilter == _lastSearch) return;

            _lastSearch = _ignoreSearchFilter;
            CategoryResolver.Match(_ignoreSearchFilter, _categoryResults);
            var sheet = Plugin.Data.GetExcelSheet<Item>();
            if (sheet != null && !string.IsNullOrEmpty(_ignoreSearchFilter))
            {
                _filteredItemsCache = sheet.Where(i =>
                {
                    var name = i.Name.ToString();
                    if (string.IsNullOrEmpty(name)) return false;
                    if (!name.Contains(_ignoreSearchFilter, StringComparison.OrdinalIgnoreCase)) return false;
                    return ItemListEligibility.IsAllowed(i);
                }).Take(20).ToArray();
            }
            else
            {
                _filteredItemsCache = Array.Empty<Item>();
            }
        }

        private void AddItemToIgnore(Item item)
        {
            AddItems(new[] { item.RowId });
            ClearSearch();
        }

        private int AddItems(IReadOnlyList<uint> itemIds)
        {
            var held = new HashSet<uint>();
            foreach (var existing in _helper.Configuration.IgnoreList)
                held.Add(existing.ItemId);

            var added = 0;
            foreach (var itemId in itemIds)
            {
                if (!held.Add(itemId))
                    continue;

                _helper.Configuration.IgnoreList.Add(new Configuration.IgnoredItem
                {
                    ItemId = itemId,
                    Name = _helper.GetItemName(itemId),
                    IgnoreEntrust = true,
                    IgnoreWithdraw = true
                });
                added++;
            }

            if (added > 0)
            {
                _helper.Configuration.IgnoreList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                _helper.Configuration.Save();
            }

            return added;
        }

        private void ClearSearch()
        {
            _ignoreSearchFilter = string.Empty;
            _listFilter = string.Empty;
            UpdateSearchCache();
        }

        private void AddCategory(CategoryResolver.CategoryMatch category)
        {
            var ids = CategoryResolver.GetItemIds(category.Id);
            _undo.Capture(_configuration.IgnoreList, "");
            var added = AddItems(ids);
            var skipped = ids.Count - added;

            if (added == 0)
                _undo.Discard();
            else
                _undo.Relabel($"Ignore {added:N0} items from {category.Name}");

            if (added == 0)
                Common.ChatHelper.Info($"All {ids.Count:N0} items from {category.Name} were already ignored.");
            else if (skipped > 0)
                Common.ChatHelper.Info($"Now ignoring {added:N0} items from {category.Name} ({skipped:N0} already ignored).");
            else
                Common.ChatHelper.Info($"Now ignoring {added:N0} items from {category.Name}.");

            ClearSearch();
        }

        private void DrawPresets()
        {
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(avail * 0.45f);

            if (ImGui.BeginCombo("##ignorePresetSel", string.IsNullOrEmpty(_selectedPresetName) ? "Load Preset..." : _selectedPresetName))
            {
                foreach (var presetName in _helper.Configuration.IgnorePresets.Keys)
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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Save Current Ignore List as Preset");

            ImGui.SameLine(0, 5);

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()) && !string.IsNullOrEmpty(_selectedPresetName))
            {
                if (_helper.Configuration.IgnorePresets.ContainsKey(_selectedPresetName))
                {
                    _helper.Configuration.IgnorePresets.Remove(_selectedPresetName);
                    _helper.Configuration.Save();
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
                if (Common.ExportHelper.Export(Common.ExportHelper.IgnoreListPrefix, _helper.Configuration.IgnoreList))
                {
                    Common.ChatHelper.Info("Ignore list exported to clipboard.");
                }
                else
                {
                    Common.ChatHelper.Warning("Failed to export ignore list.");
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Export to clipboard");

            ImGui.SameLine(0, 5);

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Import"))
            {
                var (result, data) = Common.ExportHelper.Import<List<Configuration.IgnoredItem>>(Common.ExportHelper.IgnoreListPrefix);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    var skipped = data.RemoveAll(x => Common.ItemListEligibility.IsIneligible(x.ItemId));
                    _helper.Configuration.IgnoreList = data;
                    _helper.Configuration.Save();
                    Common.ChatHelper.Info($"Imported {data.Count} items to Ignore list.");
                    if (skipped > 0)
                        Common.ChatHelper.Info($"Skipped {skipped} that cannot be stored in an FC chest.");
                }
                else
                {
                    Common.ChatHelper.Warning(Common.ExportHelper.GetErrorMessage(result, "Ignore"));
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import from clipboard");
        }

        private void LoadPreset(string name)
        {
            if (_configuration.IgnorePresets.TryGetValue(name, out var items))
            {
                _selectedPresetName = name;
                _configuration.IgnoreList = items
                    .Where(x => !Common.ItemListEligibility.IsIneligible(x.ItemId))
                    .Select(x => x.Clone()).ToList();
                _configuration.Save();
            }
        }

        private void DrawSavePresetModal()
        {
            if (_showSavePresetModal) ImGui.OpenPopup("Save Ignore Preset");

            if (ImGui.BeginPopupModal("Save Ignore Preset", ref _showSavePresetModal, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter Preset Name:");
                ImGui.InputText("##igPresetName", ref _presetNameInput, 64);

                ImGui.Spacing();

                if (ImGui.Button("Save", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_presetNameInput))
                    {
                        var listCopy = _configuration.IgnoreList.Select(x => x.Clone()).ToList();

                        _configuration.IgnorePresets[_presetNameInput] = listCopy;
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
