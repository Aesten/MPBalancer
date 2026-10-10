using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Overrides engine parameters (Native/ModuleData/native_parameters.xml) on the server at runtime, through
// the engine's override_native_parameter call (MBAPI.IMBDebugExtensions, internal, so via reflection).
// A parameter is only touched once its setting has left the vanilla value, so with everything at vanilla
// the engine call is never made. Clients keep the values from their own files.
internal static class NativeParameters
{
    private static readonly Dictionary<string, float> _applied = new Dictionary<string, float>();

    private static object _debugExtensions;
    private static MethodInfo _override;
    private static bool _resolved;

    public static void Apply()
    {
        foreach (ISetting setting in BalanceSettings.All)
        {
            if (!(setting is BalanceSetting s) || s.NativeParameter == null)
            {
                continue;
            }
            bool wasApplied = _applied.TryGetValue(s.NativeParameter, out float applied);
            if (wasApplied ? applied == s.Value : s.Value == s.Vanilla)
            {
                continue;
            }
            if (!TryOverride(s.NativeParameter, s.Value))
            {
                return;
            }
            _applied[s.NativeParameter] = s.Value;
            MPBalancerSubModule.Log("Native parameter " + s.NativeParameter + " = " + s.DisplayValue);
        }
    }

    private static bool TryOverride(string name, float value)
    {
        if (!_resolved)
        {
            _resolved = true;
            Assembly assembly = typeof(Agent).Assembly;
            _debugExtensions = assembly.GetType("TaleWorlds.MountAndBlade.MBAPI")
                ?.GetField("IMBDebugExtensions", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(null);
            _override = assembly.GetType("TaleWorlds.MountAndBlade.IMBDebugExtensions")?.GetMethod("OverrideNativeParameter");
        }
        if (_debugExtensions == null || _override == null)
        {
            MPBalancerSubModule.Log("Native parameter override unavailable; " + name + " not changed.");
            return false;
        }
        try
        {
            _override.Invoke(_debugExtensions, new object[] { name, value });
            return true;
        }
        catch (Exception e)
        {
            MPBalancerSubModule.Log("Native parameter override failed for " + name + ": " + (e.InnerException ?? e).Message);
            return false;
        }
    }
}
