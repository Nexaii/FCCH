using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace FCCH.Common
{
    // Primary permission gating comes from the FreeCompanyChest addon state. Keeping this watcher as a
    // fallback for runtime refusals the permission model cannot predict: full stacks, full inventory,
    // unique-item limits, concurrent chest use, and future patch drift.
    public unsafe class InventoryRefusalWatcher : IDisposable
    {
        private static readonly string[] RefusalKeywords =
        {
            "Unable to store",
            "Unable to retrieve",
            "Unable to deposit",
            "Unable to withdraw",
            "FC chest is full",
            "company chest is full",
            "free company chest is full",
            "Unique, untradable, or bound items cannot be stored in the company chest",
        };

        private readonly HashSet<uint> _refusalLogIds = new();
        private bool _initialized;

        public DateTime LastRefusalUtc { get; private set; } = DateTime.MinValue;
        public uint LastRefusalLogId { get; private set; }

        private delegate void ShowLogMessageDelegate(RaptureLogModule* thisPtr, uint logMessageId);
        private delegate void ShowLogMessageUIntDelegate(RaptureLogModule* thisPtr, uint logMessageId, uint value);
        private delegate void ShowLogMessageStringDelegate(RaptureLogModule* thisPtr, uint logMessageId, FFXIVClientStructs.FFXIV.Client.System.String.Utf8String* value);

        private Hook<ShowLogMessageDelegate>? _hookShow;
        private Hook<ShowLogMessageUIntDelegate>? _hookShowUInt;
        private Hook<ShowLogMessageStringDelegate>? _hookShowString;

        public InventoryRefusalWatcher()
        {
            try
            {
                DiscoverRefusalLogIds();
                InstallHooks();
                _initialized = _refusalLogIds.Count > 0
                               && (_hookShow != null || _hookShowUInt != null || _hookShowString != null);

                if (!_initialized)
                {
                    Plugin.PluginLog.Warning("[RefusalWatcher] Not initialized. ids=" + _refusalLogIds.Count
                        + " hooks=" + (_hookShow != null ? "S" : "-")
                        + (_hookShowUInt != null ? "U" : "-")
                        + (_hookShowString != null ? "T" : "-"));
                }
                else
                {
                    Plugin.PluginLog.Info($"[RefusalWatcher] Watching {_refusalLogIds.Count} LogMessage IDs for inventory refusals.");
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, "[RefusalWatcher] Initialization failed.");
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

        private void DiscoverRefusalLogIds()
        {
            var sheet = Plugin.Data.GetExcelSheet<LogMessage>();
            if (sheet == null) return;

            foreach (var row in sheet)
            {
                string text;
                try { text = row.Text.ExtractText(); }
                catch { continue; }
                if (string.IsNullOrEmpty(text)) continue;

                foreach (var kw in RefusalKeywords)
                {
                    if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _refusalLogIds.Add(row.RowId);
                        Plugin.PluginLog.Info($"[RefusalWatcher] LogMessage#{row.RowId}: \"{text}\"");
                        break;
                    }
                }
            }
        }

        private void InstallHooks()
        {
            var module = RaptureLogModule.Instance();
            if (module == null) { Plugin.PluginLog.Warning("[RefusalWatcher] RaptureLogModule.Instance() null."); return; }

            try
            {
                _hookShow = Plugin.GameInteropProvider.HookFromAddress<ShowLogMessageDelegate>(
                    (nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessage, OnShowLogMessage);
                _hookShow?.Enable();
            }
            catch (Exception ex) { Plugin.PluginLog.Error(ex, "[RefusalWatcher] Hook ShowLogMessage failed."); }

            try
            {
                _hookShowUInt = Plugin.GameInteropProvider.HookFromAddress<ShowLogMessageUIntDelegate>(
                    (nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessageUInt, OnShowLogMessageUInt);
                _hookShowUInt?.Enable();
            }
            catch (Exception ex) { Plugin.PluginLog.Error(ex, "[RefusalWatcher] Hook ShowLogMessageUInt failed."); }

            try
            {
                _hookShowString = Plugin.GameInteropProvider.HookFromAddress<ShowLogMessageStringDelegate>(
                    (nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessageString, OnShowLogMessageString);
                _hookShowString?.Enable();
            }
            catch (Exception ex) { Plugin.PluginLog.Error(ex, "[RefusalWatcher] Hook ShowLogMessageString failed."); }
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
