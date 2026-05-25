using FFXIVClientStructs.FFXIV.Client.Game;

namespace FCCH.Common
{
    public static unsafe class InventoryOpGate
    {
        public static bool HasPendingOperation()
        {
            var manager = InventoryManager.Instance();
            if (manager == null) return false;

            var ops = manager->PendingOperations;
            for (var i = 0; i < ops.Length; i++)
            {
                if (!ops[i].IsEmpty) return true;
            }
            return false;
        }
    }
}
