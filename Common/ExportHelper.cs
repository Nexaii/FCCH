using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Dalamud.Bindings.ImGui;

namespace FCCH.Common
{
    /// <summary>
    /// Helper for exporting/importing tab data via clipboard
    /// Each tab uses a unique header prefix for validation
    /// </summary>
    public static class ExportHelper
    {
        // Unique headers for each tab type
        public const string HEADER_IGNORE = "FCCH_IGN_";
        public const string HEADER_SINGLES = "FCCH_SNG_";
        public const string HEADER_WORKSHOP = "FCCH_WKS_";

        public enum ImportResult
        {
            Success,
            InvalidFormat,
            WrongTabType,
            EmptyClipboard,
            ParseError
        }

        /// <summary>
        /// Export data to clipboard with header prefix
        /// </summary>
        public static bool Export<T>(string header, T data)
        {
            try
            {
                string json = JsonConvert.SerializeObject(data, Formatting.None);
                string encoded = Encode(json);
                string result = header + encoded;
                ImGui.SetClipboardText(result);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Export failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Import data from clipboard, validating header
        /// </summary>
        public static (ImportResult Result, T? Data) Import<T>(string expectedHeader)
        {
            try
            {
                string clipboard = ImGui.GetClipboardText();
                
                if (string.IsNullOrWhiteSpace(clipboard))
                    return (ImportResult.EmptyClipboard, default);

                // Check header
                if (!clipboard.StartsWith(expectedHeader))
                {
                    // Quick check if it's a different FCCH tab
                    if (clipboard.StartsWith("FCCH_"))
                        return (ImportResult.WrongTabType, default);
                    return (ImportResult.InvalidFormat, default);
                }

                // Remove header and decode
                string encoded = clipboard.Substring(expectedHeader.Length);
                string json = Decode(encoded);
                
                T? data = JsonConvert.DeserializeObject<T>(json);
                if (data == null)
                    return (ImportResult.ParseError, default);

                return (ImportResult.Success, data);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Import failed: {ex.Message}");
                return (ImportResult.ParseError, default);
            }
        }

        /// <summary>
        /// Error message for import result
        /// </summary>
        public static string GetErrorMessage(ImportResult result, string tabName)
        {
            return result switch
            {
                ImportResult.EmptyClipboard => "Clipboard is empty.",
                ImportResult.WrongTabType => $"Clipboard contains data for a different tab (not {tabName}).",
                ImportResult.InvalidFormat => "Clipboard does not contain valid FCCH export data.",
                ImportResult.ParseError => "Failed to parse export data. It may be corrupted.",
                _ => "Unknown error."
            };
        }

        private static string Encode(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using var output = new MemoryStream();

            using (var deflateStream = new DeflateStream(output, CompressionLevel.SmallestSize))
            {
                deflateStream.Write(bytes, 0, bytes.Length);
            }

            return Convert.ToBase64String(output.ToArray());
        }

        private static string Decode(string text)
        {
            byte[] bytes = Convert.FromBase64String(text);
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();

            using (var deflateStream = new DeflateStream(input, CompressionMode.Decompress))
            {
                deflateStream.CopyTo(output);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
