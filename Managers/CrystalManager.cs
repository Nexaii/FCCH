using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FCCH.Common;
using FCCH.Models;
using FCCH.Diagnostics;

namespace FCCH.Managers
{
    public unsafe class CrystalManager
    {
        private readonly FCCH.Configuration _configuration;
        private readonly FCCH.Managers.ChestManager _chestManager;
        private readonly FCCH.Managers.MoveManager _moveManager;

        public static readonly uint[] ShardIds = { 2, 3, 4, 5, 6, 7 };
        public static readonly uint[] CrystalIds = { 8, 9, 10, 11, 12, 13 };
        public static readonly uint[] ClusterIds = { 14, 15, 16, 17, 18, 19 };
        public static readonly uint[] AllIds;

        static CrystalManager()
        {
            var list = new System.Collections.Generic.List<uint>();
            list.AddRange(ShardIds);
            list.AddRange(CrystalIds);
            list.AddRange(ClusterIds);
            AllIds = list.ToArray();
        }

        public CrystalManager(FCCH.Configuration configuration, FCCH.Managers.ChestManager chestManager, FCCH.Managers.MoveManager moveManager)
        {
            _configuration = configuration;
            _chestManager = chestManager;
            _moveManager = moveManager;
        }

        public bool IsManaged(uint itemId)
        {
            return _configuration.CrystalConfig.EnabledIds.Contains(itemId);
        }

        private static uint GetSlotForCrystal(uint itemId)
        {
            return itemId - 2;
        }

        public void DepositSingle(uint itemId)
        {
            if (!AllIds.Contains(itemId))
            {
                Chat.Warn("Tab 6: crystals only.");
                return;
            }

            var access = _chestManager.GetChestAccess(InventoryType.FreeCompanyCrystals);
            if (access != Constants.FCPermissions.FullAccess && access != Constants.FCPermissions.DepositOnly)
            {
                Chat.Warn("No deposit access on the crystal tab.");
                return;
            }

            InvalidateCache();
            if (!ProcessDeposit(itemId))
                Chat.Result("Nothing to deposit (at keep / none / full).");
        }

        public void WithdrawSingle(uint itemId)
        {
            if (!AllIds.Contains(itemId))
            {
                Chat.Warn("Not a crystal.");
                return;
            }

            if (_chestManager.GetChestAccess(InventoryType.FreeCompanyCrystals) != Constants.FCPermissions.FullAccess)
            {
                Chat.Warn("No withdraw access on the crystal tab.");
                return;
            }

            InvalidateCache();
            if (!ProcessWithdraw(itemId))
                Chat.Result("Nothing to withdraw (at 9999 / empty).");
        }

        public void Deposit(bool force = false)
        {
            if (!force && !_configuration.CrystalConfig.IncludeInDepositAll) return;
            var access = _chestManager.GetChestAccess(InventoryType.FreeCompanyCrystals);
            if (access != Constants.FCPermissions.FullAccess && access != Constants.FCPermissions.DepositOnly)
            {
                if (force) Chat.Result("Skipped dc (crystals).");
                else Log.Verbose("Skipping da for crystals.");
                return;
            }
            InvalidateCache();
            foreach (var id in AllIds)
            {
                if (!IsManaged(id)) continue;
                ProcessDeposit(id);
            }
        }

        public void DepositDuplicates()
        {
            if (!_configuration.CrystalConfig.IncludeInDepositAll) return;
            InvalidateCache();
            foreach (var id in AllIds)
            {
                if (!IsManaged(id)) continue;
                
                var fcCount = GetFCCount(id);
                if (fcCount > 0)
                {
                    ProcessDeposit(id);
                }
            }
        }

        public void Withdraw(bool force = false)
        {
            if (!force && !_configuration.CrystalConfig.IncludeInWithdrawAll) return;
            if (_chestManager.GetChestAccess(InventoryType.FreeCompanyCrystals) != Constants.FCPermissions.FullAccess)
            {
                if (force) Chat.Result("Skipped wc (crystals).");
                else Log.Verbose("Skipping wa for crystals.");
                return;
            }
            InvalidateCache();
            foreach (var id in AllIds)
            {
                if (!IsManaged(id)) continue;
                ProcessWithdraw(id);
            }
        }

        private bool ProcessDeposit(uint itemId)
        {
            var pCount = GetPlayerCount(itemId);
            if (pCount == 0) return false;

            var keep = _configuration.CrystalConfig.GlobalKeepAmount;
            if (_configuration.CrystalConfig.CustomKeepAmounts.TryGetValue(itemId, out int custom))
            {
                keep = custom;
            }

            if (pCount <= keep) return false;

            var toMove = pCount - keep;

            var fcCount = GetFCCount(itemId);
            var fcSpace = 9999 - fcCount;

            if (toMove > fcSpace) toMove = fcSpace;
            if (toMove <= 0) return false;

            _moveManager.Enqueue(new MoveOperation
            {
                Amount = (uint)toMove,
                ItemId = itemId,
                SrcInv = InventoryType.Crystals,
                SrcSlot = GetSlotForCrystal(itemId),
                DstInv = InventoryType.FreeCompanyCrystals,
                DstSlot = GetSlotForCrystal(itemId),
                IsNativeMove = true
            });
            return true;
        }

        private bool ProcessWithdraw(uint itemId)
        {
            var pCount = GetPlayerCount(itemId);
            if (pCount >= 9999) return false;

            var needed = 9999 - pCount;

            var fcCount = GetFCCount(itemId);

            if (needed > fcCount) needed = fcCount;
            if (needed <= 0) return false;

            _moveManager.Enqueue(new MoveOperation
            {
                Amount = (uint)needed,
                ItemId = itemId,
                SrcInv = InventoryType.FreeCompanyCrystals,
                SrcSlot = GetSlotForCrystal(itemId),
                DstInv = InventoryType.Crystals,
                DstSlot = GetSlotForCrystal(itemId),
                IsNativeMove = true
            });
            return true;
        }

        private long GetPlayerCount(uint itemId)
        {
            var inv = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Crystals);
            if (inv == null) return 0;
            
            for (int i = 0; i < inv->Size; i++)
            {
                var item = inv->GetInventorySlot(i);
                if (item != null && item->ItemId == itemId) return item->Quantity;
            }
            return 0;
        }

        private Dictionary<uint, long>? _fcCountCache;

        private void BuildFCCountCache()
        {
            _fcCountCache = new Dictionary<uint, long>();
            foreach (var page in _chestManager.ChestState.Values)
            {
                foreach (var slot in page)
                {
                    if (!_fcCountCache.ContainsKey(slot.ItemId))
                        _fcCountCache[slot.ItemId] = 0;
                    _fcCountCache[slot.ItemId] += slot.Quantity;
                }
            }
        }

        private long GetFCCount(uint itemId)
        {
            if (_fcCountCache == null) BuildFCCountCache();
            return _fcCountCache!.TryGetValue(itemId, out var count) ? count : 0;
        }

        public void InvalidateCache() => _fcCountCache = null;
    }
}
