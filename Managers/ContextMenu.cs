using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FCCH.Common;
using Lumina.Excel.Sheets;

namespace FCCH.Managers
{
    internal sealed class ContextMenu : IDisposable
    {
        private readonly IContextMenu contextMenu;
        private readonly Configuration configuration;

        public ContextMenu(IContextMenu contextMenu, Configuration configuration)
        {
            this.contextMenu = contextMenu;
            this.configuration = configuration;
            this.contextMenu.OnMenuOpened += OnMenuOpened;
        }

        private void OnMenuOpened(IMenuOpenedArgs args)
        {
            if (!configuration.EnableItemContextMenuEntries)
                return;

            if (!TryGetContextItem(args, out var item))
                return;

            args.AddMenuItem(CreateRootItem(item));
        }

        private MenuItem CreateRootItem(Item item)
        {
            return new MenuItem
            {
                Name = "FCCH",
                PrefixChar = 'F',
                IsSubmenu = true,
                OnClicked = args => args.OpenSubmenu(CreateSubmenuItems(item))
            };
        }

        private IReadOnlyList<IMenuItem> CreateSubmenuItems(Item item)
        {
            var inCustom = IsInCustomList(item.RowId);
            var inIgnore = IsInIgnoreList(item.RowId);
            var items = new List<IMenuItem>();

            items.Add(inCustom
                ? ActionItem("Remove from Custom List", () => RemoveFromCustomList(item))
                : ActionItem("Add to Custom List", () => AddToCustomList(item)));

            items.Add(inIgnore
                ? ActionItem("Remove from Ignore List", () => RemoveFromIgnoreList(item))
                : ActionItem("Add to Ignore List", () => AddToIgnoreList(item)));

            return items;
        }

        private static MenuItem ActionItem(string name, System.Action action)
        {
            return new MenuItem
            {
                Name = name,
                OnClicked = _ => action()
            };
        }

        private static bool TryGetContextItem(IMenuArgs args, out Item item)
        {
            item = default;

            if (args.MenuType != ContextMenuType.Inventory)
                return false;

            if (args.Target is not MenuTargetInventory inventoryTarget)
                return false;

            var targetItem = inventoryTarget.TargetItem;
            if (targetItem == null || targetItem.Value.IsEmpty)
                return false;

            var itemId = targetItem.Value.BaseItemId;
            if (itemId == 0)
                return false;

            return ItemListEligibility.TryGetAllowedItem(itemId, out item);
        }

        private bool IsInCustomList(uint itemId)
        {
            return configuration.WithdrawItems.Any(x => x.ItemId == itemId);
        }

        private bool IsInIgnoreList(uint itemId)
        {
            return configuration.IgnoreList.Any(x => x.ItemId == itemId);
        }

        private void AddToCustomList(Item item)
        {
            if (IsInCustomList(item.RowId))
                return;

            configuration.WithdrawItems.Add(new WithdrawItem
            {
                ItemId = item.RowId,
                Quantity = 1,
                Mode = CustomItemMode.Both,
                AlwaysMax = false
            });

            SortCustomList();
            configuration.Save();
            ChatHelper.Info($"Added {item.Name} to Custom list.");
        }

        private void AddToIgnoreList(Item item)
        {
            if (IsInIgnoreList(item.RowId))
                return;

            configuration.IgnoreList.Add(new Configuration.IgnoredItem
            {
                ItemId = item.RowId,
                Name = item.Name.ToString(),
                IgnoreEntrust = true,
                IgnoreWithdraw = true
            });

            SortIgnoreList();
            configuration.Save();
            ChatHelper.Info($"Added {item.Name} to Ignore list.");
        }

        private void RemoveFromCustomList(Item item)
        {
            var removed = configuration.WithdrawItems.RemoveAll(x => x.ItemId == item.RowId);
            if (removed == 0)
                return;

            configuration.Save();
            ChatHelper.Info($"Removed {item.Name} from Custom list.");
        }

        private void RemoveFromIgnoreList(Item item)
        {
            var removed = configuration.IgnoreList.RemoveAll(x => x.ItemId == item.RowId);
            if (removed == 0)
                return;

            configuration.Save();
            ChatHelper.Info($"Removed {item.Name} from Ignore list.");
        }

        private void SortCustomList()
        {
            configuration.WithdrawItems.Sort((a, b) =>
                string.Compare(GetItemName(a.ItemId), GetItemName(b.ItemId), StringComparison.OrdinalIgnoreCase));
        }

        private void SortIgnoreList()
        {
            configuration.IgnoreList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetItemName(uint itemId)
        {
            return Plugin.Data.GetExcelSheet<Item>()?.GetRowOrDefault(itemId)?.Name.ToString() ?? string.Empty;
        }

        public void Dispose()
        {
            contextMenu.OnMenuOpened -= OnMenuOpened;
        }
    }
}
