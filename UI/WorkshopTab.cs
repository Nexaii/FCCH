using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Component.GUI;

using Lumina.Excel.Sheets;
using FCCH.GameData;
using FCCH.Models;
using FCCH.Managers;

namespace FCCH.UI
{
    public unsafe class WorkshopTab
    {
        private readonly ChestHelper _helper;
        private readonly Configuration _configuration;
        private readonly WorkshopCache _cache;
        private readonly Common.WorkshoppaIPC _ipc;

        private string _searchFilter = "";

        private string _presetNameInput = "";
        private string _selectedPresetName = "";
        private bool _showSavePresetModal = false;

        private HashSet<int> _expandedProjects = new HashSet<int>();

        private bool _wasTabActive;
        private int _lastShoppingListSignature;

        public WorkshopTab(ChestHelper helper, Configuration configuration, WorkshopCache cache, Common.WorkshoppaIPC workshoppaIpc)
        {
            _helper = helper;
            _configuration = configuration;
            _cache = cache;
            _ipc = workshoppaIpc;
        }

        public void Draw()
        {
            MaybeAutoRefresh();

            DrawPresets();
            ImGui.Separator();

            var totalMats = GetTotalMaterials();
            int missingCount = totalMats.Count(m => m.Have < m.Need);
            int projectCount = _helper.ShoppingList.Count;

            if (ImGui.BeginTable("##workshopHeader", 4))
            {
                ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##clearlist", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("##queue", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("##clearws", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextDisabled($"Projects ({projectCount}) | Materials: {totalMats.Count}, {missingCount} missing");

                ImGui.TableNextColumn();
                if (projectCount > 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("Clear List", new Vector2(-1, 0)))
                    {
                        _helper.ShoppingList.Clear();
                        _expandedProjects.Clear();
                    }
                    ImGui.PopStyleColor();
                }

                ImGui.TableNextColumn();
                if (projectCount > 0 && _ipc.IsAvailable)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("Queue", new Vector2(-1, 0)))
                    {
                        int success = 0;
                        foreach (var item in _helper.ShoppingList)
                        {
                            if (_ipc.AddQueueItem(item.Craft.WorkshopItemId, item.Quantity))
                                success++;
                        }
                        if (success > 0)
                            Common.ChatHelper.Info($"Queued {success} projects to Workshoppa.");
                        else
                            Common.ChatHelper.Warning("Failed to queue \u2014 is Workshoppa busy?");
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Send projects to Workshoppa queue");
                }

                ImGui.TableNextColumn();
                if (_ipc.IsAvailable)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("Clear WS", new Vector2(-1, 0)))
                    {
                        if (_ipc.ClearQueue())
                            Common.ChatHelper.Info("Workshoppa queue cleared.");
                        else
                            Common.ChatHelper.Warning("Failed to clear \u2014 is Workshoppa busy?");
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear Workshoppa queue");
                }
                ImGui.EndTable();
            }

            float footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2;

            float matsHeight = 0;
            var storage = ImGui.GetStateStorage();
            uint matsId = ImGui.GetID("TotalMaterialsHeader");
            bool matsOpen = storage.GetBool(matsId, true);

            if (matsOpen)
            {
                matsHeight = ImGui.GetContentRegionAvail().Y * 0.30f;
                if (matsHeight < 120) matsHeight = 120;
            }
            else
            {
                matsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
            }

            float projectsHeight = ImGui.GetContentRegionAvail().Y - footerHeight - matsHeight - ImGui.GetStyle().ItemSpacing.Y;

            if (ImGui.BeginChild("ProjectsPane", new Vector2(0, projectsHeight), true))
            {
                if (_helper.ShoppingList.Count == 0)
                {
                    ImGui.TextDisabled("No workshop projects.");
                    ImGui.TextDisabled("Use search below to add.");
                }
                else
                {
                    var sortedProjects = _helper.ShoppingList
                        .Select((item, idx) => new { Item = item, Index = idx })
                        .OrderBy(x => x.Item.Craft.Name)
                        .ToList();

                    foreach (var proj in sortedProjects)
                    {
                        ImGui.PushID($"proj_{proj.Index}");
                        DrawProjectRow(proj.Item, proj.Index);
                        ImGui.PopID();
                    }
                }
                ImGui.EndChild();
            }

            ImGui.Spacing();

            if (ImGui.BeginChild("WorkshopMaterialsLoc", new Vector2(0, matsHeight), true))
            {
                if (ImGui.CollapsingHeader($"Total Materials Needed ({totalMats.Count})###TotalMaterialsHeader", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (totalMats.Count == 0)
                    {
                        ImGui.TextDisabled("No materials needed.");
                    }
                    else
                    {
                        if (ImGui.BeginTable("TotalMatsTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                        {
                            ImGui.TableSetupScrollFreeze(0, 1);
                            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50);
                            ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 50);
                            ImGui.TableSetupColumn("Diff", ImGuiTableColumnFlags.WidthFixed, 60);
                            ImGui.TableHeadersRow();

                            foreach (var mat in totalMats)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.Text(mat.Name);

                                ImGui.TableNextColumn();
                                ImGui.Text(mat.Need.ToString());

                                ImGui.TableNextColumn();
                                if (mat.Have >= mat.Need)
                                    ImGui.TextColored(ImGuiColors.HealerGreen, mat.Have.ToString());
                                else
                                    ImGui.TextColored(ImGuiColors.DalamudRed, mat.Have.ToString());

                                ImGui.TableNextColumn();
                                long diff = mat.Have - mat.Need;
                                if (diff >= 0)
                                    ImGui.TextColored(ImGuiColors.HealerGreen, $"+{diff}");
                                else
                                    ImGui.TextColored(ImGuiColors.DalamudRed, diff.ToString());
                            }
                            ImGui.EndTable();
                        }
                    }
                }
                ImGui.EndChild();
            }

            DrawSearchBox();

            DrawSavePresetModal();
        }

        private void DrawSearchBox()
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.2f, 0.2f, 0.2f, 1f));

            float refreshWidth = 80;
            float searchWidth = ImGui.GetContentRegionAvail().X - refreshWidth - ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetNextItemWidth(searchWidth);
            if (ImGui.BeginCombo("##addCraftSearch", "Search workshop projects...", ImGuiComboFlags.HeightLarge | ImGuiComboFlags.PopupAlignLeft))
            {
                ImGui.PopStyleColor(2);
                ImGui.SetNextItemWidth(-1);
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
            else
            {
                ImGui.PopStyleColor(2);
            }

            ImGui.SameLine();
            var gate = _helper.CanStartUserAction();
            if (!gate.CanRun) ImGui.BeginDisabled();
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
            if (ImGui.Button("Refresh", new Vector2(refreshWidth, 0)))
            {
                TryRefreshChestData();
            }
            ImGui.PopStyleColor();
            if (!gate.CanRun) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(!gate.CanRun
                    ? gate.Reason
                    : IsChestAddonVisible()
                    ? "Refresh Chest Data"
                    : "Refresh Chest Data\nOpen the Company Chest to update.");
            }
        }

        private void MaybeAutoRefresh()
        {
            int signature = ComputeShoppingListSignature();
            bool tabJustActivated = !_wasTabActive;
            bool listChanged = signature != _lastShoppingListSignature;
            _wasTabActive = true;

            if (_helper.ShoppingList.Count == 0)
            {
                _lastShoppingListSignature = signature;
                return;
            }

            if (!tabJustActivated && !listChanged) return;

            _lastShoppingListSignature = signature;

            TryRefreshChestData();
        }

        public void OnTabDeactivated()
        {
            _wasTabActive = false;
        }

        private void TryRefreshChestData()
        {
            if (!_helper.CanStartUserAction().CanRun) return;
            if (!IsChestAddonVisible()) return;
            _helper.StartIndexing(false);
        }

        private static bool IsChestAddonVisible()
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest", 1);
            return addon != null && addon->IsVisible;
        }

        private int ComputeShoppingListSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _helper.ShoppingList.Count;
                foreach (var item in _helper.ShoppingList)
                {
                    hash = hash * 31 + (int)item.Craft.WorkshopItemId;
                    hash = hash * 31 + item.Quantity;
                }
                return hash;
            }
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
            var materials = item.Craft.Phases
                .SelectMany(p => p.Items)
                .Select(x => new { Item = x, Required = x.TotalQuantity * item.Quantity })
                .GroupBy(x => x.Item.ItemId)
                .Select(g => new { ItemId = g.Key, Name = g.First().Item.Name, TotalNeeded = g.Sum(x => x.Required) })
                .OrderBy(m => m.Name)
                .ToList();

            int readyCount = 0;
            int totalCount = materials.Count;
            foreach (var mat in materials)
            {
                long have = _helper.GetItemCountInChest(mat.ItemId) + _helper.GetItemCountInPlayerInventory(mat.ItemId);
                if (have >= mat.TotalNeeded) readyCount++;
            }

            bool isReady = readyCount == totalCount;
            bool isExpanded = _expandedProjects.Contains(index);

            if (ImGui.BeginTable($"ProjectRow{index}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Project", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Max", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);
                ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, CellActionButton.ColumnWidth);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                string arrow = isExpanded ? "▼" : "▶";

                if (ImGui.Selectable($"{arrow} {item.Craft.Name}##sel{index}", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap))
                {
                    if (isExpanded)
                        _expandedProjects.Remove(index);
                    else
                        _expandedProjects.Add(index);
                }

                ImGui.TableNextColumn();
                int qty = item.Quantity;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputInt($"##pQty{index}", ref qty, 0))
                {
                    if (qty < 1) qty = 1;
                    item.Quantity = qty;
                }

                ImGui.TableNextColumn();
                if (isReady)
                    ImGui.TextColored(ImGuiColors.HealerGreen, "OK");
                else
                    ImGui.TextColored(ImGuiColors.DalamudOrange, $"{readyCount}/{totalCount}");

                ImGui.TableNextColumn();
                CellActionButton.DrawText("M", $"max{index}", "Max craftable", () =>
                {
                    item.Quantity = CalculateMaxCraft(item.Craft);
                });

                ImGui.TableNextColumn();
                CellActionButton.DrawIcon(FontAwesomeIcon.Minus, $"delete{index}", "Remove", () =>
                {
                    _helper.ShoppingList.RemoveAt(index);
                    _expandedProjects.Remove(index);
                }, true);

                ImGui.EndTable();
            }

            if (isExpanded)
            {
                ImGui.Indent(20);
                if (ImGui.BeginTable($"MatTable{index}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableHeadersRow();

                    foreach (var mat in materials)
                    {
                        long haveFC = _helper.GetItemCountInChest(mat.ItemId);
                        long havePl = _helper.GetItemCountInPlayerInventory(mat.ItemId);
                        long total = haveFC + havePl;
                        bool isComplete = total >= mat.TotalNeeded;

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(mat.Name);
                        ImGui.TableNextColumn();
                        ImGui.Text(mat.TotalNeeded.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextColored(isComplete ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed, total.ToString());
                    }
                    ImGui.EndTable();
                }
                ImGui.Unindent(20);
                ImGui.Spacing();
            }
        }

        private List<(string Name, uint ItemId, long Need, long Have)> GetTotalMaterials()
        {
            var totalMap = new Dictionary<uint, long>();
            foreach (var shopItem in _helper.ShoppingList)
            {
                var mats = shopItem.Craft.Phases.SelectMany(p => p.Items).Select(x => new { Item = x, Req = x.TotalQuantity * shopItem.Quantity });
                foreach (var mat in mats)
                {
                    if (!totalMap.ContainsKey(mat.Item.ItemId)) totalMap[mat.Item.ItemId] = 0;
                    totalMap[mat.Item.ItemId] += mat.Req;
                }
            }

            return totalMap
                .Select(kvp =>
                {
                    var name = _helper.GetItemName(kvp.Key);
                    var haveFC = _helper.GetItemCountInChest(kvp.Key);
                    var havePlayer = _helper.GetItemCountInPlayerInventory(kvp.Key);
                    return (Name: name, ItemId: kvp.Key, Need: kvp.Value, Have: haveFC + havePlayer);
                })
                .OrderBy(x => x.Name)
                .ToList();
        }

        private void DrawPresets()
        {
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(avail * 0.45f);

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

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()) && !string.IsNullOrEmpty(_selectedPresetName))
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

            ImGui.SameLine(0, 15);

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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Export to clipboard");

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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import from clipboard");
        }

        private void LoadPreset(string name)
        {
            if (_configuration.WorkshopPresets.TryGetValue(name, out var savedItems))
            {
                _selectedPresetName = name;
                _helper.ShoppingList.Clear();
                _expandedProjects.Clear();
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

                ImGui.Spacing();

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
            return Math.Max(1, (int)maxPossible);
        }
    }
}
