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

namespace FCCH.UI
{
    public class IgnoreTab
    {
        private readonly ChestHelper _helper;
        private readonly Configuration _configuration;

        private string _ignoreSearchFilter = string.Empty;

        private Item[] _filteredItemsCache = Array.Empty<Item>();
        private string _lastSearch = string.Empty;

        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        public IgnoreTab(ChestHelper helper, Configuration configuration)
        {
            _helper = helper;
            _configuration = configuration;
        }

        public void Draw()
        {
            DrawPresets();
            ImGui.Separator();

            int itemCount = _helper.Configuration.IgnoreList.Count;

            if (ImGui.BeginTable("##ignoreHeader", 2))
            {
                ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##btn", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextDisabled($"Ignored Items ({itemCount})");

                ImGui.TableNextColumn();
                if (itemCount > 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("Clear List", new Vector2(-1, 0)))
                    {
                        _helper.Configuration.IgnoreList.Clear();
                        _helper.Configuration.Save();
                    }
                    ImGui.PopStyleColor();
                }
                ImGui.EndTable();
            }

            float footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2;

            if (_helper.Configuration.IgnoreList.Count == 0)
            {
                ImGui.BeginChild("IgnoreListScroll", new Vector2(0, -footerHeight), true);
                ImGui.TextDisabled("No items in ignore list. Use search below to add items.");
                ImGui.EndChild();
            }
            else
            {
                if (ImGui.BeginChild("IgnoreListScroll", new Vector2(0, -footerHeight), true))
                {
                    if (ImGui.BeginTable("IgnoreListTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                    {
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Dep", ImGuiTableColumnFlags.WidthFixed, 35);
                        ImGui.TableSetupColumn("Wit", ImGuiTableColumnFlags.WidthFixed, 35);
                        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 25);
                        
                        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                        ImGui.TableNextColumn();
                        ImGui.Text("Item");
                        
                        ImGui.TableNextColumn();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var depIcon = FontAwesomeIcon.ArrowCircleDown.ToIconString();
                        var iconWidth = ImGui.CalcTextSize(depIcon).X;
                        var colWidth = ImGui.GetColumnWidth();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colWidth - iconWidth) * 0.5f);
                        ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), depIcon);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Skip during Deposit All");

                        ImGui.TableNextColumn();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var witIcon = FontAwesomeIcon.ArrowCircleUp.ToIconString();
                        iconWidth = ImGui.CalcTextSize(witIcon).X;
                        colWidth = ImGui.GetColumnWidth();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colWidth - iconWidth) * 0.5f);
                        ImGui.TextColored(new Vector4(1f, 0.64f, 0f, 1f), witIcon);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Skip during Withdraw");

                        ImGui.TableNextColumn();
                        var delIcon = FontAwesomeIcon.Trash.ToIconString();
                        iconWidth = ImGui.CalcTextSize(delIcon).X;
                        colWidth = ImGui.GetColumnWidth();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colWidth - iconWidth) * 0.5f);
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.TextDisabled(delIcon);
                        ImGui.PopFont();

                        var sortedItems = _helper.Configuration.IgnoreList
                            .Select(x => new { Item = x, Name = _helper.GetItemName(x.ItemId) })
                            .OrderBy(x => x.Name)
                            .ToList();

                        foreach (var gItem in sortedItems)
                        {
                            int idx = _helper.Configuration.IgnoreList.IndexOf(gItem.Item);
                            if (idx == -1) continue;

                            ImGui.PushID($"ig_item_{idx}");
                            ImGui.TableNextRow();

                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            bool isActive = gItem.Item.IgnoreEntrust || gItem.Item.IgnoreWithdraw;
                            if (isActive)
                            {
                                ImGui.TextColored(ImGuiColors.DalamudOrange, gItem.Name);
                            }
                            else
                            {
                                ImGui.TextDisabled(gItem.Name);
                            }

                            ImGui.TableNextColumn();
                            bool entrust = gItem.Item.IgnoreEntrust;
                            var chkWidth = ImGui.GetFrameHeight();
                            var colW = ImGui.GetColumnWidth();
                            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colW - chkWidth) * 0.5f);
                            if (ImGui.Checkbox("##dep", ref entrust))
                            {
                                gItem.Item.IgnoreEntrust = entrust;
                                _helper.Configuration.Save();
                            }
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Skip during Deposit All");

                            ImGui.TableNextColumn();
                            bool withdraw = gItem.Item.IgnoreWithdraw;
                            colW = ImGui.GetColumnWidth();
                            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colW - chkWidth) * 0.5f);
                            if (ImGui.Checkbox("##wit", ref withdraw))
                            {
                                gItem.Item.IgnoreWithdraw = withdraw;
                                _helper.Configuration.Save();
                            }
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Skip during Withdraw");

                            ImGui.TableNextColumn();
                            ImGui.PushFont(UiBuilder.IconFont);
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
                            var btnW = ImGui.CalcTextSize(FontAwesomeIcon.Minus.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2;
                            colW = ImGui.GetColumnWidth();
                            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colW - btnW) * 0.5f);
                            
                            if (ImGui.Button(FontAwesomeIcon.Minus.ToIconString()))
                            {
                                _helper.Configuration.IgnoreList.Remove(gItem.Item);
                                _helper.Configuration.Save();
                            }
                            ImGui.PopStyleColor(2);
                            ImGui.PopFont();
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove from ignore list");

                            ImGui.PopID();
                        }
                        ImGui.EndTable();
                    }
                    ImGui.EndChild();
                }
            }

            ImGui.Spacing();
            DrawSearchBox();

            DrawSavePresetModal();
        }

        private void DrawSearchBox()
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.2f, 0.2f, 0.2f, 1f));

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##ignoreSearch", "Search and add items to ignore...", ImGuiComboFlags.HeightLarge))
            {
                ImGui.PopStyleColor(2);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##igSearchIn", ref _ignoreSearchFilter, 64);
                UpdateSearchCache();

                if (_filteredItemsCache.Length > 0)
                {
                    foreach (var item in _filteredItemsCache)
                    {
                        if (ImGui.Selectable(item.Name.ToString(), false))
                        {
                            AddItemToIgnore(item);
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }
                else
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
            var sheet = Plugin.Data.GetExcelSheet<Item>();
            if (sheet != null && !string.IsNullOrEmpty(_ignoreSearchFilter))
            {
                _filteredItemsCache = sheet.Where(i =>
                   i.Name.ToString().Contains(_ignoreSearchFilter, StringComparison.OrdinalIgnoreCase)
                   && !i.IsUntradable
                   && i.RowId != 1
                   && !(i.RowId >= 2 && i.RowId <= 19)
                   && i.ItemSearchCategory.RowId != 0
                   && i.ItemUICategory.RowId != 61
                ).Take(20).ToArray();
            }
            else
            {
                _filteredItemsCache = Array.Empty<Item>();
            }
        }

        private void AddItemToIgnore(Item item)
        {
            if (!_helper.Configuration.IgnoreList.Any(x => x.ItemId == item.RowId))
            {
                _helper.Configuration.IgnoreList.Add(new Configuration.IgnoredItem
                {
                    ItemId = item.RowId,
                    Name = item.Name.ToString(),
                    IgnoreEntrust = true,
                    IgnoreWithdraw = true
                });

                _helper.Configuration.IgnoreList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                _helper.Configuration.Save();
                _ignoreSearchFilter = string.Empty;
                UpdateSearchCache();
            }
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
                if (Common.ExportHelper.Export(Common.ExportHelper.HEADER_IGNORE, _helper.Configuration.IgnoreList))
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
                var (result, data) = Common.ExportHelper.Import<List<Configuration.IgnoredItem>>(Common.ExportHelper.HEADER_IGNORE);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    _helper.Configuration.IgnoreList = data;
                    _helper.Configuration.Save();
                    Common.ChatHelper.Info($"Imported {data.Count} items to Ignore list.");
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
                _configuration.IgnoreList = items.Select(x => new Configuration.IgnoredItem
                {
                    ItemId = x.ItemId,
                    Name = x.Name,
                    IgnoreEntrust = x.IgnoreEntrust,
                    IgnoreWithdraw = x.IgnoreWithdraw
                }).ToList();
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
                        var listCopy = _configuration.IgnoreList.Select(x => new Configuration.IgnoredItem
                        {
                            ItemId = x.ItemId,
                            Name = x.Name,
                            IgnoreEntrust = x.IgnoreEntrust,
                            IgnoreWithdraw = x.IgnoreWithdraw
                        }).ToList();

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
