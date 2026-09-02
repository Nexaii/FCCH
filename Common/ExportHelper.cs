using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    public static class ExportHelper
    {
        public const string IgnoreListPrefix = "FCCH_IGN_";
        public const string SinglesListPrefix = "FCCH_SNG_";
        public const string WorkshopListPrefix = "FCCH_WKS_";
        public const string TeamcraftUrlPrefix = "https://ffxivteamcraft.com/";

        private static readonly Regex TeamcraftLine = new(@"^(\d+)x\s+(.+)$", RegexOptions.Compiled);

        private static readonly ClientLanguage[] AllLanguages =
        {
            ClientLanguage.Japanese,
            ClientLanguage.English,
            ClientLanguage.German,
            ClientLanguage.French
        };

        public enum ImportResult
        {
            Success,
            InvalidFormat,
            WrongTabType,
            EmptyClipboard,
            ParseError,
            MalformedTeamcraft,
            TeamcraftUrl,
            EmptyArtisanList
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

        public static (ImportResult Result, List<WithdrawItem>? Items, int Skipped, List<string> Unmatched) ImportItemList()
        {
            try
            {
                string clipboard = ImGui.GetClipboardText();
                if (string.IsNullOrWhiteSpace(clipboard))
                    return (ImportResult.EmptyClipboard, null, 0, new List<string>());

                clipboard = clipboard.Trim();

                if (clipboard.StartsWith(SinglesListPrefix))
                {
                    var list = JsonConvert.DeserializeObject<List<WithdrawItem>>(Decode(clipboard.Substring(SinglesListPrefix.Length)));
                    return list == null
                        ? (ImportResult.ParseError, null, 0, new List<string>())
                        : (ImportResult.Success, list, 0, new List<string>());
                }

                if (clipboard.StartsWith("FCCH_"))
                    return (ImportResult.WrongTabType, null, 0, new List<string>());

                if (LooksLikeTeamcraftUrl(clipboard))
                    return (ImportResult.TeamcraftUrl, null, 0, new List<string>());

                if (LooksLikeTeamcraftText(clipboard))
                    return ParseTeamcraftText(clipboard);

                if (LooksLikeArtisanList(clipboard))
                    return ParseArtisanList(clipboard);

                return (ImportResult.InvalidFormat, null, 0, new List<string>());
            }
            catch (Exception ex)
            {
                FCCHLog.Error($"Import failed: {ex.Message}");
                return (ImportResult.ParseError, null, 0, new List<string>());
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
                ImportResult.MalformedTeamcraft => "Clipboard looks like a Teamcraft list but could not be read.",
                ImportResult.TeamcraftUrl => "Paste the Teamcraft text list, not the URL.",
                ImportResult.EmptyArtisanList => "Clipboard is an Artisan list but holds no craftable item.",
                _ => "Unknown error."
            };
        }

        private static (ImportResult Result, List<WithdrawItem>? Items, int Skipped, List<string> Unmatched) ParseTeamcraftText(string text)
        {
            var quantities = new Dictionary<string, int>();
            var display = new Dictionary<string, string>();

            foreach (var rawLine in text.Split('\n'))
            {
                var match = TeamcraftLine.Match(rawLine.Trim());
                if (!match.Success)
                    continue;

                if (!int.TryParse(match.Groups[1].Value, out var qty))
                    continue;

                var name = CleanName(match.Groups[2].Value);
                var key = Normalize(name);
                if (key.Length == 0)
                    continue;

                if (quantities.ContainsKey(key))
                    quantities[key] += qty;
                else
                {
                    quantities[key] = qty;
                    display[key] = name;
                }
            }

            if (quantities.Count == 0)
                return (ImportResult.MalformedTeamcraft, null, 0, new List<string>());

            var resolved = new Dictionary<string, uint>();
            ResolveNames(new HashSet<string>(quantities.Keys), resolved);

            var items = new List<WithdrawItem>();
            var unmatched = new List<string>();
            foreach (var pair in quantities)
            {
                if (resolved.TryGetValue(pair.Key, out var itemId))
                    items.Add(new WithdrawItem { ItemId = itemId, Quantity = pair.Value, Mode = CustomItemMode.Withdraw, AlwaysMax = false });
                else
                    unmatched.Add(display[pair.Key]);
            }

            if (items.Count == 0)
                return (ImportResult.MalformedTeamcraft, null, 0, unmatched);

            return (ImportResult.Success, items, 0, unmatched);
        }

        private static (ImportResult Result, List<WithdrawItem>? Items, int Skipped, List<string> Unmatched) ParseArtisanList(string json)
        {
            ArtisanList? parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<ArtisanList>(json);
            }
            catch (JsonException)
            {
                return (ImportResult.ParseError, null, 0, new List<string>());
            }

            if (parsed?.Recipes == null || parsed.Recipes.Count == 0)
                return (ImportResult.EmptyArtisanList, null, 0, new List<string>());

            var recipes = Plugin.Data.GetExcelSheet<Recipe>();
            if (recipes == null)
                return (ImportResult.ParseError, null, 0, new List<string>());

            var quantities = new Dictionary<uint, int>();
            var skipped = 0;

            foreach (var entry in parsed.Recipes)
            {
                if (!recipes.TryGetRow(entry.ID, out var recipe) || recipe.ItemResult.RowId == 0)
                {
                    skipped++;
                    continue;
                }

                var itemId = recipe.ItemResult.RowId;
                if (quantities.ContainsKey(itemId))
                    quantities[itemId] += entry.Quantity;
                else
                    quantities[itemId] = entry.Quantity;
            }

            if (quantities.Count == 0)
                return (ImportResult.EmptyArtisanList, null, skipped, new List<string>());

            var items = new List<WithdrawItem>();
            foreach (var pair in quantities)
                items.Add(new WithdrawItem { ItemId = pair.Key, Quantity = pair.Value, Mode = CustomItemMode.Withdraw, AlwaysMax = false });

            return (ImportResult.Success, items, skipped, new List<string>());
        }

        private static void ResolveNames(HashSet<string> remaining, Dictionary<string, uint> resolved)
        {
            foreach (var language in LanguageOrder())
            {
                if (remaining.Count == 0)
                    return;

                var sheet = Plugin.Data.GetExcelSheet<Item>(language);
                if (sheet == null)
                    continue;

                foreach (var item in sheet)
                {
                    if (remaining.Count == 0)
                        break;

                    var key = Normalize(item.Name.ToString());
                    if (key.Length == 0)
                        continue;

                    if (remaining.Remove(key))
                        resolved[key] = item.RowId;
                }
            }
        }

        private static IEnumerable<ClientLanguage> LanguageOrder()
        {
            var first = Plugin.ClientState.ClientLanguage;
            yield return first;

            foreach (var language in AllLanguages)
                if (language != first)
                    yield return language;
        }

        private static bool LooksLikeTeamcraftUrl(string text)
            => text.StartsWith(TeamcraftUrlPrefix, StringComparison.OrdinalIgnoreCase);

        private static bool LooksLikeTeamcraftText(string text)
        {
            foreach (var rawLine in text.Split('\n'))
                if (TeamcraftLine.IsMatch(rawLine.Trim()))
                    return true;

            return false;
        }

        private static bool LooksLikeArtisanList(string text)
            => text.StartsWith('{') && text.Contains("\"Recipes\"");

        private static string CleanName(string name)
        {
            name = name.Trim();

            var cut = name.Length;
            var paren = name.IndexOf('(');
            if (paren >= 0)
                cut = paren;
            var bracket = name.IndexOf('[');
            if (bracket >= 0 && bracket < cut)
                cut = bracket;
            if (cut > 0 && cut < name.Length)
                name = name.Substring(0, cut).Trim();

            if (name.EndsWith(" HQ", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3).Trim();

            return name;
        }

        private static string Normalize(string value)
        {
            var builder = new StringBuilder(value.Length);
            var pendingSpace = false;

            foreach (var c in value.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
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

        private sealed class ArtisanList
        {
            public List<ArtisanRecipe>? Recipes { get; set; }
        }

        private sealed class ArtisanRecipe
        {
            public uint ID { get; set; }
            public int Quantity { get; set; }
        }
    }
}
