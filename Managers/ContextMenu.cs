using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FCCH.Common;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace FCCH.Managers
{
    internal sealed unsafe class ContextMenu : IDisposable
    {
        private const string ContextMenuAddonName = "ContextMenu";

        private readonly IContextMenu contextMenu;
        private readonly Configuration configuration;
        private readonly ChestHelper chestHelper;
        private readonly IKeyState keyState;
        private bool armClose;

        public ContextMenu(IContextMenu contextMenu, Configuration configuration, ChestHelper chestHelper, IKeyState keyState)
        {
            this.contextMenu = contextMenu;
            this.configuration = configuration;
            this.chestHelper = chestHelper;
            this.keyState = keyState;
            this.contextMenu.OnMenuOpened += OnMenuOpened;
            Plugin.Framework.Update += OnFrameworkUpdate;
        }

        private void OnMenuOpened(IMenuOpenedArgs args)
        {
            if (configuration.FastMoveEnabled && TryHandleFastMove(args))
                return;

            if (!configuration.EnableItemContextMenuEntries)
                return;

            if (!TryGetContextItem(args, out var item))
                return;

            args.AddMenuItem(CreateRootItem(item));
        }

        private bool TryHandleFastMove(IMenuOpenedArgs args)
        {
            if (!ModifierHeld())
                return false;

            if (TryResolvePlayerSource(args, out var srcType, out var srcSlot))
            {
                var destTab = ResolveDepositTab();
                if (destTab == InventoryType.Invalid)
                {
                    ChatHelper.Info("Fast Move: open a chest tab or hold a number key 1-5 to pick the deposit tab.");
                    return true;
                }

                chestHelper.ProcessCommand(() => chestHelper.DepositItemToTab(srcType, srcSlot, destTab));
                armClose = true;
                return true;
            }

            if (TryResolveChestSource(args, out var srcPage, out var itemId, out var amount))
            {
                chestHelper.ProcessCommand(() => chestHelper.WithdrawItemStack(srcPage, itemId, amount));
                armClose = true;
                return true;
            }

            return true;
        }

        private bool TryResolvePlayerSource(IMenuOpenedArgs args, out InventoryType srcType, out uint srcSlot)
        {
            srcType = InventoryType.Invalid;
            srcSlot = 0;

            var agent = AgentInventoryContext.Instance();
            if (agent == null || args.AgentPtr != (nint)agent)
                return false;

            var source = agent->TargetInventorySlot;
            var container = agent->TargetInventoryId;
            var slotId = agent->TargetInventorySlotId;
            if (source == null || source->ItemId == 0 || slotId < 0)
                return false;
            if (container < InventoryType.Inventory1 || container > InventoryType.Inventory4)
                return false;

            srcType = container;
            srcSlot = (uint)slotId;
            return true;
        }

        private bool TryResolveChestSource(IMenuOpenedArgs args, out InventoryType srcPage, out uint itemId, out int amount)
        {
            srcPage = InventoryType.Invalid;
            itemId = 0;
            amount = 0;

            if (args.AddonName != Constants.FC_CHEST_ADDON_NAME)
                return false;

            var page = chestHelper.GetOpenChestPage();
            if (page < InventoryType.FreeCompanyPage1 || page > InventoryType.FreeCompanyPage5)
                return false;

            var detail = AgentItemDetail.Instance();
            if (detail == null)
                return false;

            var slot = chestHelper.GetChestSlot(page, (int)detail->Index);
            if (slot == null || slot.Value.ItemId == 0 || slot.Value.ItemId != detail->ItemId)
                return false;

            srcPage = page;
            itemId = slot.Value.ItemId;
            amount = (int)slot.Value.Quantity;
            return true;
        }

        private bool ModifierHeld()
        {
            return Held(configuration.FastMoveModifier);
        }

        private bool Held(VirtualKey vk)
            => keyState.IsVirtualKeyValid(vk) && keyState[vk];

        private InventoryType ResolveDepositTab()
        {
            for (int d = 1; d <= 5; d++)
            {
                if (Held((VirtualKey)(48 + d)) || Held((VirtualKey)(96 + d)))
                    return (InventoryType)((int)InventoryType.FreeCompanyPage1 + (d - 1));
            }

            var page = chestHelper.GetOpenChestPage();
            if (page >= InventoryType.FreeCompanyPage1 && page <= InventoryType.FreeCompanyPage5)
                return page;

            return InventoryType.Invalid;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!armClose)
                return;

            armClose = false;
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(ContextMenuAddonName, 1);
            if (addon == null || !addon->IsVisible)
                return;

            addon->FireCallbackInt(-2);
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

        private static string GetItemName(uint itemId) => Common.ItemNames.Get(itemId);

        public void Dispose()
        {
            contextMenu.OnMenuOpened -= OnMenuOpened;
            Plugin.Framework.Update -= OnFrameworkUpdate;
        }
    }
}
