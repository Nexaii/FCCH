using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;

namespace FCCH.UI
{
    internal sealed class DragDropHelper<T>
    {
        private readonly string dragDropId;
        private readonly Func<T, string> getUniqueId;
        private readonly bool small;
        private readonly List<(Vector2 RowPos, Vector2 ButtonPos, Action BeginDraw, Action AcceptDraw)> moveCommands = new();
        private Vector2 initialCursorPos;
        private Vector2 buttonCursorPos;
        private string? currentDrag;

        public DragDropHelper(string dragDropId, Func<T, string> getUniqueId, bool smallButton = true)
        {
            this.dragDropId = dragDropId;
            this.getUniqueId = getUniqueId;
            small = smallButton;
        }

        public void Begin()
        {
            moveCommands.Clear();
        }

        public void NextRow()
        {
            initialCursorPos = ImGui.GetCursorPos();
        }

        public void DrawButtonDummy(T item, IList<T> list, int targetPosition, Action<IList<T>>? onReorder = null)
        {
            void ExecuteMove(string draggedId)
            {
                MoveItemToPosition(list, x => getUniqueId(x) == draggedId, targetPosition);
                onReorder?.Invoke(list);
            }

            DrawButtonDummy(getUniqueId(item), ExecuteMove);
        }

        private void DrawButtonDummy(string uniqueId, Action<string> onAcceptPayload)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            buttonCursorPos = ImGui.GetCursorPos();
            var size = ImGuiHelpers.GetButtonSize(FontAwesomeIcon.ArrowsUpDownLeftRight.ToIconString());
            if (small) size = size with { Y = ImGui.CalcTextSize(FontAwesomeIcon.ArrowsUpDownLeftRight.ToIconString()).Y };
            ImGui.Dummy(size);
            ImGui.PopFont();

            void SourceAction()
            {
                ImGui.PushFont(UiBuilder.IconFont);
                var label = $"{FontAwesomeIcon.ArrowsUpDownLeftRight.ToIconString()}##{dragDropId}Move{uniqueId}";
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
                if (small)
                    ImGui.SmallButton(label);
                else
                    ImGui.Button(label);
                ImGui.PopStyleColor();
                ImGui.PopFont();

                if (ImGui.IsItemHovered()) 
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
                {
                    currentDrag = uniqueId;
                    ImGui.SetDragDropPayload(dragDropId, ReadOnlySpan<byte>.Empty);
                    ImGui.EndDragDropSource();
                }
                else if (currentDrag == uniqueId)
                {
                    currentDrag = null;
                }
            }

            void TargetAction()
            {
                if (!ImGui.BeginDragDropTarget()) return;
                var payload = ImGui.AcceptDragDropPayload(dragDropId, ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                if (!payload.IsNull && currentDrag != null && currentDrag != uniqueId)
                    onAcceptPayload(currentDrag);
                ImGui.EndDragDropTarget();
            }

            moveCommands.Add((initialCursorPos, buttonCursorPos, SourceAction, TargetAction));
        }

        public bool SetRowColor(string uniqueId)
        {
            if (currentDrag != uniqueId) return false;
            var baseColor = ImGuiColors.HealerGreen;
            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(baseColor.X, baseColor.Y, baseColor.Z, 0.4f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, color);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, color);
            return true;
        }

        public bool SetRowColor(T item)
        {
            return SetRowColor(getUniqueId(item));
        }

        public void End(int numRows = 1)
        {
            var cursor = ImGui.GetCursorPos();
            foreach (var (rowPos, buttonPos, beginDraw, acceptDraw) in moveCommands)
            {
                ImGui.SetCursorPos(buttonPos);
                beginDraw();
                acceptDraw();
                ImGui.SetCursorPos(rowPos);
                var height = ImGui.GetFrameHeight() * numRows + ImGui.GetStyle().ItemInnerSpacing.Y - numRows;
                ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, height));
                acceptDraw();
            }
            ImGui.SetCursorPos(cursor);
        }

        private static void MoveItemToPosition(IList<T> list, Func<T, bool> sourceSelector, int targetIndex)
        {
            var sourceIndex = -1;
            for (var i = 0; i < list.Count; i++)
            {
                if (!sourceSelector(list[i])) continue;
                sourceIndex = i;
                break;
            }

            if (sourceIndex < 0 || sourceIndex == targetIndex) return;

            var item = list[sourceIndex];
            list.RemoveAt(sourceIndex);
            list.Insert(targetIndex, item);
        }
    }
}
