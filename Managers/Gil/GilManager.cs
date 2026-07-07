using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FCCH.Common;

namespace FCCH.Managers.Gil
{
    public unsafe class GilManager : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly ChestManager _chestManager;
        private readonly GilExecutor _executor;

        private bool _debugMode;
        private PendingGilTransaction? _pendingTransaction;

        private delegate byte FireCallbackDelegate(AtkUnitBase* addon, int valueCount, AtkValue* values, byte updateState);
        private Hook<FireCallbackDelegate>? _fireCallbackHook;

        private static readonly Regex AmountPattern = new(@"^([\d,]+(?:\.\d{1,2})?)([km%])?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public GilManager(Configuration configuration, ChestManager chestManager, MoveManager moveManager)
        {
            _configuration = configuration;
            _chestManager = chestManager;
            _executor = new GilExecutor(configuration, chestManager, moveManager, SetPendingTransaction);

            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, Constants.InputNumericAddonName, OnInputNumericSetup);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Bank", OnBankSetup);
        }

        private void SetPendingTransaction(PendingGilTransaction transaction)
        {
            _pendingTransaction = transaction;
        }

        private void OnInputNumericSetup(AddonEvent type, AddonArgs args)
        {
            var pending = _pendingTransaction;
            if (pending == null) return;

            var transaction = pending.Value;
            if (Environment.TickCount64 - transaction.TimestampMs > 5000)
            {
                _pendingTransaction = null;
                return;
            }

            var addon = (AtkUnitBase*)(nint)args.Addon;
            if (addon == null) return;

            try
            {
                var values = stackalloc AtkValue[2];
                values[0] = new AtkValue() { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int, Int = (int)transaction.Amount };
                values[1] = new AtkValue() { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int, Int = 0 };

                addon->FireCallback(1, values);

                _pendingTransaction = null;
                ChatHelper.Info($"{(transaction.IsDeposit ? "Deposited" : "Withdrew")} {transaction.Amount:N0} Gil.");
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] Failed to handle InputNumeric");
                _pendingTransaction = null;
            }
        }

        private void OnBankSetup(AddonEvent type, AddonArgs args)
        {
            var pending = _pendingTransaction;
            if (pending == null) return;

            var transaction = pending.Value;
            if (!transaction.IsDeposit) return;
            if (Environment.TickCount64 - transaction.TimestampMs > 5000)
            {
                _pendingTransaction = null;
                return;
            }

            var addon = (AtkUnitBase*)(nint)args.Addon;
            if (addon == null) return;

            try
            {
                Callback.Fire(addon, true, 3, (uint)transaction.Amount);
                Callback.Fire(addon, true, 0);
                _pendingTransaction = null;
                ChatHelper.Info($"Deposited {transaction.Amount:N0} Gil.");
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] Failed to handle Bank deposit");
                _pendingTransaction = null;
            }
        }

        private bool TryResolveDebugHook()
        {
            if (_fireCallbackHook != null) return true;
            try
            {
                var ptr = Plugin.SigScanner.ScanText(Callback.Sig);
                _fireCallbackHook = Plugin.GameInteropProvider.HookFromAddress<FireCallbackDelegate>(ptr, FireCallbackDetour);
                FCCHLog.Info("[GilManager] Debug hook resolved for FireCallback");
                return true;
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] Failed to resolve debug hook");
                return false;
            }
        }

        private void EnableDebugHook()
        {
            if (!TryResolveDebugHook()) return;
            if (_fireCallbackHook == null || _fireCallbackHook.IsEnabled) return;
            try
            {
                _fireCallbackHook.Enable();
                FCCHLog.Info("[GilManager] Debug hook enabled");
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] Failed to enable debug hook");
            }
        }

        private void DisableDebugHook()
        {
            if (_fireCallbackHook == null || !_fireCallbackHook.IsEnabled) return;
            try
            {
                _fireCallbackHook.Disable();
                FCCHLog.Info("[GilManager] Debug hook disabled");
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] Failed to disable debug hook");
            }
        }

        private byte FireCallbackDetour(AtkUnitBase* addon, int valueCount, AtkValue* values, byte updateState)
        {
            try
            {
                var addonName = addon->NameString;
                if (_debugMode && (addonName == Constants.InputNumericAddonName || addonName == Constants.FreeCompanyChestAddonName || addonName == "Bank"))
                {
                    ChatHelper.Info($"[DEBUG] {addonName} Callback Intercepted!");
                    ChatHelper.Info($"[DEBUG] valueCount={valueCount}, updateState={updateState}");

                    for (int i = 0; i < valueCount && i < 10; i++)
                    {
                        var val = values[i];
                        string valStr = val.Type switch
                        {
                            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int => $"Int={val.Int}",
                            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt => $"UInt={val.UInt}",
                            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool => $"Bool={val.Byte}",
                            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Float => $"Float={val.Float}",
                            _ => $"Type={val.Type}"
                        };
                        ChatHelper.Info($"[DEBUG] values[{i}]: {valStr}");
                    }
                }
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] FireCallbackDetour threw.");
            }

            return _fireCallbackHook!.Original(addon, valueCount, values, updateState);
        }

        public void EnableDebugMode()
        {
            _debugMode = true;
            EnableDebugHook();
            ChatHelper.Info("[GilManager] Debug mode ENABLED. Open Gil Transfer, enter amount, and click OK. Watch chat for callback data.");
        }

        public void DisableDebugMode()
        {
            _debugMode = false;
            DisableDebugHook();
            ChatHelper.Info("[GilManager] Debug mode disabled.");
        }

        public string GetPermissionString() => GilValidator.GetPermissionString(_chestManager);

        public void CancelPendingTransaction()
        {
            _pendingTransaction = null;
        }

        public bool IsValidAmountSyntax(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return false;

            var input = args.Trim().ToLower();
            return input == "all" || AmountPattern.IsMatch(input);
        }

        public void HandleDepositCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ChatHelper.Error("Usage: /fcch gd <amount> (e.g., 15k, 5m, all)");
                return;
            }

            uint playerGil = GilValidator.GetPlayerGil();
            uint fcGil = GilValidator.GetFCGilHeader();

            var access = _chestManager.GetChestAccess(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.FreeCompanyGil);
            if (access != Constants.FCPermissions.FullAccess && access != Constants.FCPermissions.DepositOnly)
            {
                ChatHelper.Info("Skipping gd for gil.");
                return;
            }

            if (!TryParseAmount(args, playerGil, fcGil, true, out uint amount, out string error))
            {
                ChatHelper.Error(error);
                return;
            }

            _executor.ExecuteDeposit(amount);
        }

        public void HandleWithdrawCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ChatHelper.Error("Usage: /fcch gw <amount> (e.g., 15k, 5m, all)");
                return;
            }

            uint playerGil = GilValidator.GetPlayerGil();
            uint fcGil = GilValidator.GetFCGilHeader(); 

            if (_chestManager.GetChestAccess(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.FreeCompanyGil) != Constants.FCPermissions.FullAccess)
            {
                ChatHelper.Info("Skipping gw for gil.");
                return;
            }

            if (!TryParseAmount(args, playerGil, fcGil, false, out uint amount, out string error))
            {
                ChatHelper.Error(error);
                return;
            }

            _executor.ExecuteWithdraw(amount);
        }

        private bool TryParseAmount(string input, uint playerGil, uint fcGil, bool isDeposit, out uint amount, out string error)
        {
            amount = 0;
            error = "";

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Amount is required.";
                return false;
            }

            input = input.Trim().ToLower();

            if (input == "all")
            {
                if (isDeposit)
                {
                    uint alwaysKeep = _configuration.GilAlwaysKeep;
                    if (playerGil <= alwaysKeep)
                    {
                        error = $"Your Gil ({playerGil:N0}) is at or below 'Always Keep' ({alwaysKeep:N0}).";
                        return false;
                    }
                    amount = playerGil - alwaysKeep;
                    uint fcRoom = GilValidator.MaxGil - fcGil;
                    amount = Math.Min(amount, fcRoom);
                }
                else
                {
                    amount = fcGil;
                    uint playerRoom = GilValidator.MaxGil - playerGil;
                    amount = Math.Min(amount, playerRoom);
                }
                return true;
            }

            var match = AmountPattern.Match(input);
            if (!match.Success)
            {
                error = "Invalid format. Use: 15000, 15k, 15.1k, 15m, or 'all'.";
                return false;
            }

            string numPart = match.Groups[1].Value.Replace(",", "");
            string suffix = match.Groups[2].Value.ToLower();

            if (!double.TryParse(numPart, out double baseValue))
            {
                error = "Could not parse numeric value.";
                return false;
            }

            double multiplier = suffix switch
            {
                "k" => 1_000,
                "m" => 1_000_000,
                _ => 1
            };

            double result = baseValue * multiplier;

            if (result != Math.Floor(result))
            {
                error = $"Result {result:N2} is not a whole number. For exact amounts like {(uint)Math.Floor(result):N0}, use the specific value.";
                return false;
            }

            if (result < 1 || result > GilValidator.MaxGil)
            {
                error = $"Amount must be between 1 and {GilValidator.MaxGil:N0}.";
                return false;
            }

            amount = (uint)result;
            return true;
        }

        public void Dispose()
        {
            try
            {
                if (_fireCallbackHook != null)
                {
                    if (_fireCallbackHook.IsEnabled) _fireCallbackHook.Disable();
                    _fireCallbackHook.Dispose();
                    _fireCallbackHook = null;
                }
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "[GilManager] Error disposing debug hook");
            }
            Plugin.AddonLifecycle.UnregisterListener(OnInputNumericSetup);
            Plugin.AddonLifecycle.UnregisterListener(OnBankSetup);
        }
    }
}
