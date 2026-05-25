using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;

namespace FCCH.Managers
{
    public unsafe class InventoryScanner : IDisposable
    {
        private readonly HashSet<InventoryType> _loadedInventories = new();
        private readonly Configuration _configuration;

        private delegate void FcBitsetSetterDelegate(nint state, uint byteIndex, byte newByte);

        [Signature("83 FA 26 0F 83 ?? ?? ?? ?? 55 57 48 83 EC 28 8B EA 41 0F B6 F8",
                   DetourName = nameof(FcBitsetSetterDetour), UseFlags = SignatureUseFlags.Hook)]
        private Hook<FcBitsetSetterDelegate>? _fcBitsetHook = null;

        private static readonly InventoryType[] FC_ITEM_PAGES = {
            InventoryType.FreeCompanyPage1,
            InventoryType.FreeCompanyPage2,
            InventoryType.FreeCompanyPage3,
            InventoryType.FreeCompanyPage4,
            InventoryType.FreeCompanyPage5,
        };

        private static readonly InventoryType[] FC_NON_ITEM = {
            InventoryType.FreeCompanyGil,
            InventoryType.FreeCompanyCrystals,
        };

        public InventoryScanner(Configuration configuration)
        {
            _configuration = configuration;
            Plugin.GameInteropProvider.InitializeFromAttributes(this);

            if (_fcBitsetHook != null)
            {
                _fcBitsetHook.Enable();
                DebugLog("[InventoryScanner] FC bitset setter (140BDF390) hook resolved and enabled.");
            }
            else
            {
                FCCH.Common.FCCHLog.Warning("[InventoryScanner] FC bitset setter signature mismatch - hook not resolved.");
                DebugLog("[InventoryScanner] FC bitset setter signature mismatch - hook not resolved.");
            }
        }

        public void Dispose()
        {
            if (_fcBitsetHook != null)
            {
                if (_fcBitsetHook.IsEnabled)
                    _fcBitsetHook.Disable();
                _fcBitsetHook.Dispose();
            }
            _loadedInventories.Clear();
        }

        private void FcBitsetSetterDetour(nint state, uint byteIndex, byte newByte)
        {
            byte oldByte = 0;
            bool inRange = byteIndex < 0x26;

            try
            {
                if (inRange && state != 0)
                {
                    oldByte = *(byte*)(state + 0x4F1 + (nint)byteIndex);
                }
            }
            catch (Exception e)
            {
                FCCH.Common.FCCHLog.Error(e, "[InventoryScanner] Failed to read old FC bitset byte.");
            }

            _fcBitsetHook!.Original(state, byteIndex, newByte);

            if (!inRange) return;

            try
            {
                byte setBits = (byte)(newByte & (oldByte ^ newByte));
                byte clearedBits = (byte)(oldByte & (oldByte ^ newByte));

                for (int bit = 0; bit < 8; bit++)
                {
                    int containerId = 20000 + (int)byteIndex * 8 + bit;
                    if (containerId > 20004) break;

                    if ((setBits & (1 << bit)) != 0)
                    {
                        var t = (InventoryType)containerId;
                        _loadedInventories.Add(t);
                        DebugLog($"[InventoryScanner] FC page loaded: byteIndex={byteIndex} bit={bit} id={containerId} ({t}) old=0x{oldByte:X2} new=0x{newByte:X2}");
                    }
                    else if ((clearedBits & (1 << bit)) != 0)
                    {
                        var t = (InventoryType)containerId;
                        _loadedInventories.Remove(t);
                        DebugLog($"[InventoryScanner] FC page unloaded: byteIndex={byteIndex} bit={bit} id={containerId} ({t}) old=0x{oldByte:X2} new=0x{newByte:X2}");
                    }
                }
            }
            catch (Exception e)
            {
                FCCH.Common.FCCHLog.Error(e, "[InventoryScanner] FC bitset processing failed.");
            }
        }

        public void ResetSession()
        {
            _loadedInventories.Clear();
            DebugLog("[InventoryScanner] Session reset; cleared loaded inventories.");
        }

        public void MarkObserved(InventoryType type)
        {
            if (IsFCItemPage(type))
            {
                _loadedInventories.Add(type);
                DebugLog($"[InventoryScanner] Marked observed: {type}");
            }
        }

        public void Update()
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName<AtkUnitBase>(Constants.FC_CHEST_ADDON_NAME, 1);

            if (addon == null || !addon->IsVisible)
            {
                if (_loadedInventories.Count > 0)
                {
                    _loadedInventories.Clear();
                    DebugLog("[InventoryScanner] Chest closed; cleared loaded inventories.");
                }
            }
        }

        public bool IsInventoryLoaded(InventoryType type)
        {
            if (IsFCItemPage(type))
            {
                if (_loadedInventories.Contains(type)) return true;
                var c = InventoryManager.Instance()->GetInventoryContainer(type);
                return c != null && c->IsLoaded;
            }

            var container = InventoryManager.Instance()->GetInventoryContainer(type);
            if (container == null) return false;
            return container->IsLoaded;
        }

        private static bool IsFCItemPage(InventoryType type) => Array.IndexOf(FC_ITEM_PAGES, type) >= 0;
        private static bool IsFCNonItem(InventoryType type) => Array.IndexOf(FC_NON_ITEM, type) >= 0;

        public InventoryContainer* GetContainer(InventoryType type)
        {
            if (IsFCItemPage(type) && !IsInventoryLoaded(type)) return null;
            return InventoryManager.Instance()->GetInventoryContainer(type);
        }

        private void DebugLog(string msg)
        {
            if (!_configuration.DebugMode) return;
            FCCH.Common.FCCHLog.Info(msg);
            Common.DebugFileLogger.Enqueue(_configuration.DebugLogPath, msg);
        }
    }
}
