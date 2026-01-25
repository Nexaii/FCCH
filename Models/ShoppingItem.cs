using FCCH.GameData;

namespace FCCH.Models
{
    public class ShoppingItem
    {
        public required WorkshopCraft Craft { get; set; }
        public int Quantity { get; set; }
    }
}
