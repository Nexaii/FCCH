using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Dalamud.Bindings.ImGui;

namespace FCCH.Common
{
    public static class ExportHelper
    {
        public const string IgnoreListPrefix = "FCCH_IGN_";
        public const string SinglesListPrefix = "FCCH_SNG_";
        public const string WorkshopListPrefix = "FCCH_WKS_";

        public enum ImportResult
        {
            Success,
            InvalidFormat,
            WrongTabType,
            EmptyClipboard,
            ParseError
        }

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
                FCCHLog.Error($"Export failed: {ex.Message}");
                return false;
            }
        }

        public static (ImportResult Result, T? Data) Import<T>(string expectedHeader)
        {
            try
            {
                string clipboard = ImGui.GetClipboardText();
                
                if (string.IsNullOrWhiteSpace(clipboard))
                    return (ImportResult.EmptyClipboard, default);

                if (!clipboard.StartsWith(expectedHeader))
                {
                    // Quick check if it's a different FCCH tab
                    if (clipboard.StartsWith("FCCH_"))
                        return (ImportResult.WrongTabType, default);
                    return (ImportResult.InvalidFormat, default);
                }

                string encoded = clipboard.Substring(expectedHeader.Length);
                string json = Decode(encoded);
                
                T? data = JsonConvert.DeserializeObject<T>(json);
                if (data == null)
                    return (ImportResult.ParseError, default);

                return (ImportResult.Success, data);
            }
            catch (Exception ex)
            {
                FCCHLog.Error($"Import failed: {ex.Message}");
                return (ImportResult.ParseError, default);
            }
        }

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
