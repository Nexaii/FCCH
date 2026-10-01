using System;
using System.Collections.Generic;
using System.Text;

namespace FCCH.Diagnostics;

public static class LogRing
{
    public const int Capacity = 2000;

    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(250);
    private static readonly LogEntry[] Entries = new LogEntry[Capacity];
    private static readonly Dictionary<(LogLevel Level, string? Category, string Message), long> Recent = new();
    private static readonly Queue<((LogLevel Level, string? Category, string Message) Key, long Ordinal, DateTime At)> Order = new();
    private static readonly object Gate = new();

    private static long _pushed;
    private static int _version;

    public static int Version
    {
        get { lock (Gate) return _version; }
    }

    public static int Count
    {
        get { lock (Gate) return LiveCount(); }
    }

    public static void Push(LogLevel level, string message, string? category)
    {
        var now = DateTime.Now;
        lock (Gate)
        {
            DropExpired(now);

            var key = (level, category, message);
            if (Recent.TryGetValue(key, out var ordinal) && IsLive(ordinal))
            {
                ref var merged = ref Entries[Slot(ordinal)];
                merged.Count++;
                merged.Last = now;
                _version++;
                return;
            }

            Entries[Slot(_pushed)] = new LogEntry(level, message, category, now);
            Recent[key] = _pushed;
            Order.Enqueue((key, _pushed, now));
            _pushed++;
            _version++;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Array.Clear(Entries);
            Recent.Clear();
            Order.Clear();
            _pushed = 0;
            _version++;
        }
    }

    public static void Snapshot(List<LogEntry> into, string? search)
    {
        into.Clear();
        lock (Gate)
        {
            var live = LiveCount();
            var first = _pushed - live;
            for (var i = 0; i < live; i++)
            {
                var entry = Entries[Slot(first + i)];
                if (Matches(entry, search)) into.Add(entry);
            }
        }
    }

    public static string CopyToString(string header, string? search)
    {
        var builder = new StringBuilder(4096);
        if (header.Length > 0) builder.AppendLine(header);

        lock (Gate)
        {
            var live = LiveCount();
            var first = _pushed - live;
            for (var i = 0; i < live; i++)
            {
                var entry = Entries[Slot(first + i)];
                if (!Matches(entry, search)) continue;

                builder.Append('[');
                builder.Append(entry.First.ToString("HH:mm:ss"));
                if (entry.Count > 1)
                {
                    builder.Append(" - ");
                    builder.Append(entry.Last.ToString("HH:mm:ss"));
                }

                builder.Append("] [");
                builder.Append(entry.Level);
                builder.Append("] ");
                if (entry.Category != null)
                {
                    builder.Append('[');
                    builder.Append(entry.Category);
                    builder.Append("] ");
                }

                builder.Append(entry.Message);
                if (entry.Count > 1)
                {
                    builder.Append(" x");
                    builder.Append(entry.Count);
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static bool Matches(in LogEntry entry, string? search)
    {
        if (string.IsNullOrEmpty(search)) return true;
        if (entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        if (LevelName(entry.Level).Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        return entry.Category != null && entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Verbose => "Verbose",
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        LogLevel.Warning => "Warning",
        _ => "Error",
    };

    private static void DropExpired(DateTime now)
    {
        while (Order.Count > 0 && now - Order.Peek().At > MergeWindow)
        {
            var stale = Order.Dequeue();
            if (Recent.TryGetValue(stale.Key, out var ordinal) && ordinal == stale.Ordinal)
                Recent.Remove(stale.Key);
        }
    }

    private static int LiveCount() => _pushed < Capacity ? (int)_pushed : Capacity;

    private static bool IsLive(long ordinal) => ordinal >= _pushed - Capacity;

    private static int Slot(long ordinal) => (int)(ordinal % Capacity);
}
