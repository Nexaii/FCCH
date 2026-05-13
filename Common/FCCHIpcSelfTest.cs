using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin;

namespace FCCH.Common
{
    internal sealed class FCCHIpcSelfTest
    {
        private const string Prefix = "FCCH.";
        private const int ExpectedContractVersion = 3;

        private const string TokenReady = "";
        private const string TokenBusy = "busy";
        private const string TokenChestClosed = "chest-closed";
        private const string TokenUnavailable = "unavailable";

        private static readonly HashSet<string> AllowedTokens = new()
        {
            TokenReady,
            TokenBusy,
            TokenChestClosed,
            TokenUnavailable,
        };

        private static readonly HashSet<string> GateOpenTokens = new() { TokenReady, TokenChestClosed };
        private static readonly HashSet<string> GateClosedTokens = new() { TokenBusy, TokenUnavailable };

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly List<string> _report = new();
        private int _passCount;
        private int _failCount;

        internal FCCHIpcSelfTest(IDalamudPluginInterface pluginInterface)
        {
            _pluginInterface = pluginInterface;
        }

        internal void Run()
        {
            _report.Clear();
            _passCount = 0;
            _failCount = 0;

            CheckIsAvailable();
            CheckContractVersion();
            CheckReadinessPair();
            CheckBusyMatchesGate();
            CheckTOCTOU();

            Emit();
        }

        private void CheckIsAvailable()
        {
            try
            {
                var sub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "IsAvailable");
                var value = sub.InvokeFunc();
                Record("IsAvailable", value, value == true);
            }
            catch (Exception e)
            {
                Fail("IsAvailable", e);
            }
        }

        private void CheckContractVersion()
        {
            try
            {
                var sub = _pluginInterface.GetIpcSubscriber<int>(Prefix + "GetVersion");
                var value = sub.InvokeFunc();
                Record("GetVersion", value, value == ExpectedContractVersion, $"expected {ExpectedContractVersion}");
            }
            catch (Exception e)
            {
                Fail("GetVersion", e);
            }
        }

        private void CheckReadinessPair()
        {
            try
            {
                var canSub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "CanAcceptCommand");
                var reasonSub = _pluginInterface.GetIpcSubscriber<string>(Prefix + "GetBlockReason");

                var canAccept = canSub.InvokeFunc();
                var reason = reasonSub.InvokeFunc();

                if (reason == null)
                {
                    Record("GetBlockReason non-null", "null", false);
                    return;
                }

                var tokenInSet = AllowedTokens.Contains(reason);
                Record("Token in documented set", Display(reason), tokenInSet);

                var pairConsistent = canAccept
                    ? GateOpenTokens.Contains(reason)
                    : GateClosedTokens.Contains(reason);
                Record("CanAcceptCommand/GetBlockReason classify", $"can={canAccept} reason={Display(reason)}", pairConsistent);
            }
            catch (Exception e)
            {
                Fail("ReadinessPair", e);
            }
        }

        private void CheckBusyMatchesGate()
        {
            try
            {
                var canSub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "CanAcceptCommand");
                var busySub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "IsBusy");

                var canAccept = canSub.InvokeFunc();
                var busy = busySub.InvokeFunc();
                Record("IsBusy == !CanAcceptCommand", $"can={canAccept} busy={busy}", busy == !canAccept);
            }
            catch (Exception e)
            {
                Fail("BusyMatchesGate", e);
            }
        }

        private void CheckTOCTOU()
        {
            try
            {
                var canSub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "CanAcceptCommand");
                var stopSub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "Stop");
                var depositSub = _pluginInterface.GetIpcSubscriber<bool>(Prefix + "DepositAll");

                var canAccept = canSub.InvokeFunc();
                if (!canAccept)
                {
                    Record("TOCTOU demo skipped", "gate not open", true);
                    return;
                }

                var first = depositSub.InvokeFunc();
                var second = depositSub.InvokeFunc();
                stopSub.InvokeFunc();

                Record("Mutation first call accepted", $"{first}", first == true);
                Record("Mutation second call refused", $"{second}", second == false);
            }
            catch (Exception e)
            {
                Fail("TOCTOU", e);
            }
        }

        private void Record(string name, object value, bool ok, string? note = null)
        {
            var status = ok ? "PASS" : "FAIL";
            if (ok) _passCount++; else _failCount++;
            var line = note == null
                ? $"[{status}] {name} = {value}"
                : $"[{status}] {name} = {value} ({note})";
            _report.Add(line);
        }

        private void Fail(string name, Exception e)
        {
            _failCount++;
            _report.Add($"[FAIL] {name} threw: {e.GetType().Name}: {e.Message}");
        }

        private void Emit()
        {
            var summary = $"FCCH IPC self-test: {_passCount} pass, {_failCount} fail.";
            Plugin.PluginLog.Info($"[FCCH.IPCTest] {summary}");
            ChatHelper.Info(summary);

            var sb = new StringBuilder();
            foreach (var line in _report)
            {
                Plugin.PluginLog.Info($"[FCCH.IPCTest] {line}");
                sb.AppendLine(line);
            }

            if (_failCount > 0)
            {
                ChatHelper.Warning("FCCH IPC self-test produced failures. See /xllog for details.");
            }
        }

        private static string Display(string token) => token.Length == 0 ? "\"\"" : $"\"{token}\"";
    }
}
