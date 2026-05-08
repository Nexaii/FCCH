using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Models
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
        public bool PendingSplit;
    }
}
