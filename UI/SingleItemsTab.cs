using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Lumina.Excel.Sheets;
using FC_Chest_Helper.GameData;

namespace FC_Chest_Helper.UI
{
    public class SingleItemsTab
    {
        private readonly FCChestHelper _helper;
        private readonly Configuration _configuration;

        // Search State
        private string _searchFilter = "";
        
        // Preset State
        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        public SingleItemsTab(FCChestHelper helper, Configuration configuration)
        {
            _helper = helper;
            _configuration = configuration;
        }

        public void Draw()
        {
            // PRESETS
            DrawPresets();
            ImGui.Separator();

            // Calculate Footer Height
            float footerHeight = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y * 3;

            // CURRENT LIST (Scrollable, takes remaining space)
            if (_configuration.WithdrawItems.Count == 0)
            {
                ImGui.BeginChild("SingleItemsList", new Vector2(0, -footerHeight), true);
                ImGui.TextDisabled("No items in withdrawal list.");
                ImGui.EndChild();
            }
            else
            {
                if (ImGui.BeginChild("SingleItemsList", new Vector2(0, -footerHeight), true))
                {
                    var groupedItems = _configuration.WithdrawItems
                         .Select(x => new { Item = x, Name = _helper.GetItemName(x.ItemId) })
                         .OrderBy(x => x.Name)
                         .GroupBy(x => x.Name.Length > 0 ? char.ToUpper(x.Name[0]) : '?');

                    foreach (var group in groupedItems)
                    {
                        if (ImGui.CollapsingHeader(group.Key.ToString(), ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            if (ImGui.BeginTable($"InnerTable_{group.Key}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                            {
                                ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 30);
                                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 70);
                                ImGui.TableSetupColumn("##max", ImGuiTableColumnFlags.WidthFixed, 40);
                                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                                ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 50);
                                ImGui.TableHeadersRow();

                                foreach (var gItem in group)
                                {
                                    int idx = _configuration.WithdrawItems.IndexOf(gItem.Item);
                                    if (idx != -1)
                                    {
                                        ImGui.PushID($"s_item_{group.Key}_{idx}");
                                        DrawItemRow(gItem.Item, idx);
                                        ImGui.PopID();
                                    }
                                }
                                ImGui.EndTable();
                            }
                        }
                    }
                    ImGui.EndChild();
                }
            }

            // ADD NEW ITEM (Bottom/Sticky Footer)
            ImGui.Separator();

            ImGui.TextColored(ImGuiColors.HealerGreen, "Add New Item:");

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.4f, 0.4f, 0.4f, 1f));
            if (ImGui.BeginCombo("##addItemSearch", "Search item...", ImGuiComboFlags.HeightLarge))
            {
                 ImGui.PopStyleColor();
                 ImGui.InputText("##searchIn", ref _searchFilter, 64);
                  var sheet = Plugin.Data.GetExcelSheet<Item>();
                  
                  if (sheet != null && !string.IsNullOrEmpty(_searchFilter))
                  {
                       // Filter: Name match + Not Untradable + Exclude elemental items (IDs 2-19)
                       var filtered = sheet.Where(i => 
                            i.Name.ToString().Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) 
                            && !i.IsUntradable 
                            && !(i.RowId >= 2 && i.RowId <= 19) // Exclude elemental items
                       ).Take(20);

                       if (filtered.Any())
                      {
                          foreach (var item in filtered)
                          {
                              // Click to Add
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
            else { ImGui.PopStyleColor(); }
            
            DrawSavePresetModal();
        }
        
        private void AddItem(Item item)
        {
             if (!_configuration.WithdrawItems.Any(x => x.ItemId == item.RowId))
             {
                  var newItem = new WithdrawItem { ItemId = item.RowId, Quantity = 1 };
                  _configuration.WithdrawItems.Add(newItem);
                  
                  // Sort 
                  _configuration.WithdrawItems.Sort((a, b) => string.Compare(_helper.GetItemName(a.ItemId), _helper.GetItemName(b.ItemId), StringComparison.OrdinalIgnoreCase));
                  
                  _configuration.Save();
             }
             _searchFilter = "";
        }

        private void DrawItemRow(WithdrawItem item, int index)
        {
            ImGui.TableNextRow();

            // Remove Button
            ImGui.TableNextColumn();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
            bool clicked = ImGui.Button(FontAwesomeIcon.Times.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (clicked)
            {
                _configuration.WithdrawItems.RemoveAt(index);
                _configuration.Save();
                // If we remove, we can't continue drawing this row safely if we were iterating
                // But generally in ImGui immediate mode, we just return.
                return; 
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove");

            // Quantity Input
            ImGui.TableNextColumn();
            int qty = item.Quantity;
            
            int maxLimit = 249750;
            var sheetItem = Plugin.Data.GetExcelSheet<Item>()?.GetRow(item.ItemId);
            if (sheetItem != null && sheetItem.Value.StackSize > 999) maxLimit = 9999;

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputInt("##qty", ref qty, 0))
            {
                 qty = Math.Max(1, qty);
                 qty = Math.Min(maxLimit, qty);
                 item.Quantity = qty;
                 _configuration.Save();
            }

            // Max Button
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            bool maxClicked = ImGui.Button("Max");
            ImGui.PopStyleColor();

            if (maxClicked)
            {
                 long count = _helper.GetItemCountInChest(item.ItemId);
                 if (_configuration.LeaveOneItemPerStack && count > 0)
                     count--;
                 if (count > maxLimit) count = maxLimit;
                 item.Quantity = Math.Max(1, (int)count);
                 _configuration.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Set to max available (Limit: {maxLimit})");

            // Name
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(_helper.GetItemName(item.ItemId));

            // Have
            ImGui.TableNextColumn();
            long have = _helper.GetItemCountInChest(item.ItemId);
            ImGui.TextDisabled($"{have}");
        }

        private void DrawPresets()
        {
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(avail * 0.50f);
            
            if (ImGui.BeginCombo("##singlePresetSel", string.IsNullOrEmpty(_selectedPresetName) ? "Load Preset..." : _selectedPresetName))
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
            
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Times.ToIconString()) && !string.IsNullOrEmpty(_selectedPresetName))
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

            ImGui.SameLine(0, 5);
            
            // Export Button
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Export"))
            {
                if (Common.ExportHelper.Export(Common.ExportHelper.HEADER_SINGLES, _configuration.WithdrawItems))
                {
                    Common.ChatHelper.Info("Singles list exported to clipboard.");
                }
                else
                {
                    Common.ChatHelper.Warning("Failed to export singles list.");
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Export Singles List to clipboard");

            ImGui.SameLine(0, 5);
            
            // Import Button
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Import"))
            {
                var (result, data) = Common.ExportHelper.Import<List<WithdrawItem>>(Common.ExportHelper.HEADER_SINGLES);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    _configuration.WithdrawItems = data;
                    _configuration.Save();
                    Common.ChatHelper.Info($"Imported {data.Count} items to Singles list.");
                }
                else
                {
                    Common.ChatHelper.Warning(Common.ExportHelper.GetErrorMessage(result, "Singles"));
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import Singles List from clipboard");
        }

        private void LoadPreset(string name)
        {
            if (_configuration.SinglePresets.TryGetValue(name, out var items))
            {
                _selectedPresetName = name;
                // Deep copy
                _configuration.WithdrawItems = items.Select(x => new WithdrawItem { ItemId = x.ItemId, Quantity = x.Quantity }).ToList();
                _configuration.Save();
            }
        }

        private void DrawSavePresetModal()
        {
            if (_showSavePresetModal) ImGui.OpenPopup("Save Single Item Preset");

            if (ImGui.BeginPopupModal("Save Single Item Preset", ref _showSavePresetModal, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter Preset Name:");
                ImGui.InputText("##presetName", ref _presetNameInput, 64);
                
                if (ImGui.Button("Save", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_presetNameInput))
                    {
                        var listCopy = _configuration.WithdrawItems.Select(x => new WithdrawItem { ItemId = x.ItemId, Quantity = x.Quantity }).ToList();
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
