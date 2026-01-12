using System.Runtime.InteropServices;

namespace FC_Chest_Helper.Common
{
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct ContainerInfo
    {
        [FieldOffset(0)]
        public uint ContainerSequence;
        [FieldOffset(8)]
        public uint NumItems;
        [FieldOffset(16)]
        public uint ContainerId;
        [FieldOffset(24)]
        public uint StartOrFinish;
    }
}
