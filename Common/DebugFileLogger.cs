using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace FCCH.Common
{
    public static class DebugFileLogger
    {
        private const int MaxQueueDepth = 4096;
        private const int FlushIntervalMs = 250;
        private const int MaxLinesPerFlush = 512;
        private const int FailureWarnIntervalSeconds = 60;

        private static readonly ConcurrentQueue<Entry> _queue = new();
        private static int _droppedSinceLastFlush;
        private static DateTime _lastFlushUtc = DateTime.UtcNow;
        private static DateTime _lastFailureWarnUtc = DateTime.MinValue;
        private static readonly object _flushGate = new();

        private readonly struct Entry
        {
            public readonly string Path;
            public readonly string Line;
            public Entry(string path, string line) { Path = path; Line = line; }
        }

        public static void Enqueue(string? path, string message)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (message == null) return;

            if (_queue.Count >= MaxQueueDepth)
            {
                System.Threading.Interlocked.Increment(ref _droppedSinceLastFlush);
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            _queue.Enqueue(new Entry(path!, line));
        }

        public static void Tick()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastFlushUtc).TotalMilliseconds < FlushIntervalMs) return;
            _lastFlushUtc = now;
            FlushPending(MaxLinesPerFlush);
        }

        public static void DrainAndShutdown(int timeoutMs = 250)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!_queue.IsEmpty && DateTime.UtcNow < deadline)
            {
                FlushPending(MaxLinesPerFlush);
            }
        }

        private static void FlushPending(int maxLines)
        {
            if (!System.Threading.Monitor.TryEnter(_flushGate)) return;
            try
            {
                int dropped = System.Threading.Interlocked.Exchange(ref _droppedSinceLastFlush, 0);

                var byPath = new System.Collections.Generic.Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
                int taken = 0;
                while (taken < maxLines && _queue.TryDequeue(out var entry))
                {
                    if (!byPath.TryGetValue(entry.Path, out var sb))
                    {
                        sb = new StringBuilder(4096);
                        byPath[entry.Path] = sb;
                    }
                    sb.Append(entry.Line);
                    taken++;
                }

                if (dropped > 0 && byPath.Count > 0)
                {
                    foreach (var sb in byPath.Values)
                    {
                        sb.Insert(0, $"[{DateTime.Now:HH:mm:ss}] [DebugFileLogger] dropped {dropped} entries (queue overflow){Environment.NewLine}");
                    }
                }
                else if (dropped > 0)
                {
                    System.Threading.Interlocked.Add(ref _droppedSinceLastFlush, dropped);
                }

                foreach (var kv in byPath)
                {
                    WritePathSafely(kv.Key, kv.Value.ToString());
                }
            }
            finally
            {
                System.Threading.Monitor.Exit(_flushGate);
            }
        }

        private static void WritePathSafely(string path, string payload)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 8192, useAsync: false);
                var bytes = Encoding.UTF8.GetBytes(payload);
                fs.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastFailureWarnUtc).TotalSeconds >= FailureWarnIntervalSeconds)
                {
                    _lastFailureWarnUtc = now;
                    try { Plugin.PluginLog.Warning($"[DebugFileLogger] write failed for '{path}': {ex.Message}"); } catch { }
                }
            }
        }
    }
}
