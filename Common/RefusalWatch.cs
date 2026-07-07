using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    public unsafe class RefusalWatch : IDisposable
    {
        private static readonly HashSet<uint> PinnedRefusalLogMessageIds = new()
        {
            1860, // Unable to obtain company chest data.
            1861, // Unable to complete company chest action.
            1862, // Unable to access company chest. You are not a member of this free company.
            1863, // The company chest is inaccessible at this time.
            1865, // Equipped items cannot be stored in the company chest.
            1866, // Unique, untradable, or bound items cannot be stored in the company chest.
            1867, // Unable to store item.
            1869, // Unable to store item. The stack contained in the chest is full.
            1870, // Unable to retrieve item. The stack in your inventory is full.
            1871, // The company chest can hold no more gil.
            1873, // Unable to store item. Another player is using the chest.
            1874, // Unable to retrieve item. Another player is using the chest.
            3145, // Company chest access is currently restricted.
            3490, // Insufficient space in inventory. Unable to retrieve item.
            4315, // Glamoured items cannot be stored in the company chest.
        };

        private static readonly string[] DiscoveryKeywords =
        {
            "company chest",
            "Unable to store item",
            "Unable to retrieve item",
        };

        private readonly HashSet<uint> _refusalLogIds = new(PinnedRefusalLogMessageIds);
        private bool _initialized;

        public DateTime LastRefusalUtc { get; private set; } = DateTime.MinValue;
        public uint LastRefusalLogId { get; private set; }

        private delegate void ShowLogMessageDelegate(RaptureLogModule* thisPtr, uint logMessageId);
        private delegate void ShowLogMessageUIntDelegate(RaptureLogModule* thisPtr, uint logMessageId, uint value);
        private delegate void ShowLogMessageStringDelegate(RaptureLogModule* thisPtr, uint logMessageId, FFXIVClientStructs.FFXIV.Client.System.String.Utf8String* value);

        private Hook<ShowLogMessageDelegate>? _hookShow;
        private Hook<ShowLogMessageUIntDelegate>? _hookShowUInt;
        private Hook<ShowLogMessageStringDelegate>? _hookShowString;

        public RefusalWatch()
        {
            try
            {
                DiscoverAdditionalRefusalLogIds();
                InstallHooks();
                _initialized = _hookShow != null || _hookShowUInt != null || _hookShowString != null;

                if (!_initialized)
                {
                    FCCHLog.Warning("[RefusalWatch] Not initialized. hooks="
                        + (_hookShow != null ? "S" : "-")
                        + (_hookShowUInt != null ? "U" : "-")
                        + (_hookShowString != null ? "T" : "-"));
                }
                else
                {
                    FCCHLog.Info($"[RefusalWatch] Watching {_refusalLogIds.Count} LogMessage IDs for inventory refusals ({PinnedRefusalLogMessageIds.Count} pinned).");
                }
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[RefusalWatch] Initialization failed.");
            }
        }

        public bool ConsumeRefusalSince(DateTime utc)
        {
            if (LastRefusalUtc > utc)
            {
                LastRefusalUtc = DateTime.MinValue;
                return true;
            }
            return false;
        }

        private void DiscoverAdditionalRefusalLogIds()
        {
            var sheet = Plugin.Data.GetExcelSheet<LogMessage>(Dalamud.Game.ClientLanguage.English);
            if (sheet == null) return;

            foreach (var row in sheet)
            {
                if (_refusalLogIds.Contains(row.RowId)) continue;

                string text;
                try { text = row.Text.ExtractText(); }
                catch { continue; }
                if (string.IsNullOrEmpty(text)) continue;
                if (text.IndexOf("storeroom", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                foreach (var kw in DiscoveryKeywords)
                {
                    if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!LooksLikeRefusal(text)) break;

                    _refusalLogIds.Add(row.RowId);
                    FCCHLog.Warning($"[RefusalWatch] Unpinned refusal LogMessage#{row.RowId}: \"{text}\" (add to pinned set)");
                    break;
                }
            }
        }

        private static bool LooksLikeRefusal(string text)
            => text.IndexOf("Unable", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("restricted", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("no more", StringComparison.OrdinalIgnoreCase) >= 0;

        private void InstallHooks()
        {
            var module = RaptureLogModule.Instance();
            if (module == null) { FCCHLog.Warning("[RefusalWatch] RaptureLogModule.Instance() null."); return; }

            try
            {
                _hookShow = Plugin.GameInteropProvider.HookFromAddress<ShowLogMessageDelegate>(
                    (nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessage, OnShowLogMessage);
                _hookShow?.Enable();
            }
            catch (Exception ex) { FCCHLog.Error(ex, "[RefusalWatch] Hook ShowLogMessage failed."); }

            try
            {
                _hookShowUInt = Plugin.GameInteropProvider.HookFromAddress<ShowLogMessageUIntDelegate>(
                    (nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessageUInt, OnShowLogMessageUInt);
                _hookShowUInt?.Enable();
            }
            catch (Exception ex) { FCCHLog.Error(ex, "[RefusalWatch] Hook ShowLogMessageUInt failed."); }

            try
            {
                _hookShowString = Plugin.GameInteropProvider.HookFromAddress<ShowLogMessageStringDelegate>(
                    (nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessageString, OnShowLogMessageString);
                _hookShowString?.Enable();
            }
            catch (Exception ex) { FCCHLog.Error(ex, "[RefusalWatch] Hook ShowLogMessageString failed."); }
        }

        private void OnShowLogMessage(RaptureLogModule* thisPtr, uint logMessageId)
        {
            MarkIfRefusal(logMessageId);
            _hookShow!.Original(thisPtr, logMessageId);
        }

        private void OnShowLogMessageUInt(RaptureLogModule* thisPtr, uint logMessageId, uint value)
        {
            MarkIfRefusal(logMessageId);
            _hookShowUInt!.Original(thisPtr, logMessageId, value);
        }

        private void OnShowLogMessageString(RaptureLogModule* thisPtr, uint logMessageId, FFXIVClientStructs.FFXIV.Client.System.String.Utf8String* value)
        {
            MarkIfRefusal(logMessageId);
            _hookShowString!.Original(thisPtr, logMessageId, value);
        }

        private void MarkIfRefusal(uint logMessageId)
        {
            if (_refusalLogIds.Contains(logMessageId))
            {
                LastRefusalUtc = DateTime.UtcNow;
                LastRefusalLogId = logMessageId;
            }
        }

        public void Dispose()
        {
            _hookShow?.Disable(); _hookShow?.Dispose(); _hookShow = null;
            _hookShowUInt?.Disable(); _hookShowUInt?.Dispose(); _hookShowUInt = null;
            _hookShowString?.Disable(); _hookShowString?.Dispose(); _hookShowString = null;
        }
    }
}
