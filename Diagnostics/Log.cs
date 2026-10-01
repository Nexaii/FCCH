using System;
using System.Reflection;
using Dalamud.Plugin.Services;

namespace FCCH.Diagnostics;

public static class Log
{
    private static IPluginLog? _pluginLog;
    private static Func<bool>? _fileEnabled;
    private static Func<string>? _filePath;
    private static string _defaultFilePath = string.Empty;

    public static string PluginName { get; private set; } = "Plugin";

    public static string Header { get; private set; } = string.Empty;

    public static bool FileEnabled => _fileEnabled?.Invoke() ?? false;

    public static string FilePath
    {
        get
        {
            var configured = _filePath?.Invoke();
            return string.IsNullOrWhiteSpace(configured) ? _defaultFilePath : configured;
        }
    }

    public static void Init(string pluginName, IPluginLog pluginLog, Func<bool> fileEnabled, Func<string> filePath, string defaultFilePath)
    {
        PluginName = pluginName;
        _pluginLog = pluginLog;
        _fileEnabled = fileEnabled;
        _filePath = filePath;
        _defaultFilePath = defaultFilePath;
        Header = $"{pluginName} {Assembly.GetExecutingAssembly().GetName().Version}";
    }

    public static void Verbose(string message, string? category = null) => Write(LogLevel.Verbose, message, category);

    public static void Debug(string message, string? category = null) => Write(LogLevel.Debug, message, category);

    public static void Info(string message, string? category = null) => Write(LogLevel.Info, message, category);

    public static void Warning(string message, string? category = null) => Write(LogLevel.Warning, message, category);

    public static void Error(string message, string? category = null) => Write(LogLevel.Error, message, category);

    public static void Error(Exception ex, string message, string? category = null) => Write(LogLevel.Error, $"{message} | {ex}", category);

    public static void Tick()
    {
        if (!FileEnabled) return;

        var path = FilePath;
        if (path.Length == 0) return;
        Report(LogFileSink.Tick(path));
    }

    public static void Shutdown()
    {
        var path = FilePath;
        if (FileEnabled && path.Length > 0) Report(LogFileSink.Drain(path));

        _pluginLog = null;
        _fileEnabled = null;
        _filePath = null;
    }

    private static void Write(LogLevel level, string message, string? category)
    {
        LogRing.Push(level, message, category);

        if (level == LogLevel.Error) _pluginLog?.Error(message);
        else if (level == LogLevel.Warning) _pluginLog?.Warning(message);

        if (FileEnabled) LogFileSink.Enqueue(level, message, category);
    }

    private static void Report(string? failure)
    {
        if (failure == null) return;
        LogRing.Push(LogLevel.Error, failure, "log");
        _pluginLog?.Warning(failure);
    }
}
