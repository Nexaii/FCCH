using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FCCH.UI
{
    internal enum PaintColumn
    {
        None = 0,
        Mode = 1,
        Max = 2
    }

    internal class BulkPaint
    {
        private const float ArmDistance = 4f;

        private PaintColumn _column = PaintColumn.None;
        private int _originRow = -1;
        private Vector2 _origin;
        private bool _armed;

        public bool Active => _column != PaintColumn.None;
        public bool Armed => _armed;

        public bool IsOrigin(int row) => _originRow == row;

        public void BeginFrame()
        {
            if (Active && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                Reset();
        }

        public void Start(PaintColumn column, int row)
        {
            _column = column;
            _originRow = row;
            _origin = ImGui.GetMousePos();
            _armed = false;
        }

        public bool ShouldPaint(PaintColumn column, int row)
        {
            if (!Active || _column != column || _originRow == row)
                return false;

            if (!_armed)
            {
                var travel = ImGui.GetMousePos() - _origin;
                if (travel.LengthSquared() < ArmDistance * ArmDistance)
                    return false;

                _armed = true;
            }

            return Contains(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetMousePos());
        }

        public bool EndedThisFrame()
        {
            if (!Active || !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                return false;

            var painted = _armed;
            Reset();
            return painted;
        }

        private void Reset()
        {
            _column = PaintColumn.None;
            _originRow = -1;
            _armed = false;
        }

        private static bool Contains(Vector2 min, Vector2 max, Vector2 point)
        {
            return point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;
        }
    }
}
