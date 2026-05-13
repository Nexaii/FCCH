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

        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        public CustomTab(ChestHelper helper, Configuration configuration)
        {
            _helper = helper;
            _configuration = configuration;
        }

        public void Draw()
        {
            DrawPresets();
            ImGui.Separator();

            int totalQty = _configuration.WithdrawItems.Sum(x => x.Quantity);
            int itemCount = _configuration.WithdrawItems.Count;

            if (ImGui.BeginTable("##customHeader", 2))
            {
                ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##btn", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextDisabled($"Custom List ({itemCount} items, {totalQty:N0} total)");

                ImGui.TableNextColumn();
                if (itemCount > 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("Clear List", new Vector2(-1, 0)))
                    {
                        _configuration.WithdrawItems.Clear();
                        _configuration.Save();
                    }
                    ImGui.PopStyleColor();
                }
                ImGui.EndTable();
            }

            float footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2;

            if (_configuration.WithdrawItems.Count == 0)
            {
                ImGui.BeginChild("CustomItemsList", new Vector2(0, -footerHeight), true);
                ImGui.TextDisabled("No items in custom list. Use search below to add items.");
                ImGui.EndChild();
            }
            else
            {
                if (ImGui.BeginChild("CustomItemsList", new Vector2(0, -footerHeight), true))
                {
                    if (ImGui.BeginTable("CustomItemsTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                    {
                        float numericColumnWidth = ImGui.CalcTextSize(NumericColumnSample).X + ImGui.GetStyle().FramePadding.X * 2;

                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, numericColumnWidth);
                        ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, numericColumnWidth);
                        ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                        ImGui.TableSetupColumn("Max", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                        ImGui.TableHeadersRow();

                        var sortedItems = _configuration.WithdrawItems
                            .Select((item, idx) => new { Item = item, Index = idx, Name = _helper.GetItemName(item.ItemId) })
                            .OrderBy(x => x.Name)
                            .ToList();

                        foreach (var gItem in sortedItems)
                        {
                            ImGui.PushID($"c_item_{gItem.Index}");
                            DrawItemRow(gItem.Item, gItem.Index, gItem.Name);
                            ImGui.PopID();
                        }
                        ImGui.EndTable();
                    }
                    ImGui.EndChild();
                }
            }

            DrawSearchBox();

            DrawSavePresetModal();
        }

        private void DrawSearchBox()
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.2f, 0.2f, 0.2f, 1f));

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##addItemSearch", "Search and add items...", ImGuiComboFlags.HeightLarge))
            {
                ImGui.PopStyleColor(2);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##searchIn", ref _searchFilter, 64);
                var sheet = Plugin.Data.GetExcelSheet<Item>();

                if (sheet != null && !string.IsNullOrEmpty(_searchFilter))
                {
                    var filtered = sheet.Where(i =>
                    {
                        var name = i.Name.ToString();
                        if (string.IsNullOrEmpty(name)) return false;
                        if (!name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) return false;
                        return ItemListEligibility.IsAllowed(i);
                    }).Take(20);

                    if (filtered.Any())
                    {
                        foreach (var item in filtered)
                        {
                            if (ImGui.Selectable(item.Name.ToString(), false))
                            {
                                AddItem(item);
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("No results found");
                    }
                }
                ImGui.EndCombo();
            }
            else
            {
                ImGui.PopStyleColor(2);
            }
        }

        private void AddItem(Item item)
        {
            if (!_configuration.WithdrawItems.Any(x => x.ItemId == item.RowId))
            {
                var newItem = new WithdrawItem { ItemId = item.RowId, Quantity = 1 };
                _configuration.WithdrawItems.Add(newItem);
                _configuration.WithdrawItems.Sort((a, b) => string.Compare(_helper.GetItemName(a.ItemId), _helper.GetItemName(b.ItemId), StringComparison.OrdinalIgnoreCase));
                _configuration.Save();
            }
            _searchFilter = "";
        }

        private void DrawItemRow(WithdrawItem item, int index, string itemName)
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

            DrawQuantity(item, maxLimit);

            ImGui.TableNextColumn();
            DrawHave(item, chestHave, inventoryHave, insufficient);

            ImGui.TableNextColumn();
            DrawModeButton(item);

            ImGui.TableNextColumn();
            DrawMaxToggle(item, index);

            ImGui.TableNextColumn();
            DrawDeleteButton(index);
        }

        private void DrawQuantity(WithdrawItem item, int maxLimit)
        {
            if (item.AlwaysMax)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Max");
                return;
            }

            int qty = item.Quantity;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("##qty", ref qty, 0))
            {
                qty = Math.Max(1, qty);
                qty = Math.Min(maxLimit, qty);
                item.Quantity = qty;
                _configuration.Save();
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

        private void DrawModeButton(WithdrawItem item)
        {
            CellActionButton.DrawIcon(GetModeIcon(item.Mode), "mode", $"{GetModeLabel(item.Mode)}\nClick to cycle mode", () =>
            {
                item.CycleMode();
                _configuration.Save();
            });
        }

        private void DrawMaxToggle(WithdrawItem item, int index)
        {
            CellActionButton.DrawText("M", $"max{index}", "Always use max available", () =>
            {
                item.AlwaysMax = !item.AlwaysMax;
                _configuration.Save();
            }, item.AlwaysMax);
        }

        private void DrawDeleteButton(int index)
        {
            CellActionButton.DrawIcon(FontAwesomeIcon.Minus, "delete", "Remove", () =>
            {
                _configuration.WithdrawItems.RemoveAt(index);
                _configuration.Save();
            }, true);
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
                if (Common.ExportHelper.Export(Common.ExportHelper.HEADER_SINGLES, _configuration.WithdrawItems))
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
                var (result, data) = Common.ExportHelper.Import<List<WithdrawItem>>(Common.ExportHelper.HEADER_SINGLES);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    _configuration.WithdrawItems = data;
                    _configuration.Save();
                    Common.ChatHelper.Info($"Imported {data.Count} items to Custom list.");
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
                _configuration.WithdrawItems = items.Select(x => x.Clone()).ToList();
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
