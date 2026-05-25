using System;

namespace FCCH.Common
{
    public static class FCCHLog
    {
        public static void Info(string message)
        {
            Plugin.PluginLog.Info(message);
            MirrorIfEnabled("INF", message);
        }

        public static void Warning(string message)
        {
            Plugin.PluginLog.Warning(message);
            MirrorIfEnabled("WRN", message);
        }

        public static void Error(string message)
        {
            Plugin.PluginLog.Error(message);
            MirrorIfEnabled("ERR", message);
        }

        public static void Error(Exception ex, string message)
        {
            Plugin.PluginLog.Error(ex, message);
            MirrorIfEnabled("ERR", $"{message} | {ex}");
        }

        public static void Debug(string message)
        {
            Plugin.PluginLog.Debug(message);
            MirrorIfEnabled("DBG", message);
        }

        public static void Verbose(string message)
        {
            Plugin.PluginLog.Verbose(message);
            MirrorIfEnabled("VRB", message);
        }

        private static void MirrorIfEnabled(string level, string message)
        {
            var cfg = Plugin.Configuration;
            if (cfg == null) return;
            if (!cfg.DebugMode) return;
            DebugFileLogger.Enqueue(cfg.DebugLogPath, $"{level} | {message}");
        }
    }
}
