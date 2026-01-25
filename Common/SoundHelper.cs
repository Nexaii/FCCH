using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FCCH.Common
{
    public static class SoundHelper
    {
        [DllImport("winmm.dll")]
        private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

        public static void PlayCompletionSound(Configuration config)
        {
            if (!config.PlayCompletionSound) return;

            try
            {
                var soundPath = string.IsNullOrWhiteSpace(config.CustomSoundPath)
                    ? System.IO.Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Assets", "Completion.mp3")
                    : config.CustomSoundPath;

                if (!System.IO.File.Exists(soundPath))
                {
                    Plugin.PluginLog.Warning($"[SoundHelper] Sound file not found: {soundPath}");
                    return;
                }

                mciSendString("close fcch_sound", null, 0, IntPtr.Zero);
                mciSendString($"open \"{soundPath}\" type mpegvideo alias fcch_sound", null, 0, IntPtr.Zero);
                mciSendString("play fcch_sound", null, 0, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning($"[SoundHelper] Failed to play sound: {ex.Message}");
            }
        }
    }
}
