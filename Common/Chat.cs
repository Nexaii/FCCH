using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace FCCH.Common;

public static class Chat
{
    private const ushort ColorResult = 504;
    private const ushort ColorWarn = 31;
    private const ushort ColorError = 17;

    private static IChatGui? _chat;
    private static Func<bool>? _quiet;
    private static string _prefix = string.Empty;

    public static void Init(string pluginName, IChatGui chat, Func<bool>? quiet = null)
    {
        _chat = chat;
        _quiet = quiet;
        _prefix = $"[{pluginName}]";
    }

    public static void Result(string message)
    {
        if (_quiet?.Invoke() ?? false) return;
        Print(ColorResult, _prefix, message);
    }

    public static void Reply(string message) => Print(ColorResult, _prefix, message);

    public static void Warn(string message) => Print(ColorWarn, _prefix, message);

    public static void Error(string message) => Print(ColorError, _prefix, message);

    private static void Print(ushort color, string tag, string message)
    {
        if (_chat == null) return;

        var line = new SeStringBuilder()
            .AddUiForeground(color)
            .AddText(tag)
            .AddUiForegroundOff()
            .AddText($" {message}")
            .Build();
        _chat.Print(line);
    }
}
