using System;
using System.Diagnostics;

namespace FCCH.Common
{
    public static class PerfCounter
    {
        private static readonly object _gate = new();
        private static long _onUpdateTicksAccumulator;
        private static int _onUpdateSamples;
        private static int _updateChestStateCalls;
        private static int _scanFCChestCalls;
        private static int _opLockDetourCalls;
        private static DateTime _windowStartUtc = DateTime.UtcNow;

        private const double WindowSeconds = 1.0;

        public static void RecordOnUpdate(long elapsedTicks)
        {
            if (!IsEnabled()) return;
            lock (_gate)
            {
                _onUpdateTicksAccumulator += elapsedTicks;
                _onUpdateSamples++;
            }
        }

        public static void RecordUpdateChestState()
        {
            if (!IsEnabled()) return;
            lock (_gate) { _updateChestStateCalls++; }
        }

        public static void RecordScanFCChest()
        {
            if (!IsEnabled()) return;
            lock (_gate) { _scanFCChestCalls++; }
        }

        public static void RecordOpLockDetour()
        {
            if (!IsEnabled()) return;
            lock (_gate) { _opLockDetourCalls++; }
        }

        public static void TickAndMaybeFlush()
        {
            if (!IsEnabled()) return;

            string? line = null;
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _windowStartUtc).TotalSeconds;
                if (elapsed < WindowSeconds) return;

                double avgUs = 0;
                if (_onUpdateSamples > 0)
                {
                    double totalMs = _onUpdateTicksAccumulator * 1000.0 / Stopwatch.Frequency;
                    avgUs = (totalMs * 1000.0) / _onUpdateSamples;
                }

                line = $"[Perf] win={elapsed:F2}s onUpdate avg={avgUs:F1}us samples={_onUpdateSamples} | UpdateChestState/s={_updateChestStateCalls / elapsed:F2} ScanFCChest/s={_scanFCChestCalls / elapsed:F2} OpLock/s={_opLockDetourCalls / elapsed:F2}";

                _onUpdateTicksAccumulator = 0;
                _onUpdateSamples = 0;
                _updateChestStateCalls = 0;
                _scanFCChestCalls = 0;
                _opLockDetourCalls = 0;
                _windowStartUtc = now;
            }

            try { Plugin.PluginLog.Info(line); } catch { }
        }

        private static bool IsEnabled()
        {
            try { return Plugin.Configuration?.DebugMode == true; }
            catch { return false; }
        }
    }
}
