using System;
using System.Collections.Generic;
using System.Text;

namespace FCCH.UI
{
    internal class UndoStack<T>
    {
        public const int Depth = 10;

        private readonly List<(List<T> Snapshot, string Label)> _entries = new();
        private readonly Func<T, T> _clone;

        public UndoStack(Func<T, T> clone)
        {
            _clone = clone;
        }

        public int Count => _entries.Count;

        public void Capture(List<T> source, string label)
        {
            var snapshot = new List<T>(source.Count);
            for (var i = 0; i < source.Count; i++)
                snapshot.Add(_clone(source[i]));

            _entries.Add((snapshot, label));

            if (_entries.Count > Depth)
                _entries.RemoveAt(0);
        }

        public void Relabel(string label)
        {
            var last = _entries.Count - 1;
            _entries[last] = (_entries[last].Snapshot, label);
        }

        public void Discard()
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        public List<T> Pop()
        {
            var last = _entries.Count - 1;
            var snapshot = _entries[last].Snapshot;
            _entries.RemoveAt(last);
            return snapshot;
        }

        public string BuildTooltip()
        {
            var last = _entries.Count - 1;
            var text = new StringBuilder();
            text.Append("> ").Append(_entries[last].Label);

            for (var i = last - 1; i >= 0; i--)
                text.Append("\n  ").Append(_entries[i].Label);

            return text.ToString();
        }
    }
}
