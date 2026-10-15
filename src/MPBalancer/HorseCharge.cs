using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace MPBalancer;

// Horse charges ("bumps").
// - Whether a bump happens at all is decided by the engine, using the horse Monster's
//   relative_speed_limit_for_charge (monsters.xml: 4.3 m/s horses, 5.0 camels). The value is copied into
//   each agent when it spawns, so bump_speed changes the Monster objects and applies to the next spawns.
//   The engine also only bumps while the horse is in a running gait, whatever the limit (lowest seen: 5.9 m/s).
internal static class HorseCharge
{
    private const float VanillaHorseLimit = 4.3f;

    private static readonly Dictionary<Monster, float> _originalLimits = new Dictionary<Monster, float>();

    private static readonly MethodInfo _setLimit = typeof(Monster).GetProperty(nameof(Monster.RelativeSpeedLimitForCharge))?.GetSetMethod(true);

    // Scales every mountable Monster's limit so the horse one equals bump_speed (camels keep their ratio).
    public static void ApplySpeedLimit()
    {
        var monsters = MBObjectManager.Instance?.GetObjectTypeList<Monster>();
        if (monsters == null || _setLimit == null)
        {
            return;
        }
        float scale = BalanceSettings.BumpSpeed.Value / VanillaHorseLimit;
        foreach (Monster monster in monsters)
        {
            if (!_originalLimits.TryGetValue(monster, out float original))
            {
                original = monster.RelativeSpeedLimitForCharge;
                _originalLimits[monster] = original;
            }
            if (original < 1000f)
            {
                _setLimit.Invoke(monster, new object[] { original * scale });
            }
        }
    }
}
