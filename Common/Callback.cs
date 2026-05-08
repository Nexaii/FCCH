using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace FCCH.Common
{
    public static unsafe class Callback
    {
        public const string Sig = "48 89 5C 24 ?? 48 89 6C 24 ?? 56 57 41 54 41 56 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? BF";
        public delegate byte AtkUnitBase_FireCallbackDelegate(AtkUnitBase* Base, int valueCount, AtkValue* values, byte updateState);
        internal static AtkUnitBase_FireCallbackDelegate? FireCallback = null;

        public static void Initialize()
        {
            if (FireCallback != null) return;
            try
            {
                var ptr = Plugin.SigScanner.ScanText(Sig);
                FireCallback = Marshal.GetDelegateForFunctionPointer<AtkUnitBase_FireCallbackDelegate>(ptr);
                Plugin.PluginLog.Info($"Initialized Callback module, FireCallback = 0x{ptr:X16}");
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, "Failed to initialize Callback module.");
            }
        }

        public static void FireRaw(AtkUnitBase* Base, int valueCount, AtkValue* values, byte updateState = 0)
        {
            if (FireCallback == null) Initialize();
            if (FireCallback == null) return;
            FireCallback(Base, valueCount, values, updateState);
        }

        public static void Fire(AtkUnitBase* Base, bool updateState, params object[] values)
        {
            if (Base == null) return;
            var atkValues = (AtkValue*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue));
            try
            {
                for (var i = 0; i < values.Length; i++)
                {
                    var v = values[i];
                    switch (v)
                    {
                        case uint uintValue:
                            atkValues[i].Type = ValueType.UInt;
                            atkValues[i].UInt = uintValue;
                            break;
                        case int intValue:
                            atkValues[i].Type = ValueType.Int;
                            atkValues[i].Int = intValue;
                            break;
                        case float floatValue:
                            atkValues[i].Type = ValueType.Float;
                            atkValues[i].Float = floatValue;
                            break;
                        case bool boolValue:
                            atkValues[i].Type = ValueType.Bool;
                            atkValues[i].Byte = (byte)(boolValue ? 1 : 0);
                            break;
                        case string stringValue:
                            {
                                atkValues[i].Type = ValueType.String;
                                var stringBytes = Encoding.UTF8.GetBytes(stringValue);
                                var stringAlloc = Marshal.AllocHGlobal(stringBytes.Length + 1);
                                Marshal.Copy(stringBytes, 0, stringAlloc, stringBytes.Length);
                                Marshal.WriteByte(stringAlloc, stringBytes.Length, 0);
                                atkValues[i].String = (byte*)stringAlloc;
                                break;
                            }
                        default:
                            throw new ArgumentException($"Unable to convert type {v.GetType()} to AtkValue");
                    }
                }
                FireRaw(Base, values.Length, atkValues, (byte)(updateState ? 1 : 0));
            }
            finally
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (atkValues[i].Type == ValueType.String)
                    {
                        Marshal.FreeHGlobal(new IntPtr(atkValues[i].String));
                    }
                }
                Marshal.FreeHGlobal(new IntPtr(atkValues));
            }
        }
    }
}
