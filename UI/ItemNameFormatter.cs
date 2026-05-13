using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace FCCH.UI
{
    internal readonly record struct ItemDisplayName(string FullName, string VisibleName, bool ShowTooltip);

    internal static class ItemNameFormatter
    {
        private const string Separator = "\u30fb";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
        private static readonly Regex EnglishLeadingGrade = new(@"^Grade\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex PrimedEnglishLeadingGrade = new(@"^Primed\s+Grade\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex CompactTrailingGrade = new(@"^(?<rest>.+?)\s*G(?<number>\d{1,2})$", RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex LabeledTrailingGrade = new(@"^(?<rest>.+?)\s+(?:de\s+)?(?:grade|rang|grad|stufe)\s+(?<number>[IVXLCDM]+|\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex GermanOrdinalTrailingGrade = new(@"^(?<rest>.+?)\s+(?<number>\d{1,2})\.\s*(?:grades|grade|stufe)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex RomanTrailingGrade = new(@"^(?<rest>.+?)\s+(?<number>[IVXLCDM]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex EnglishLeadingLevel = new(@"^Level\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex LabeledLeadingLevel = new(@"^(?:level|niveau|stufe)\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex CompactTrailingLevel = new(@"^(?<rest>.+?)\s*L(?<number>\d{1,2})$", RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex LabeledTrailingLevel = new(@"^(?<rest>.+?)\s+(?:level|niveau|stufe)\s+(?<number>\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex GermanOrdinalTrailingLevel = new(@"^(?<rest>.+?)\s+(?<number>\d{1,2})\.\s*stufe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);

        public static ItemDisplayName Format(uint itemId, string fullName, bool enabled, float maxWidth, string suffix = "")
        {
            var fullLabel = fullName + suffix;

            if (!enabled || string.IsNullOrWhiteSpace(fullName))
                return new ItemDisplayName(fullLabel, fullLabel, false);

            var semanticName = GetSemanticName(itemId, fullName) + suffix;
            var visibleName = FitToWidth(semanticName, maxWidth);
            return new ItemDisplayName(fullLabel, visibleName, !string.Equals(fullLabel, visibleName, StringComparison.Ordinal));
        }

        private static string GetSemanticName(uint itemId, string fullName)
        {
            if (TryCompactMateriaName(itemId, out var materiaName))
                return materiaName;

            if (TryCompactVerifiedFamilyName(itemId, fullName, out var familyName))
                return familyName;

            return fullName;
        }

        private static bool TryCompactMateriaName(uint itemId, out string displayName)
        {
            displayName = string.Empty;

            try
            {
                var materiaSheet = Plugin.Data.GetExcelSheet<Materia>();
                var baseParamSheet = Plugin.Data.GetExcelSheet<BaseParam>();
                if (materiaSheet == null || baseParamSheet == null)
                    return false;

                foreach (var materia in materiaSheet)
                {
                    for (var i = 0; i < materia.Item.Count; i++)
                    {
                        if (materia.Item[i].RowId != itemId)
                            continue;

                        var baseParam = baseParamSheet.GetRowOrDefault(materia.BaseParam.RowId);
                        var statName = baseParam?.Name.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(statName))
                            return false;

                        displayName = BuildTrailingTierName(statName, ToRoman(i + 1));
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string BuildTrailingTierName(string baseName, string tier)
        {
            return $"{baseName}{Separator}{tier}";
        }

        private static bool TryCompactVerifiedFamilyName(uint itemId, string fullName, out string displayName)
        {
            displayName = string.Empty;

            if (!CompactItemNameFamilyRegistry.TryGetRule(itemId, out var rule))
                return false;

            return rule.Prefix switch
            {
                CompactItemNamePrefix.Level => TryCompactLevelName(rule, fullName, out displayName),
                _ => TryCompactGradeName(rule, fullName, out displayName)
            };
        }

        private static bool TryCompactGradeName(CompactItemNameFamilyRule rule, string fullName, out string displayName)
        {
            if (TryBuildPrefixedName(PrimedEnglishLeadingGrade.Match(fullName), "G", rule.Number, rest => "Primed " + rest, out displayName))
                return true;

            if (TryBuildPrefixedName(EnglishLeadingGrade.Match(fullName), "G", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(CompactTrailingGrade.Match(fullName), "G", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(LabeledTrailingGrade.Match(fullName), "G", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(GermanOrdinalTrailingGrade.Match(fullName), "G", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(RomanTrailingGrade.Match(fullName), "G", rule.Number, static rest => rest, out displayName))
                return true;

            displayName = string.Empty;
            return false;
        }

        private static bool TryCompactLevelName(CompactItemNameFamilyRule rule, string fullName, out string displayName)
        {
            if (TryBuildPrefixedName(EnglishLeadingLevel.Match(fullName), "L", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(LabeledLeadingLevel.Match(fullName), "L", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(CompactTrailingLevel.Match(fullName), "L", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(LabeledTrailingLevel.Match(fullName), "L", rule.Number, static rest => rest, out displayName))
                return true;

            if (TryBuildPrefixedName(GermanOrdinalTrailingLevel.Match(fullName), "L", rule.Number, static rest => rest, out displayName))
                return true;

            displayName = string.Empty;
            return false;
        }

        private static bool TryBuildPrefixedName(Match match, string prefix, int expectedNumber, Func<string, string> formatRemainder, out string displayName)
        {
            displayName = string.Empty;

            if (!match.Success)
                return false;

            var number = ParseNumber(match.Groups["number"].Value);
            if (number != expectedNumber)
                return false;

            var rest = formatRemainder(match.Groups["rest"].Value.Trim());
            if (string.IsNullOrWhiteSpace(rest))
                return false;

            displayName = $"{prefix}{number}{Separator}{rest}";
            return true;
        }

        private static int ParseNumber(string value)
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return number;

            return FromRoman(value);
        }

        private static string ToRoman(int number)
        {
            return number switch
            {
                <= 0 => string.Empty,
                1 => "I",
                2 => "II",
                3 => "III",
                4 => "IV",
                5 => "V",
                6 => "VI",
                7 => "VII",
                8 => "VIII",
                9 => "IX",
                10 => "X",
                11 => "XI",
                12 => "XII",
                13 => "XIII",
                14 => "XIV",
                15 => "XV",
                16 => "XVI",
                _ => number.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static int FromRoman(string value)
        {
            var total = 0;
            var previous = 0;

            for (var i = value.Length - 1; i >= 0; i--)
            {
                var current = value[i] switch
                {
                    'I' or 'i' => 1,
                    'V' or 'v' => 5,
                    'X' or 'x' => 10,
                    'L' or 'l' => 50,
                    'C' or 'c' => 100,
                    'D' or 'd' => 500,
                    'M' or 'm' => 1000,
                    _ => 0
                };

                if (current == 0)
                    return 0;

                if (current < previous)
                    total -= current;
                else
                    total += current;

                previous = current;
            }

            return total;
        }

        private static string FitToWidth(string text, float maxWidth)
        {
            if (maxWidth <= 0 || ImGui.CalcTextSize(text).X <= maxWidth)
                return text;

            var ellipsis = "...";
            if (ImGui.CalcTextSize(ellipsis).X > maxWidth)
                return ellipsis;

            var elements = SplitTextElements(text);
            if (elements.Count <= 3)
                return ellipsis;

            var left = Math.Max(1, elements.Count / 2);
            var right = Math.Max(1, elements.Count - left);

            while (left + right > 1)
            {
                var candidate = string.Concat(elements.GetRange(0, left)) + ellipsis + string.Concat(elements.GetRange(elements.Count - right, right));
                if (ImGui.CalcTextSize(candidate).X <= maxWidth)
                    return candidate;

                if (left >= right && left > 1)
                    left--;
                else if (right > 1)
                    right--;
                else
                    break;
            }

            return ellipsis;
        }

        private static List<string> SplitTextElements(string text)
        {
            var indexes = StringInfo.ParseCombiningCharacters(text);
            var elements = new List<string>(indexes.Length);

            for (var i = 0; i < indexes.Length; i++)
            {
                var start = indexes[i];
                var end = i + 1 < indexes.Length ? indexes[i + 1] : text.Length;
                elements.Add(text[start..end]);
            }

            return elements;
        }
    }
}
