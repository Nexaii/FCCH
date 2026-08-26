using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    internal static class CategoryResolver
    {
        internal readonly struct CategoryMatch
        {
            public CategoryMatch(uint id, string name, int count)
            {
                Id = id;
                Name = name;
                Count = count;
            }

            public uint Id { get; }
            public string Name { get; }
            public int Count { get; }
        }

        private static readonly List<CategoryMatch> _categories = new();
        private static readonly Dictionary<uint, List<uint>> _itemsByCategory = new();
        private static bool _initialized;

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var itemSheet = Plugin.Data.GetExcelSheet<Item>();
                var categorySheet = Plugin.Data.GetExcelSheet<ItemUICategory>();
                if (itemSheet == null || categorySheet == null)
                    return;

                foreach (var item in itemSheet)
                {
                    if (!ItemListEligibility.IsAllowed(item))
                        continue;

                    var categoryId = item.ItemUICategory.RowId;
                    if (!_itemsByCategory.TryGetValue(categoryId, out var ids))
                    {
                        ids = new List<uint>();
                        _itemsByCategory[categoryId] = ids;
                    }

                    ids.Add(item.RowId);
                }

                foreach (var category in categorySheet)
                {
                    var name = category.Name.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!_itemsByCategory.TryGetValue(category.RowId, out var ids))
                        continue;

                    _categories.Add(new CategoryMatch(category.RowId, name, ids.Count));
                }

                _categories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[CategoryResolver] Category map build failed; category add is unavailable.");
            }

            FCCHLog.Info($"[CategoryResolver] Built {_categories.Count} addable categories.");
        }

        public static void Match(string search, List<CategoryMatch> results)
        {
            results.Clear();
            if (string.IsNullOrEmpty(search))
                return;

            Initialize();

            foreach (var category in _categories)
            {
                if (category.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    results.Add(category);
            }
        }

        public static IReadOnlyList<uint> GetItemIds(uint categoryId)
        {
            return _itemsByCategory.TryGetValue(categoryId, out var ids) ? ids : Array.Empty<uint>();
        }
    }
}
