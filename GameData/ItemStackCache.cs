using System;
using System.Collections.Concurrent;
using Lumina.Excel.Sheets;

namespace FCCH.GameData
{
    public static class ItemStackCache
    {
        private const uint DefaultMaxStack = 999;

        private static readonly ConcurrentDictionary<uint, uint> _maxStackCache = new();

        public static uint GetMaxStack(uint itemId)
        {
            if (_maxStackCache.TryGetValue(itemId, out var cached))
                return cached;

            uint resolved = ResolveMaxStack(itemId);
            _maxStackCache[itemId] = resolved;
            return resolved;
        }

        private static uint ResolveMaxStack(uint itemId)
        {
            try
            {
                var sheet = Plugin.Data.GetExcelSheet<Item>();
                var row = sheet?.GetRowOrDefault(itemId);
                if (row != null) return row.Value.StackSize;
            }
            catch (Exception ex)
            {
                FCCH.Common.FCCHLog.Warning($"Failed to get stack size for Item#{itemId}: {ex.Message}");
            }
            return DefaultMaxStack;
        }
    }
}
