using FC_Chest_Helper.GameData;

namespace FC_Chest_Helper.Models
{
    public class ShoppingItem
    {
        public WorkshopCraft Craft { get; set; } = null!;
        public int Quantity { get; set; }
    }
}
