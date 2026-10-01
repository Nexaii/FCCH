using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace FCCH.Diagnostics;

public static class LogFileSink
{
    private const int MaxQueueDepth = 4096;
    private const int FlushIntervalMs = 250;
    private const int MaxLinesPerFlush = 512;
    private const int FailureQuietSeconds = 60;
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly object FlushGate = new();

    private static int _dropped;
    private static DateTime _lastFlushUtc = DateTime.UtcNow;
    private static DateTime _lastFailureUtc = DateTime.MinValue;

    public static void Enqueue(LogLevel level, string message, string? category)
    {
        if (Queue.Count >= MaxQueueDepth)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        var tag = category != null ? $"[{category}] " : string.Empty;
        Queue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {Short(level)} | {tag}{message}{Environment.NewLine}");
    }

    public static string? Tick(string path)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastFlushUtc).TotalMilliseconds < FlushIntervalMs) return null;
        _lastFlushUtc = now;
        return Flush(path);
    }

    public static string? Drain(string path, int timeoutMs = 250)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? failure = null;
        while ((!Queue.IsEmpty || Volatile.Read(ref _dropped) > 0) && DateTime.UtcNow < deadline)
            failure ??= Flush(path);
        return failure;
    }

    public static string? Save(string path, string payload)
    {
        try
        {
            EnsureFolder(path);
            File.WriteAllText(path, payload, Encoding.UTF8);
            return null;
        }
        catch (Exception ex)
        {
            return $"save failed: {ex.Message}";
        }
    }

    public static string? Reveal(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "no log folder yet";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"open folder failed: {ex.Message}";
        }
    }

    private static string? Flush(string path)
    {
        if (!Monitor.TryEnter(FlushGate)) return null;
        try
        {
            var builder = new StringBuilder(4096);

            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
                builder.Append($"[{DateTime.Now:HH:mm:ss.fff}] WRN | dropped {dropped} entries, queue overflow{Environment.NewLine}");

            var taken = 0;
            while (taken < MaxLinesPerFlush && Queue.TryDequeue(out var line))
            {
                builder.Append(line);
                taken++;
            }

            return builder.Length == 0 ? null : Write(path, builder.ToString());
        }
        finally
        {
            Monitor.Exit(FlushGate);
        }
    }

    private static string? Write(string path, string payload)
    {
        try
        {
            EnsureFolder(path);
            Rotate(path);

            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 8192, useAsync: false);
            var bytes = Encoding.UTF8.GetBytes(payload);
            stream.Write(bytes, 0, bytes.Length);
            return null;
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastFailureUtc).TotalSeconds < FailureQuietSeconds) return null;
            _lastFailureUtc = now;
            return $"log write failed for '{path}': {ex.Message}";
        }
    }

    private static void Rotate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxFileBytes) return;

        var rolled = path + ".1";
        File.Delete(rolled);
        File.Move(path, rolled);
    }

    private static void EnsureFolder(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Verbose => "VRB",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        _ => "ERR",
    };
}
