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
                    if (ImGui.BeginTable("IgnoreListTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                    {
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                        ImGui.TableHeadersRow();

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
                            if (IsIgnored(gItem.Item))
                            {
                                ItemNameDisplay.TextColored(gItem.Item.ItemId, gItem.Name, ImGuiColors.DalamudOrange, _configuration);
                            }
                            else
                            {
                                ItemNameDisplay.TextDisabled(gItem.Item.ItemId, gItem.Name, _configuration);
                            }

                            ImGui.TableNextColumn();
                            DrawModeButton(gItem.Item);

                            ImGui.TableNextColumn();
                            CellActionButton.DrawIcon(FontAwesomeIcon.Minus, "delete", "Remove", () =>
                            {
                                _helper.Configuration.IgnoreList.Remove(gItem.Item);
                                _helper.Configuration.Save();
                            }, true);

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

        private void DrawModeButton(Configuration.IgnoredItem item)
        {
            CellActionButton.DrawIcon(GetModeIcon(item), "mode", $"{GetModeLabel(item)}\nClick to cycle mode", () =>
            {
                CycleMode(item);
                _helper.Configuration.Save();
            });
        }

        private static void CycleMode(Configuration.IgnoredItem item)
        {
            var deposit = item.IgnoreEntrust;
            var withdraw = item.IgnoreWithdraw;

            if (!deposit && withdraw)
            {
                item.IgnoreEntrust = true;
                item.IgnoreWithdraw = false;
                return;
            }

            if (deposit && !withdraw)
            {
                item.IgnoreEntrust = true;
                item.IgnoreWithdraw = true;
                return;
            }

            item.IgnoreEntrust = false;
            item.IgnoreWithdraw = true;
        }

        private static bool IsIgnored(Configuration.IgnoredItem item)
        {
            return item.IgnoreEntrust || item.IgnoreWithdraw;
        }

        private static string GetModeLabel(Configuration.IgnoredItem item)
        {
            if (item.IgnoreEntrust && item.IgnoreWithdraw) return "Skip Deposit and Withdraw";
            if (item.IgnoreEntrust) return "Skip Deposit";
            if (item.IgnoreWithdraw) return "Skip Withdraw";
            return "Not Ignored";
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
