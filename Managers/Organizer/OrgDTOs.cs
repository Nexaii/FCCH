using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Models;

namespace FCCH.Managers.Organizer
{
    public enum OrgOperationMode
    {
        Move,
        Sort
    }

    public enum OrgSortOrder
    {
        ByCategory,
        ById,
        ByName,
        ByQuantity
    }

    public enum OrgFilterCategory
    {
        AllItems,
        Equipment,
        MedicinesMeals,
        Materials,
        Materia,
        Registrable,
        Dye,
        Housing,
        Gardening,
        Miscellaneous
    }

    public enum OrgJobStatus
    {
        Idle,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public class OrgJobRequest
    {
        public OrgOperationMode Mode { get; set; } = OrgOperationMode.Move;
        public InventoryType SourceTab { get; set; } = InventoryType.FreeCompanyPage1;
        public InventoryType DestTab { get; set; } = InventoryType.FreeCompanyPage2;
        public HashSet<OrgFilterCategory> Filters { get; set; } = new() { OrgFilterCategory.AllItems };
        public OrgSortOrder SortOrder { get; set; } = OrgSortOrder.ByCategory;
        public bool SortDescending { get; set; } = false;
        public HashSet<uint> SelectedItemIds { get; set; } = new();
        public bool SelectAll { get; set; } = true;
    }

    public class OrgCheckResult
    {
        public bool IsValid { get; set; }
        public string StatusMessage { get; set; } = "";

        public int PlayerFreeSlots { get; set; }
        public int StackCount { get; set; }
        public bool PlayerBufferOK { get; set; }

        public int DestFreeSlots { get; set; }
        public int NetSlotsNeeded { get; set; }
        public bool DestCapacityOK { get; set; }

        public List<OrgPreviewItem> PreviewItems { get; set; } = new();
        public List<MoveOperation> WithdrawMoves { get; set; } = new();
        public List<MoveOperation> DepositMoves { get; set; } = new();
        public Dictionary<uint, uint> ExpectedCounts { get; set; } = new();
    }

    public class OrgPreviewItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public uint Quantity { get; set; }
        public bool WillMerge { get; set; }
        public bool IsSelected { get; set; } = true;
    }
}
