using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FCCH.Managers;
using FCCH;

namespace FCCH.UI
{
    public unsafe class CrystalTabUI
    {
        private readonly global::FCCH.Configuration _configuration;
        private readonly global::FCCH.Managers.CrystalManager _manager;
        private readonly ChestHelper _helper;

        private static readonly string[] ElementNames = { "Fire", "Ice", "Wind", "Earth", "Lightning", "Water" };
        private static readonly string[] ElementIcons = { "\uE0C6", "\uE0C7", "\uE0C9", "\uE0C8", "\uE0CA", "\uE0CB" };

        private static readonly uint[][] RowIds = {
            new uint[] { 2, 8, 14 },
            new uint[] { 3, 9, 15 },
            new uint[] { 5, 11, 17 },
            new uint[] { 4, 10, 16 },
            new uint[] { 6, 12, 18 },
            new uint[] { 7, 13, 19 }
        };

        public CrystalTabUI(global::FCCH.Configuration configuration, global::FCCH.Managers.CrystalManager manager, ChestHelper helper)
        {
            _configuration = configuration;
            _manager = manager;
            _helper = helper;
        }

        public void Draw()
        {
            var config = _configuration.CrystalConfig;
            var style = ImGui.GetStyle();

            if (ImGui.BeginChild("CrystalGlobalBox", new Vector2(0, 0), true))
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.15f, 0.5f));
                if (ImGui.BeginChild("GlobalSettings", new Vector2(0, 70), true))
                {
                    bool dep = config.IncludeInDepositAll;
                    if (ImGui.Checkbox("Include in Deposits", ref dep))
                    {
                        config.IncludeInDepositAll = dep;
                        _configuration.Save();
                    }
                    ImGui.SameLine(220);
                    bool wit = config.IncludeInWithdrawAll;
                    if (ImGui.Checkbox("Include in Withdrawals", ref wit))
                    {
                        config.IncludeInWithdrawAll = wit;
                        _configuration.Save();
                    }

                    ImGui.Spacing();

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Global Keep Amount:");
                    ImGui.SameLine();
                    int keep = config.GlobalKeepAmount;
                    ImGui.SetNextItemWidth(170);
                    if (ImGui.SliderInt("##GlobalSlider", ref keep, 0, 9999))
                    {
                        config.GlobalKeepAmount = keep;
                        _configuration.Save();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("(0-9999)");
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("Reset to Global"))
                    {
                        config.CustomKeepAmounts.Clear();
                        _configuration.Save();
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset all crystals to Global Keep Amount");
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();

                ImGui.Spacing();

                if (ImGui.BeginTable("CrystalTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.NoBordersInBody))
                {
                    ImGui.TableSetupColumn("Element", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Shards", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Crystals", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Clusters", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.FrameBg]);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                    if (ImGui.Button("ALL", new Vector2(-1, 0))) ToggleAll();

                    ImGui.TableNextColumn();
                    if (ImGui.Button("Shards", new Vector2(-1, 0))) ToggleCol(global::FCCH.Managers.CrystalManager.ShardIds);

                    ImGui.TableNextColumn();
                    if (ImGui.Button("Crystals", new Vector2(-1, 0))) ToggleCol(global::FCCH.Managers.CrystalManager.CrystalIds);

                    ImGui.TableNextColumn();
                    if (ImGui.Button("Clusters", new Vector2(-1, 0))) ToggleCol(global::FCCH.Managers.CrystalManager.ClusterIds);
                    ImGui.PopStyleColor(2);

                    for (int r = 0; r < 6; r++)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.FrameBg]);
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                        if (ImGui.Button($"{ElementNames[r]}", new Vector2(-1, 0))) ToggleRow(RowIds[r]);
                        ImGui.PopStyleColor(2);

                        ImGui.TableNextColumn(); DrawCell(RowIds[r][0]);
                        ImGui.TableNextColumn(); DrawCell(RowIds[r][1]);
                        ImGui.TableNextColumn(); DrawCell(RowIds[r][2]);
                    }
                    ImGui.EndTable();
                }

                ImGui.Spacing();

                string legendText = "Left click: Toggle On/Off  |  Right click: Custom amount  |  * = Overrides Global";
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                float legendWidth = ImGui.CalcTextSize(legendText).X;
                ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - legendWidth) * 0.5f);
                ImGui.Text(legendText);
                ImGui.PopStyleColor();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float btnWidth = 140;
                float spacing = style.ItemSpacing.X;
                float totalGroupWidth = (btnWidth * 2) + spacing;
                float cursorX = (ImGui.GetContentRegionAvail().X - totalGroupWidth) * 0.5f;
                if (cursorX < 0) cursorX = 0;
                ImGui.SetCursorPosX(cursorX);
                var gate = _helper.CanStartUserAction();
                if (!gate.CanRun) ImGui.BeginDisabled();

                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
                if (ImGui.Button("Deposit Crystals", new Vector2(btnWidth, 0))) _helper.TryStartUserAction(() => _manager.Deposit(true));
                ImGui.SameLine();
                if (ImGui.Button("Withdraw Crystals", new Vector2(btnWidth, 0))) _helper.TryStartUserAction(() => _manager.Withdraw(true));
                ImGui.PopStyleColor();
                if (!gate.CanRun) ImGui.EndDisabled();
                if (!gate.CanRun && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(gate.Reason);

                ImGui.EndChild();
            }
        }

        private Dictionary<uint, int> _editingValues = new();

        private void DrawCell(uint id)
        {
            var enabled = _configuration.CrystalConfig.EnabledIds.Contains(id);
            var hasCustom = _configuration.CrystalConfig.CustomKeepAmounts.ContainsKey(id);
            var style = ImGui.GetStyle();

            Vector4 btnColor;
            Vector4 textColor;

            if (enabled)
            {
                btnColor = style.Colors[(int)ImGuiCol.TabActive];
                textColor = new Vector4(1, 1, 1, 1);
            }
            else
            {
                btnColor = new Vector4(0.2f, 0.2f, 0.2f, 1f);
                textColor = new Vector4(0.5f, 0.5f, 0.5f, 1f);
            }

            ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.TabHovered]);
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);

            int keepAmount = hasCustom
                ? _configuration.CrystalConfig.CustomKeepAmounts[id]
                : _configuration.CrystalConfig.GlobalKeepAmount;

            string label = enabled
                ? (hasCustom ? $"*{keepAmount}" : keepAmount.ToString())
                : "---";

            if (ImGui.Button($"{label}##{id}", new Vector2(-1, 0)))
            {
                if (enabled) _configuration.CrystalConfig.EnabledIds.Remove(id);
                else _configuration.CrystalConfig.EnabledIds.Add(id);
                _configuration.Save();
            }

            ImGui.PopStyleColor(3);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.15f, 0.15f, 0.15f, 0.95f));
            if (ImGui.BeginPopupContextItem($"Ctx{id}"))
            {
                if (!_editingValues.ContainsKey(id))
                {
                    _editingValues[id] = hasCustom
                        ? _configuration.CrystalConfig.CustomKeepAmounts[id]
                        : _configuration.CrystalConfig.GlobalKeepAmount;
                }

                int val = _editingValues[id];

                ImGui.Text("Custom Keep Amount:");
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputInt("##CustomKeep", ref val))
                {
                    if (val < 0) val = 0;
                    if (val > 9999) val = 9999;
                    
                    _configuration.CrystalConfig.CustomKeepAmounts[id] = val;
                    _configuration.Save();
                    _editingValues[id] = val;
                }

                ImGui.Spacing();

                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
                if (ImGui.Button("Clear", new Vector2(55, 0)))
                {
                    _configuration.CrystalConfig.CustomKeepAmounts.Remove(id);
                    _configuration.Save();
                    ImGui.CloseCurrentPopup();
                    _editingValues.Remove(id);
                }
                ImGui.PopStyleColor();
                ImGui.EndPopup();
            }
            else
            {
                if (_editingValues.ContainsKey(id)) _editingValues.Remove(id);
            }
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }

        private void ToggleAll()
        {
            bool anyOff = global::FCCH.Managers.CrystalManager.AllIds.Any(x => !_configuration.CrystalConfig.EnabledIds.Contains(x));
            if (anyOff)
            {
                foreach (var id in global::FCCH.Managers.CrystalManager.AllIds) _configuration.CrystalConfig.EnabledIds.Add(id);
            }
            else
            {
                _configuration.CrystalConfig.EnabledIds.Clear();
            }
            _configuration.Save();
        }

        private void ToggleRow(uint[] ids) => ToggleSet(ids);
        private void ToggleCol(uint[] ids) => ToggleSet(ids);

        private void ToggleSet(uint[] ids)
        {
            bool anyOff = ids.Any(x => !_configuration.CrystalConfig.EnabledIds.Contains(x));
            if (anyOff)
            {
                foreach (var id in ids) _configuration.CrystalConfig.EnabledIds.Add(id);
            }
            else
            {
                foreach (var id in ids) _configuration.CrystalConfig.EnabledIds.Remove(id);
            }
            _configuration.Save();
        }
    }
}
