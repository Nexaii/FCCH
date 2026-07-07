using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FCCH.Common
{
    public static unsafe class ChestAddon
    {
        public static AtkUnitBase* GetOpen()
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FreeCompanyChestAddonName, 1);
            return addon != null && addon->IsVisible ? addon : null;
        }
    }
}
