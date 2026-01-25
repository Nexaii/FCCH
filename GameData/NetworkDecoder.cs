using FCCH.Common;
using System;

namespace FCCH.GameData
{
    public static class NetworkDecoder
    {
        public static unsafe ContainerInfo DecodeContainerInfo(IntPtr dataPtr)
        {
            return *(ContainerInfo*)dataPtr;
        }
    }
}
