using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Common
{
    public static unsafe class ItemHelper
    {
        public static InventoryItem* ResolveItem(InventoryItem* item)
        {
            int depth = 0;
            while (item != null && item->IsSymbolic && depth < 5)
            {
                item = item->GetLinkedItem();
                depth++;
            }
            return item;
        }
    }
}
