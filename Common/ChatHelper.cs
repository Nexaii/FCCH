using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace FC_Chest_Helper.Common
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

        /// <summary>Standard info message with green [FCCH] prefix.</summary>
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

        /// <summary>Debug message with orange [FCCH Debug] prefix.</summary>
        public static void Debug(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorOrange)
                .AddText("[FCCH Debug]")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }

        /// <summary>Warning message with yellow [FCCH Warning] prefix.</summary>
        public static void Warning(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorYellow)
                .AddText("[FCCH Warning]")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }

        /// <summary>Error message with red [FCCH Error] prefix.</summary>
        public static void Error(string message)
        {
            var seString = new SeStringBuilder()
                .AddUiForeground(ColorRed)
                .AddText("[FCCH Error]")
                .AddUiForegroundOff()
                .AddText($" {message}")
                .Build();
            Plugin.Chat.Print(seString);
        }
    }
}
