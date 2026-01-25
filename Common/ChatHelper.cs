using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace FCCH.Common
{
    /// <summary>
    /// Centralized chat messaging with colored [FCCH] prefix.
    /// Colors: Info=Green, Debug=Orange, Warning=Yellow, Error=Red
    /// </summary>
    public static class ChatHelper
    {
        // UIForeground color IDs (from Lumina UIColor sheet)
        private const ushort ColorGreen = 504;   // Healer green
        private const ushort ColorOrange = 500;  // Debug orange
        private const ushort ColorYellow = 31;   // Warning yellow
        private const ushort ColorRed = 17;      // Error red

        public static void Info(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorGreen)
                .AddText("[FCCH]")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }

        public static void Debug(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorOrange)
                .AddText("[FCCH] Debug:")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }

        public static void Warning(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorYellow)
                .AddText("[FCCH] Warning:")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }

        public static void Error(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorRed)
                .AddText("[FCCH] Error:")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }

        public static void PrintBatchWarnings(System.Collections.Generic.Dictionary<string, int> failures)
        {
            if (failures == null || failures.Count == 0) return;

            foreach (var kvp in failures)
            {
                Warning($"Failed to process {kvp.Value} items due to: {kvp.Key} (Enable Verbose Mode for details).");
            }
        }
    }
}
