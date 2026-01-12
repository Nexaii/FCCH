using FFXIVClientStructs.FFXIV.Client.Game;

namespace FC_Chest_Helper.Models
{
    public struct MoveOperation
    {
        public InventoryType SrcInv;
        public uint SrcSlot;
        public InventoryType DstInv;
        public uint DstSlot;
        public uint ItemId;
        public uint Amount;
        public bool IsNativeMove;
    }
}
