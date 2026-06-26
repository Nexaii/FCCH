using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FCCH.Common
{
    public static unsafe class ChestAddon
    {
        public static AtkUnitBase* GetOpen()
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);
            return addon != null && addon->IsVisible ? addon : null;
        }
    }
}
