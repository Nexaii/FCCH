using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Managers;
using FCCH.Managers.Organizer;
using FCCH.Common;

namespace FCCH.UI
{
    public class OrganizerTab : IDisposable
    {
        private readonly OrgService _service;
        private readonly Configuration _config;
        private readonly ChestHelper _helper;

        private int _selectedModeIndex = 0;
        private int _selectedSourceIndex = 1;
        private int _selectedDestIndex = 2;
        private HashSet<OrgFilterCategory> _selectedFilters = new() { OrgFilterCategory.AllItems };
        private int _selectedSortIndex = 0;
        private bool _sortDescending = false;
        private bool _wasChestOpen = false;
        private int _chestClosedFrames = 0;
        private const int CHEST_CLOSE_THRESHOLD = 3;

        public OrganizerTab(OrgService service, Configuration config, ChestHelper helper)
        {
            _service = service;
            _config = config;
            _helper = helper;
            SyncRequestFromUI();
        }

        private void SyncRequestFromUI()
        {
            var tabs = OrgService.GetAvailableTabs();
            _service.CurrentRequest.Mode = _selectedModeIndex switch
            {
                0 => OrgOperationMode.Move,
                _ => OrgOperationMode.Sort
            };
            _service.CurrentRequest.SourceTab = tabs[_selectedSourceIndex];

            if (_service.CurrentRequest.Mode == OrgOperationMode.Sort)
                _service.CurrentRequest.DestTab = tabs[_selectedSourceIndex];
            else
                _service.CurrentRequest.DestTab = tabs[_selectedDestIndex];

            _service.CurrentRequest.Filters = new HashSet<OrgFilterCategory>(_selectedFilters);
            _service.CurrentRequest.SortOrder = Enum.GetValues<OrgSortOrder>()[_selectedSortIndex];
            _service.CurrentRequest.SortDescending = _sortDescending;
        }

        public void Draw()
        {
            var tabs = OrgService.GetAvailableTabs();
            var filterCategories = Enum.GetValues<OrgFilterCategory>();
            var sortOrders = Enum.GetValues<OrgSortOrder>();
            var check = _service.LastCheck;
            var status = _service.JobStatus;
            var style = ImGui.GetStyle();

            bool isMove = _selectedModeIndex == 0;
            bool isSort = _selectedModeIndex == 1;

            if (_selectedSourceIndex == 0)
            {
                _selectedSourceIndex = 1;
                if (_selectedDestIndex == 1) _selectedDestIndex = 2;
            }

            float footerHeight = ImGui.GetFrameHeight() * 2 + style.ItemSpacing.Y;

            float previewHeight = ImGui.GetContentRegionAvail().Y * 0.37f;
            if (previewHeight < 100) previewHeight = 100;

            float settingsHeight = ImGui.GetContentRegionAvail().Y - footerHeight - previewHeight - style.ItemSpacing.Y * 2;

            if (ImGui.BeginChild("SettingsPane", new Vector2(0, settingsHeight), true))
            {
                if (ImGui.BeginTable("ModeTable", 2, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("Col1", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Col2", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    Vector4 moveBg = isMove ? style.Colors[(int)ImGuiCol.TabActive] : style.Colors[(int)ImGuiCol.FrameBg];
                    ImGui.PushStyleColor(ImGuiCol.Button, moveBg);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                    if (status == OrgJobStatus.Running) ImGui.BeginDisabled();
                    if (ImGui.Button("Move", new Vector2(-1, 30)))
                    {
                        _selectedModeIndex = 0;
                        SyncAndInvalidate();
                    }
                    if (status == OrgJobStatus.Running) ImGui.EndDisabled();
                    ImGui.PopStyleColor(2);
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(status == OrgJobStatus.Running ? "Operation in progress" : "Transfer items between tabs");

                    ImGui.TableNextColumn();
                    Vector4 sortBg = isSort ? style.Colors[(int)ImGuiCol.TabActive] : style.Colors[(int)ImGuiCol.FrameBg];
                    ImGui.PushStyleColor(ImGuiCol.Button, sortBg);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                    if (status == OrgJobStatus.Running) ImGui.BeginDisabled();
                    if (ImGui.Button("Sort", new Vector2(-1, 30)))
                    {
                        _selectedModeIndex = 1;
                        SyncAndInvalidate();
                    }
                    if (status == OrgJobStatus.Running) ImGui.EndDisabled();
                    ImGui.PopStyleColor(2);
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(status == OrgJobStatus.Running ? "Operation in progress" : "Reorder items within a tab");

                    ImGui.EndTable();
                }

                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Transfer Settings", ImGuiTreeNodeFlags.Leaf);
                if (ImGui.BeginTable("TransferTable", 4, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("L1", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("D1", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("L2", ImGuiTableColumnFlags.WidthFixed, 30);
                    ImGui.TableSetupColumn("D2", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableNextRow();

                    if (isSort)
                    {
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Tab:");
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##TargetTab", OrgService.GetTabDisplayName(tabs[_selectedSourceIndex])))
                        {
                            for (int i = 1; i < tabs.Length; i++)
                            {
                                if (ImGui.Selectable(OrgService.GetTabDisplayName(tabs[i]), i == _selectedSourceIndex))
                                {
                                    _selectedSourceIndex = i;
                                    _selectedDestIndex = i;
                                    SyncAndInvalidate();
                                }
                            }
                            ImGui.EndCombo();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Tab to sort items in");
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                    }
                    else
                    {
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("From:");
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##FromTab", OrgService.GetTabDisplayName(tabs[_selectedSourceIndex])))
                        {
                            for (int i = 1; i < tabs.Length; i++)
                            {
                                if (ImGui.Selectable(OrgService.GetTabDisplayName(tabs[i]), i == _selectedSourceIndex))
                                {
                                    _selectedSourceIndex = i;
                                    if (_selectedDestIndex == _selectedSourceIndex)
                                    {
                                        for (int j = 1; j < tabs.Length; j++)
                                        {
                                            if (j != _selectedSourceIndex)
                                            {
                                                _selectedDestIndex = j;
                                                break;
                                            }
                                        }
                                    }
                                    SyncAndInvalidate();
                                }
                            }
                            ImGui.EndCombo();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Source tab to take items from");

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("To:");
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##ToTab", OrgService.GetTabDisplayName(tabs[_selectedDestIndex])))
                        {
                            for (int i = 1; i < tabs.Length; i++)
                            {
                                bool isSource = (i == _selectedSourceIndex);
                                if (isSource) ImGui.BeginDisabled();
                                if (ImGui.Selectable(OrgService.GetTabDisplayName(tabs[i]), i == _selectedDestIndex))
                                {
                                    _selectedDestIndex = i;
                                    SyncAndInvalidate();
                                }
                                if (isSource) ImGui.EndDisabled();
                            }
                            ImGui.EndCombo();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Destination tab to place items");
                    }
                    ImGui.EndTable();
                }

                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader("Filter", ImGuiTreeNodeFlags.Leaf);
                
                var sortedCategories = new List<OrgFilterCategory> { OrgFilterCategory.AllItems };
                var otherCats = filterCategories.Where(c => c != OrgFilterCategory.AllItems).OrderBy(c => GetFilterShortName(c)).ToList();
                sortedCategories.AddRange(otherCats);

                if (ImGui.BeginTable("FilterGrid", 4, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("C1", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("C2", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("C3", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("C4", ImGuiTableColumnFlags.WidthStretch);

                    for (int i = 0; i < sortedCategories.Count; i++)
                    {
                        if (i % 4 == 0) ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        var cat = sortedCategories[i];
                        bool isChecked = _selectedFilters.Contains(cat);
                        string label = GetFilterShortName(cat);

                        if (ImGui.Checkbox($"{label}##F{i}", ref isChecked))
                        {
                            if (cat == OrgFilterCategory.AllItems)
                            {
                                _selectedFilters.Clear();
                                _selectedFilters.Add(OrgFilterCategory.AllItems);
                            }
                            else
                            {
                                if (isChecked)
                                {
                                    _selectedFilters.Remove(OrgFilterCategory.AllItems);
                                    _selectedFilters.Add(cat);
                                }
                                else
                                {
                                    _selectedFilters.Remove(cat);
                                }
                                if (_selectedFilters.Count == 0) _selectedFilters.Add(OrgFilterCategory.AllItems);
                            }
                            SyncAndInvalidate();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Include {label}");
                    }
                    ImGui.EndTable();
                }

                ImGui.Spacing();

                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader(isSort ? "Sort Order" : "Sort Order", ImGuiTreeNodeFlags.Leaf);

                var sortOptions = new[] {
                    (Value: OrgSortOrder.ByCategory, Label: "Category"),
                    (Value: OrgSortOrder.ById, Label: "ID"),
                    (Value: OrgSortOrder.ByName, Label: "Name"),
                    (Value: OrgSortOrder.ByQuantity, Label: "Qty")
                };

                if (ImGui.BeginTable("OrderLayout", 2, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("ComboCol", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("RevCol", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    OrgSortOrder currentOrder = sortOrders[_selectedSortIndex];
                    string currentLabel = sortOptions.FirstOrDefault(x => x.Value == currentOrder).Label ?? currentOrder.ToString();

                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##SortCombo", currentLabel))
                    {
                        foreach (var opt in sortOptions)
                        {
                            bool isSelected = opt.Value == currentOrder;
                            if (ImGui.Selectable(opt.Label, isSelected))
                            {
                                _selectedSortIndex = Array.IndexOf(sortOrders, opt.Value);
                                SyncAndInvalidate();
                            }
                        }
                        ImGui.EndCombo();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("How to order items");

                    ImGui.TableNextColumn();
                    if (ImGui.Checkbox("Reverse", ref _sortDescending))
                    {
                        SyncAndInvalidate();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reverse the sort order");

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.Spacing();

            int previewCount = check?.StackCount ?? 0;
            if (ImGui.BeginChild("PreviewPane", new Vector2(0, previewHeight), true))
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                ImGui.CollapsingHeader($"Preview ({previewCount} items)", ImGuiTreeNodeFlags.Leaf);

                if (ImGui.BeginTable("PreviewTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableHeadersRow();

                    if (check != null && check.PreviewItems.Count > 0)
                    {
                        foreach (var item in check.PreviewItems)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            string mergeTag = item.WillMerge ? " (M)" : "";
                            ItemNameDisplay.Text(item.ItemId, item.ItemName, _config, mergeTag, item.WillMerge ? "Will merge with existing stack" : null);

                            ImGui.TableNextColumn();
                            ImGui.Text($"{item.Quantity}");

                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(item.CategoryName);
                        }
                    }
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Click 'Check' to preview...");
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                    }
                    ImGui.EndTable();
                }
                ImGui.EndChild();
            }

            if (ImGui.BeginTable("FooterTable", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Button", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (status == OrgJobStatus.Running)
                {
                    ImGui.Text($"Status: Running ({_service.CompletedMoves}/{_service.TotalMoves})");
                }
                else if (status == OrgJobStatus.Completed)
                {
                    ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "Status: Completed!");
                }
                else if (status == OrgJobStatus.Failed)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.2f, 0.2f, 1.0f), $"Status: {_service.StatusMessage}");
                }
                else if (check != null && check.IsValid)
                {
                    ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), $"Status: Ready ({check.StackCount} items)");
                    ImGui.SameLine();
                    var pColor = check.PlayerBufferOK ? new Vector4(0.5f, 0.8f, 0.5f, 1.0f) : new Vector4(0.8f, 0.5f, 0.5f, 1.0f);
                    var dColor = check.DestCapacityOK ? new Vector4(0.5f, 0.8f, 0.5f, 1.0f) : new Vector4(0.8f, 0.5f, 0.5f, 1.0f);
                    ImGui.TextColored(pColor, $"| Player: {check.PlayerFreeSlots}");
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Free slots in player inventory");
                    ImGui.SameLine();
                    ImGui.TextColored(dColor, $"| Dest: {check.DestFreeSlots}");
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Free slots in destination");
                }
                else if (check != null)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.2f, 0.2f, 1.0f), $"Status: {check.StatusMessage}");
                }
                else
                {
                    ImGui.TextDisabled("Status: Not checked");
                }

                ImGui.TableNextColumn();
                bool isRunning = status == OrgJobStatus.Running;
                bool conflict = !isSort && (_selectedSourceIndex == _selectedDestIndex);
                var gate = _helper.CanStartUserAction();
                bool blocked = !gate.CanRun && !isRunning;
                bool canRun = !conflict && check != null && check.IsValid;
                string buttonLabel = isRunning ? "Cancel" : (canRun ? GetActionLabel() : "Check");

                if (conflict || blocked) ImGui.BeginDisabled();
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isRunning ? new Vector4(0.8f, 0.2f, 0.2f, 1.0f) : style.Colors[(int)ImGuiCol.TabHovered]);
                if (ImGui.Button(buttonLabel, new Vector2(-1, 30)))
                {
                    if (isRunning)
                        _service.Cancel();
                    else if (canRun)
                        _helper.TryStartUserAction(() => _service.Run());
                    else
                        _helper.TryStartUserAction(() => _service.Check());
                }
                ImGui.PopStyleColor();
                if (conflict || blocked) ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    if (conflict) ImGui.SetTooltip("Source and Destination cannot be the same");
                    else if (blocked) ImGui.SetTooltip(gate.Reason);
                    else if (isRunning) ImGui.SetTooltip("Cancel the current operation");
                    else if (canRun) ImGui.SetTooltip("Execute the operation");
                    else ImGui.SetTooltip("Check if operation is valid");
                }

                ImGui.EndTable();
            }
        }

        private string GetActionLabel()
        {
            return _selectedModeIndex switch
            {
                0 => "Move Items",
                1 => "Sort Items",
                _ => "Execute"
            };
        }

        private static string GetFilterShortName(OrgFilterCategory cat)
        {
            return cat switch
            {
                OrgFilterCategory.AllItems => "All Items",
                OrgFilterCategory.Equipment => "Equipment",
                OrgFilterCategory.MedicinesMeals => "Med/Meals",
                OrgFilterCategory.Materials => "Materials",
                OrgFilterCategory.Materia => "Materia",
                OrgFilterCategory.Registrable => "Registrable",
                OrgFilterCategory.Dye => "Dye",
                OrgFilterCategory.Housing => "Housing",
                OrgFilterCategory.Gardening => "Gardening",
                OrgFilterCategory.Miscellaneous => "Misc",
                _ => cat.ToString()
            };
        }

        private void SyncAndInvalidate()
        {
            SyncRequestFromUI();
            _service.Reset();
        }

        private void DebugLog(string msg)
        {
            if (!_config.DebugMode) return;
            Plugin.PluginLog.Info($"[OrganizerTab] {msg}");
            ChatHelper.Debug($"[OrgTab] {msg}");
        }

        public unsafe void Update()
        {
            _service.Update();

            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
            bool isChestOpen = addon != null && addon->IsVisible;

            if (_wasChestOpen && !isChestOpen)
            {
                _chestClosedFrames++;
                if (_chestClosedFrames >= CHEST_CLOSE_THRESHOLD)
                {
                    DebugLog("Chest closed (confirmed). Resetting check.");
                    _service.Reset();
                    SyncRequestFromUI();
                    _chestClosedFrames = 0;
                }
            }
            else
            {
                _chestClosedFrames = 0;
            }
            _wasChestOpen = isChestOpen;
        }

        public void Dispose()
        {
        }
    }
}
