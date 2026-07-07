using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Game;
using FCCH.Common;
using Lumina.Excel.Sheets;

namespace FCCH.UI
{
    internal enum CompactItemNamePrefix
    {
        Grade,
        Level
    }

    internal readonly record struct CompactItemNameFamilyRule(CompactItemNamePrefix Prefix, int Number, string FamilyKey, string EnglishName, string EnglishRemainder);

    internal readonly record struct CompactItemNameAuditEntry(
        uint ItemId,
        CompactItemNamePrefix Prefix,
        int Number,
        string FamilyKey,
        string EnglishName,
        string FrenchName,
        string GermanName,
        string JapaneseName,
        string ProposedEnglishName,
        string RejectionReason);

    internal static class CompactItemNameFamilyRegistry
    {
        private const string Separator = "\u30fb";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex EnglishGrade = new(@"^Grade\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex PrimedEnglishGrade = new(@"^Primed\s+Grade\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex EnglishLevel = new(@"^Level\s+(?<number>\d{1,2})\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly object SyncRoot = new();

        private static IReadOnlyDictionary<uint, CompactItemNameFamilyRule>? rules;
        private static IReadOnlyList<CompactItemNameAuditEntry>? auditEntries;

        public static bool TryGetRule(uint itemId, out CompactItemNameFamilyRule rule)
        {
            EnsureLoaded();
            return rules!.TryGetValue(itemId, out rule);
        }

        public static IReadOnlyList<CompactItemNameAuditEntry> GetAuditEntries()
        {
            EnsureLoaded();
            return auditEntries!;
        }

        private static void EnsureLoaded()
        {
            if (rules != null && auditEntries != null)
                return;

            lock (SyncRoot)
            {
                if (rules != null && auditEntries != null)
                    return;

                var nextRules = new Dictionary<uint, CompactItemNameFamilyRule>();
                var nextAudit = new List<CompactItemNameAuditEntry>();

                try
                {
                    var englishSheet = Plugin.Data.GetExcelSheet<Item>(ClientLanguage.English);
                    if (englishSheet != null)
                    {
                        foreach (var item in englishSheet)
                        {
                            var englishName = item.Name.ToString();
                            if (string.IsNullOrWhiteSpace(englishName))
                                continue;

                            if (item.FilterGroup == 18 && IsCandidateName(englishName))
                            {
                                nextAudit.Add(CreateRejectedAuditEntry(item.RowId, englishName, "Treasure map"));
                                continue;
                            }

                            if (!TryCreateRule(englishName, out var rule))
                            {
                                if (IsCandidateName(englishName))
                                    nextAudit.Add(CreateRejectedAuditEntry(item.RowId, englishName, "Unsupported family"));

                                continue;
                            }

                            nextRules[item.RowId] = rule;
                            nextAudit.Add(CreateAuditEntry(item.RowId, rule));
                        }
                    }
                }
                catch (Exception ex)
                {
                    FCCHLog.Error(ex, "[CompactItemNameFamilyRegistry] Rule rebuild failed.");
                }

                rules = nextRules;
                auditEntries = nextAudit;
            }
        }

        private static CompactItemNameAuditEntry CreateAuditEntry(uint itemId, CompactItemNameFamilyRule rule)
        {
            return new CompactItemNameAuditEntry(
                itemId,
                rule.Prefix,
                rule.Number,
                rule.FamilyKey,
                rule.EnglishName,
                GetLocalizedName(itemId, ClientLanguage.French),
                GetLocalizedName(itemId, ClientLanguage.German),
                GetLocalizedName(itemId, ClientLanguage.Japanese),
                $"{GetPrefix(rule.Prefix)}{rule.Number}{Separator}{rule.EnglishRemainder}",
                string.Empty);
        }

        private static CompactItemNameAuditEntry CreateRejectedAuditEntry(uint itemId, string englishName, string reason)
        {
            return new CompactItemNameAuditEntry(
                itemId,
                CompactItemNamePrefix.Grade,
                0,
                string.Empty,
                englishName,
                GetLocalizedName(itemId, ClientLanguage.French),
                GetLocalizedName(itemId, ClientLanguage.German),
                GetLocalizedName(itemId, ClientLanguage.Japanese),
                string.Empty,
                reason);
        }

        private static string GetLocalizedName(uint itemId, ClientLanguage language)
        {
            try
            {
                return Plugin.Data.GetExcelSheet<Item>(language)?.GetRowOrDefault(itemId)?.Name.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                FCCHLog.Debug($"[CompactItemNameFamilyRegistry] Localized name lookup failed for item {itemId}: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool TryCreateRule(string englishName, out CompactItemNameFamilyRule rule)
        {
            rule = default;

            var primedGrade = PrimedEnglishGrade.Match(englishName);
            if (primedGrade.Success && TryCreateGradeRule(primedGrade, "Primed " + primedGrade.Groups["rest"].Value.Trim(), englishName, out rule))
                return true;

            var grade = EnglishGrade.Match(englishName);
            if (grade.Success && TryCreateGradeRule(grade, grade.Groups["rest"].Value.Trim(), englishName, out rule))
                return true;

            var level = EnglishLevel.Match(englishName);
            if (level.Success && TryCreateLevelRule(level, level.Groups["rest"].Value.Trim(), englishName, out rule))
                return true;

            return false;
        }

        private static bool IsCandidateName(string englishName)
        {
            return PrimedEnglishGrade.IsMatch(englishName) || EnglishGrade.IsMatch(englishName) || EnglishLevel.IsMatch(englishName);
        }

        private static bool TryCreateGradeRule(Match match, string remainder, string englishName, out CompactItemNameFamilyRule rule)
        {
            rule = default;

            if (!int.TryParse(match.Groups["number"].Value, out var number))
                return false;

            var familyKey = GetGradeFamilyKey(remainder);
            if (familyKey.Length == 0)
                return false;

            rule = new CompactItemNameFamilyRule(CompactItemNamePrefix.Grade, number, familyKey, englishName, remainder);
            return true;
        }

        private static bool TryCreateLevelRule(Match match, string remainder, string englishName, out CompactItemNameFamilyRule rule)
        {
            rule = default;

            if (!int.TryParse(match.Groups["number"].Value, out var number))
                return false;

            var familyKey = GetLevelFamilyKey(remainder);
            if (familyKey.Length == 0)
                return false;

            rule = new CompactItemNameFamilyRule(CompactItemNamePrefix.Level, number, familyKey, englishName, remainder);
            return true;
        }

        private static string GetGradeFamilyKey(string remainder)
        {
            var normalized = NormalizeEnglish(remainder);

            if (normalized == "carbonized matter") return "carbonized-matter";
            if (normalized == "la noscean topsoil") return "la-noscean-topsoil";
            if (normalized == "shroud topsoil") return "shroud-topsoil";
            if (normalized == "thanalan topsoil") return "thanalan-topsoil";
            if (normalized == "clear prism") return "clear-prism";
            if (normalized == "dark matter") return "dark-matter";
            if (normalized.StartsWith("tincture of ", StringComparison.Ordinal)) return "tincture";
            if (normalized.StartsWith("wheel of ", StringComparison.Ordinal)) return "wheel";
            if (normalized.StartsWith("primed wheel of ", StringComparison.Ordinal)) return "primed-wheel";
            if (normalized.StartsWith("feed - ", StringComparison.Ordinal) && normalized.EndsWith(" blend", StringComparison.Ordinal)) return "feed-blend";
            if (normalized.StartsWith("reisui of ", StringComparison.Ordinal)) return "reisui";
            if (normalized.EndsWith(" alkahest", StringComparison.Ordinal)) return "alkahest";

            return string.Empty;
        }

        private static string GetLevelFamilyKey(string remainder)
        {
            return NormalizeEnglish(remainder) == "aetherial wheel stand" ? "aetherial-wheel-stand" : string.Empty;
        }

        private static string NormalizeEnglish(string value)
        {
            return Whitespace.Replace(value.Trim().ToLowerInvariant(), " ");
        }

        private static string GetPrefix(CompactItemNamePrefix prefix)
        {
            return prefix == CompactItemNamePrefix.Level ? "L" : "G";
        }
    }
}
