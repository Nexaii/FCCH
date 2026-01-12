using System;

namespace FC_Chest_Helper.GameData
{
    public static class NetworkDecoder
    {
        public static unsafe Common.ContainerInfo DecodeContainerInfo(IntPtr dataPtr)
        {
            return *(Common.ContainerInfo*)dataPtr;
        }
    }
}
