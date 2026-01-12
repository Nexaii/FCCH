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
using FC_Chest_Helper.Models;

namespace FC_Chest_Helper.UI
{
    public class WorkshopTab
    {
        private readonly FCChestHelper _helper;
        private readonly Configuration _configuration;
        private readonly WorkshopCache _cache;

        private string _searchFilter = "";
        
        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        public WorkshopTab(FCChestHelper helper, Configuration configuration, WorkshopCache cache)
        {
            _helper = helper;
            _configuration = configuration;
            _cache = cache;
        }

        public void Draw()
        {
            DrawPresets();
            ImGui.Separator();

            float footerHeight = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y * 3;

            if (ImGui.BeginChild("WorkshopScroll", new Vector2(0, -footerHeight), true))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Current Projects Layout");
                if (_helper.ShoppingList.Count == 0)
                {
                    ImGui.TextDisabled("No workshop projects active.");
                }
                else
                {
                     var grouped = _helper.ShoppingList
                         .OrderBy(x => x.Craft.Name)
                         .GroupBy(x => x.Craft.Name.Length > 0 ? char.ToUpper(x.Craft.Name[0]) : '?');
                     
                     foreach (var group in grouped)
                     {
                         if (ImGui.CollapsingHeader(group.Key.ToString(), ImGuiTreeNodeFlags.DefaultOpen))
                         {
                             foreach(var item in group)
                             {
                                 int idx = _helper.ShoppingList.IndexOf(item);
                                 if (idx != -1)
                                 {
                                     ImGui.PushID($"proj_{idx}");
                                     DrawProjectRow(item, idx);
                                     ImGui.PopID();
                                 }
                             }
                         }
                     }
                }               
                
                
                ImGui.Spacing();
                ImGui.Separator();
                if (ImGui.CollapsingHeader("Total Raw Materials Needed", ImGuiTreeNodeFlags.DefaultOpen))
                {
                     var totalMap = new Dictionary<uint, long>();
                     foreach(var shopItem in _helper.ShoppingList)
                     {
                         var mats = shopItem.Craft.Phases.SelectMany(p => p.Items).Select(x => new { Item = x, Req = x.TotalQuantity * shopItem.Quantity });
                         foreach(var mat in mats)
                         {
                             if (!totalMap.ContainsKey(mat.Item.ItemId)) totalMap[mat.Item.ItemId] = 0;
                             totalMap[mat.Item.ItemId] += mat.Req;
                         }
                     }
                     
                     if (totalMap.Count == 0)
                     {
                         ImGui.TextDisabled("No materials needed.");
                     }
                     else
                     {
                         if (ImGui.BeginTable("TotalMatsTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                         {
                             ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                             ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50);
                             ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 50);
                             ImGui.TableHeadersRow();

                             foreach(var kvp in totalMap.OrderBy(x => _helper.GetItemName(x.Key)))
                             {
                                 var name = _helper.GetItemName(kvp.Key);
                                 var need = kvp.Value;
                                 var haveFC = _helper.GetItemCountInChest(kvp.Key);
                                 var havePlayer = _helper.GetItemCountInPlayerInventory(kvp.Key);
                                 var have = haveFC + havePlayer;
                                 
                                 ImGui.TableNextRow();
                                 ImGui.TableNextColumn();
                                 ImGui.Text(name);
                                 ImGui.TableNextColumn();
                                 ImGui.Text(need.ToString());
                                 ImGui.TableNextColumn();
                                 
                                 if (have >= need) ImGui.TextColored(ImGuiColors.HealerGreen, have.ToString());
                                 else ImGui.TextColored(ImGuiColors.DalamudRed, have.ToString());
                             }
                             ImGui.EndTable();
                         }
                     }
                }
                
                ImGui.EndChild();
            }


            
            ImGui.Separator();
            
            ImGui.TextColored(ImGuiColors.HealerGreen, "Add New Project:");
            
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.4f, 0.4f, 0.4f, 1f));
            if (ImGui.BeginCombo("##addCraftSearch", "Search Project...", ImGuiComboFlags.HeightLarge))
            {
                 ImGui.PopStyleColor();
                 ImGui.InputText("##searchInC", ref _searchFilter, 64);
                 if (!string.IsNullOrEmpty(_searchFilter))
                 {
                       var filtered = _cache.Crafts.Where(c => c.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)).Take(20);
                       if (filtered.Any())
                       {
                           foreach (var craft in filtered)
                           {
                               if (ImGui.Selectable(craft.Name, false))
                               {
                                   AddProject(craft);
                                   ImGui.CloseCurrentPopup();
                               }
                           }
                       }
                       else 
                       {
                           ImGui.TextDisabled("No projects found");
                       }
                 }
                 ImGui.EndCombo();
            }
            else { ImGui.PopStyleColor(); }
            
            ImGui.SameLine();
            if (ImGui.Button("Refresh Chest Data"))
            {
                _helper.StartIndexing(false);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Force a full scan of all FC chest tabs.");

            DrawSavePresetModal();
        }

        private void AddProject(WorkshopCraft craft)
        {
             var existing = _helper.ShoppingList.FirstOrDefault(x => x.Craft.WorkshopItemId == craft.WorkshopItemId);
             if (existing != null)
             {
                 existing.Quantity++;
             }
             else
             {
                 var newItem = new ShoppingItem { Craft = craft, Quantity = 1 };
                 _helper.ShoppingList.Add(newItem);
             }
           
             _helper.ShoppingList.Sort((a, b) => string.Compare(a.Craft.Name, b.Craft.Name, StringComparison.OrdinalIgnoreCase));
             
             _searchFilter = "";
        }

        private void DrawProjectRow(ShoppingItem item, int index)
        {
            
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
            bool clicked = ImGui.Button(FontAwesomeIcon.Times.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (clicked)
            {
                _helper.ShoppingList.RemoveAt(index);
                return;
            }
            ImGui.SameLine();

            int qty = item.Quantity;
            ImGui.SetNextItemWidth(60);
            if (ImGui.InputInt("##pQty", ref qty))
            {
                if (qty < 1) qty = 1;
                item.Quantity = qty;
            }
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Max"))
            {
                item.Quantity = CalculateMaxCraft(item.Craft);
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Calculate max possible based on FC + Player inventory");

            ImGui.SameLine();

            bool expanded = ImGui.TreeNodeEx(item.Craft.Name, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.NoTreePushOnOpen);
          
            if (expanded)
            {
                ImGui.Indent();
                if (ImGui.BeginTable("IndivMatTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                     ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                     ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 40);
                     ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 40);
                     ImGui.TableHeadersRow();

                     var materials = item.Craft.Phases
                        .SelectMany(p => p.Items)
                        .Select(x => new { Item = x, Required = x.TotalQuantity * item.Quantity })
                        .GroupBy(x => x.Item.ItemId)
                        .Select(g => new 
                        {
                            ItemId = g.Key,
                            Name = g.First().Item.Name,
                            TotalNeeded = g.Sum(x => x.Required)
                        })
                        .OrderBy(m => m.Name.ToString());
                     
                     foreach(var mat in materials)
                     {
                         ImGui.TableNextRow();
                         ImGui.TableNextColumn();
                         ImGui.Text(mat.Name);
                         ImGui.TableNextColumn();
                         ImGui.Text($"{mat.TotalNeeded}");
                         
                         long haveFC = _helper.GetItemCountInChest(mat.ItemId);
                         long havePl = _helper.GetItemCountInPlayerInventory(mat.ItemId);
                         long total = haveFC + havePl;
                         ImGui.TableNextColumn();
                         ImGui.TextColored(total >= mat.TotalNeeded ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed, $"{total}");
                     }
                     ImGui.EndTable();
                }
                ImGui.Unindent();
            }
        }

        private void DrawTotalMaterials()
        {
             if (_helper.ShoppingList.Count == 0)
             {
                 ImGui.TextDisabled("Add a project to see material requirements.");
                 return;
             }

             if (ImGui.BeginTable("TotalMatsTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 200))) 
             {
                 ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
                 ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50);
                 ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 50);
                 ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 60);
                 ImGui.TableHeadersRow();

                 var totalMats = _helper.ShoppingList
                    .SelectMany(si => si.Craft.Phases.SelectMany(p => p.Items)
                        .Select(x => new { Item = x, Required = x.TotalQuantity * si.Quantity }))
                    .GroupBy(x => x.Item.ItemId)
                    .Select(g => new 
                    {
                        ItemId = g.Key,
                        Name = g.First().Item.Name,
                        TotalNeeded = g.Sum(x => x.Required)
                    })
                    .OrderBy(x => x.Name);
                 
                 foreach (var mat in totalMats)
                 {
                     ImGui.TableNextRow();
                     ImGui.TableNextColumn();
                     ImGui.Text(mat.Name);
                     
                     ImGui.TableNextColumn();
                     ImGui.Text($"{mat.TotalNeeded}");
                     
                     long haveFC = _helper.GetItemCountInChest(mat.ItemId);
                     long havePl = _helper.GetItemCountInPlayerInventory(mat.ItemId);
                     long total = haveFC + havePl;
                     
                     ImGui.TableNextColumn();
                     ImGui.Text($"{total}");

                     ImGui.TableNextColumn();
                     if (total >= mat.TotalNeeded) ImGui.TextColored(ImGuiColors.HealerGreen, "OK");
                     else ImGui.TextColored(ImGuiColors.DalamudRed, "Missing");
                 }
                 ImGui.EndTable();
             }
        }

        private void DrawPresets()
        {
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(avail * 0.50f);
            
            if (ImGui.BeginCombo("##workPresetSel", string.IsNullOrEmpty(_selectedPresetName) ? "Load Preset..." : _selectedPresetName))
            {
                foreach (var presetName in _configuration.WorkshopPresets.Keys)
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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Save Current Projects as Preset");

            ImGui.SameLine(0, 5);
            
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF0000FF);
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Times.ToIconString()) && !string.IsNullOrEmpty(_selectedPresetName))
            {
                if (_configuration.WorkshopPresets.ContainsKey(_selectedPresetName))
                {
                    _configuration.WorkshopPresets.Remove(_selectedPresetName);
                    _configuration.Save();
                    _selectedPresetName = "";
                }
            }
            ImGui.PopFont();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete Preset");

            ImGui.SameLine(0, 5);
            
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Export"))
            {
                var exportData = _helper.ShoppingList.Select(x => new PresetShoppingItem 
                { 
                    WorkshopItemId = x.Craft.WorkshopItemId, 
                    Quantity = x.Quantity 
                }).ToList();
                
                if (Common.ExportHelper.Export(Common.ExportHelper.HEADER_WORKSHOP, exportData))
                {
                    Common.ChatHelper.Info($"Exported {exportData.Count} workshop projects to clipboard.");
                }
                else
                {
                    Common.ChatHelper.Warning("Failed to export workshop projects.");
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Export current Workshop Projects to clipboard");

            ImGui.SameLine(0, 5);
            
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Import"))
            {
                var (result, data) = Common.ExportHelper.Import<List<PresetShoppingItem>>(Common.ExportHelper.HEADER_WORKSHOP);
                if (result == Common.ExportHelper.ImportResult.Success && data != null)
                {
                    _helper.ShoppingList.Clear();
                    foreach (var item in data)
                    {
                        var craft = _cache.Crafts.FirstOrDefault(c => c.WorkshopItemId == item.WorkshopItemId);
                        if (craft != null)
                        {
                            _helper.ShoppingList.Add(new ShoppingItem { Craft = craft, Quantity = item.Quantity });
                        }
                    }
                    Common.ChatHelper.Info($"Imported {data.Count} workshop projects.");
                }
                else
                {
                    Common.ChatHelper.Warning(Common.ExportHelper.GetErrorMessage(result, "Workshop"));
                }
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import Workshop Projects from clipboard");
        }

        private void LoadPreset(string name)
        {
             if (_configuration.WorkshopPresets.TryGetValue(name, out var savedItems))
             {
                 _selectedPresetName = name;
                 _helper.ShoppingList.Clear();
                 foreach (var item in savedItems)
                 {
                     var craft = _cache.Crafts.FirstOrDefault(c => c.WorkshopItemId == item.WorkshopItemId);
                     if (craft != null)
                     {
                         _helper.ShoppingList.Add(new ShoppingItem { Craft = craft, Quantity = item.Quantity });
                     }
                 }
             }
        }

        private void DrawSavePresetModal()
        {
            if (_showSavePresetModal) ImGui.OpenPopup("Save Workshop Preset");

            if (ImGui.BeginPopupModal("Save Workshop Preset", ref _showSavePresetModal, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter Preset Name:");
                ImGui.InputText("##wkPresetName", ref _presetNameInput, 64);
                
                if (ImGui.Button("Save", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_presetNameInput))
                    {
                        var listCopy = _helper.ShoppingList.Select(x => new PresetShoppingItem 
                        { 
                            WorkshopItemId = x.Craft.WorkshopItemId, 
                            Quantity = x.Quantity 
                        }).ToList();

                        _configuration.WorkshopPresets[_presetNameInput] = listCopy;
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
        
        private int CalculateMaxCraft(WorkshopCraft craft)
        {
             var materials = craft.Phases
                .SelectMany(p => p.Items)
                .Select(x => new { ItemId = x.ItemId, PerDraft = x.TotalQuantity })
                .GroupBy(x => x.ItemId)
                .Select(g => new { ItemId = g.Key, RequiredPerUnit = g.Sum(x => x.PerDraft) });

             long maxPossible = long.MaxValue;
             
             foreach (var mat in materials)
             {
                 long effectiveFC = _helper.GetItemCountInChest(mat.ItemId);
                 long player = _helper.GetItemCountInPlayerInventory(mat.ItemId);
                 long totalAvail = effectiveFC + player;
                 
                 long canMake = totalAvail / mat.RequiredPerUnit;
                 if (canMake < maxPossible) maxPossible = canMake;
             }
             
             if (maxPossible < 0) maxPossible = 0;
             if (maxPossible > 9999) maxPossible = 9999;
             return (int)maxPossible;
        }
    }
}
