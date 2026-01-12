using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Lumina.Excel.Sheets;
using FC_Chest_Helper;

namespace FC_Chest_Helper.UI
{
    public class IgnoreTab
    {
        private readonly FCChestHelper _helper;
        private readonly Configuration _configuration;
        
        // State
        private string _ignoreSearchFilter = string.Empty;
        
        // Caching
        private Item[] _filteredItemsCache = Array.Empty<Item>();
        private string _lastSearch = string.Empty;

        // Preset State
        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        public IgnoreTab(FCChestHelper helper, Configuration configuration)
        {
            _helper = helper;
            _configuration = configuration;
        }

        public void Draw()
        {
            // PRESETS (Top)
            DrawPresets();
            ImGui.Separator();

            // Calculate Footer Height for "Add Item" section
            float footerHeight = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y * 3;

            // Child List code - Unchanged in logic, just need to match structure
             if (_helper.Configuration.IgnoreList.Count == 0)
             {
                 ImGui.BeginChild("IgnoreListScroll", new Vector2(0, -footerHeight), true);
                 ImGui.TextDisabled("No items in ignore list.");
                 ImGui.EndChild();
             }
             else
             {
                 if (ImGui.BeginChild("IgnoreListScroll", new Vector2(0, -footerHeight), true))
                 {
                     if (ImGui.BeginTable("IgnoreListTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                     {
                         ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25); // Delete button
                         ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                         ImGui.TableSetupColumn("Deposit", ImGuiTableColumnFlags.WidthFixed, 60);
                         ImGui.TableSetupColumn("Withdraw", ImGuiTableColumnFlags.WidthFixed, 70);
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

                             // Column 1: Delete button
                             ImGui.TableNextColumn();
                             ImGui.PushFont(UiBuilder.IconFont);
                             ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
                             if (ImGui.Button(FontAwesomeIcon.Times.ToIconString()))
                             {
                                 _helper.Configuration.IgnoreList.Remove(gItem.Item);
                                 _helper.Configuration.Save();
                             }
                             ImGui.PopStyleColor();
                             ImGui.PopFont();

                             // Column 2: Item name
                             ImGui.TableNextColumn();
                             ImGui.Text(gItem.Name);

                             // Column 3: Deposit checkbox
                             ImGui.TableNextColumn();
                             bool entrust = gItem.Item.IgnoreEntrust;
                             if (ImGui.Checkbox("##dep", ref entrust))
                             {
                                 gItem.Item.IgnoreEntrust = entrust;
                                 _helper.Configuration.Save();
                             }

                             // Column 4: Withdraw checkbox
                             ImGui.TableNextColumn();
                             bool withdraw = gItem.Item.IgnoreWithdraw;
                             if (ImGui.Checkbox("##wit", ref withdraw))
                             {
                                 gItem.Item.IgnoreWithdraw = withdraw;
                                 _helper.Configuration.Save();
                             }

                             ImGui.PopID();
                         }
                         ImGui.EndTable();
                     }
                     ImGui.EndChild();
                 }
              }

            // ADD ITEM (Bottom/Sticky Footer)
            ImGui.Separator();
            
            ImGui.TextColored(ImGuiColors.HealerGreen, "Add Item to Ignore:"); 

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X );
            
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.4f, 0.4f, 0.4f, 1f)); 
            if (ImGui.BeginCombo("##ignoreSearch", "Search item...", ImGuiComboFlags.HeightLarge))
            {
                ImGui.PopStyleColor(); // Restore immediately inside
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
                ImGui.PopStyleColor();
            }

            DrawSavePresetModal();
        }

        private void UpdateSearchCache()
        {
            if (_ignoreSearchFilter == _lastSearch) return; // cache hit
            
            _lastSearch = _ignoreSearchFilter;
            var sheet = Plugin.Data.GetExcelSheet<Item>();
            if (sheet != null && !string.IsNullOrEmpty(_ignoreSearchFilter))
            {
                 // Filter: 
                 // 1. IsUntradable (Basic check for FC chest capability)
                 // 2. Exclude elemental items (IDs 2-19)
                 
                 _filteredItemsCache = sheet.Where(i => 
                    i.Name.ToString().Contains(_ignoreSearchFilter, StringComparison.OrdinalIgnoreCase) 
                    && !i.IsUntradable 
                    && !(i.RowId >= 2 && i.RowId <= 19) // Exclude elemental items!
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

        private void DrawIgnoreItemRow(Configuration.IgnoredItem item)
        {
             ImGui.PushFont(UiBuilder.IconFont);
             ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
             if (ImGui.Button(FontAwesomeIcon.Times.ToIconString()))
             {
                 _helper.Configuration.IgnoreList.Remove(item);
                 _helper.Configuration.Save();
                 ImGui.PopStyleColor();
                 ImGui.PopFont();
                 return;
             }
             ImGui.PopStyleColor();
             ImGui.PopFont();
             
             ImGui.SameLine();
             ImGui.AlignTextToFramePadding();
             ImGui.Text(item.Name);

             ImGui.SameLine();
             // Spacers for alignment (Reduced buffer to accommodate longer text)
             float avail = ImGui.GetContentRegionAvail().X;
             ImGui.Dummy(new Vector2(avail - 250, 0)); // Push checkboxes to right
             
             ImGui.SameLine();
             bool entrust = item.IgnoreEntrust;
             if (ImGui.Checkbox("Deposit", ref entrust))
             {
                 item.IgnoreEntrust = entrust;
                 _helper.Configuration.Save();
             }
             if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ignore during Deposit");

             ImGui.SameLine();
             bool withdraw = item.IgnoreWithdraw;
             if (ImGui.Checkbox("Withdraw", ref withdraw))
             {
                 item.IgnoreWithdraw = withdraw;
                 _helper.Configuration.Save();
             }
             if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ignore during Withdraw");
        }

        private void DrawPresets()
        {
            // Reduced width to fit all buttons
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(avail * 0.50f);
            
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
            
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Times.ToIconString()) && !string.IsNullOrEmpty(_selectedPresetName))
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

            ImGui.SameLine(0, 5);
            
            // Export Button
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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Export Ignore List to clipboard");

            ImGui.SameLine(0, 5);
            
            // Import Button
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Import"))
            {
                var (result, data) = Common.ExportHelper.Import<List<Configuration.IgnoredItem>>(Common.ExportHelper.HEADER_IGNORE);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    // Merge or replace
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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import Ignore List from clipboard");
        }

        private void LoadPreset(string name)
        {
            if (_configuration.IgnorePresets.TryGetValue(name, out var items))
            {
                _selectedPresetName = name;
                // Deep copy
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
