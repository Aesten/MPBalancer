using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace MPBalancer;

// Horse charges ("bumps").
// - Whether a bump happens at all is decided by the engine, using the horse Monster's
//   relative_speed_limit_for_charge (monsters.xml: 4.3 m/s horses, 5.0 camels). The value is copied into
//   each agent when it spawns, so bump_speed changes the Monster objects and applies to the next spawns.
//   The engine also only bumps while the horse is in a running gait, whatever the limit (lowest seen: 5.9 m/s).
// - The engine's bump speed (ChargeVelocity) equals the closing speed computed here; both are logged.
internal static class HorseCharge
{
    private const float VanillaHorseLimit = 4.3f;

    private static readonly Dictionary<Monster, float> _originalLimits = new Dictionary<Monster, float>();

    private static readonly MethodInfo _setLimit = typeof(Monster).GetProperty(nameof(Monster.RelativeSpeedLimitForCharge))?.GetSetMethod(true);

    // Closing speed of the last bump, from the damage model, for the report that follows it.
    private static Agent _lastAttacker;
    private static Agent _lastVictim;
    private static float _lastSpeed;

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

    // Same closing speed the game uses for bump damage: the charger's velocity minus the victim's
    // velocity along the charger's direction.
    public static float ClosingSpeed(in AttackInformation a)
    {
        Vec2 direction = a.AttackerAgentMovementDirection;
        Vec2 victimAlong = direction * Vec2.DotProduct(Combat.VictimVelocity(in a), direction);
        return (Combat.AttackerVelocity(in a) - victimAlong).Length;
    }

    public static void Record(in AttackInformation a)
    {
        _lastAttacker = a.AttackerAgent;
        _lastVictim = a.VictimAgent;
        _lastSpeed = ClosingSpeed(in a);
    }

    // Called once per damaging bump, after both knock decisions.
    public static void Report(Agent attacker, Agent victim, in AttackCollisionData collisionData, bool knockedBack, bool knockedDown)
    {
        float speed = attacker == _lastAttacker && victim == _lastVictim ? _lastSpeed : -1f;
        string effect = knockedDown ? "knocked down" : knockedBack ? "knocked back" : "no knock";
        MPBalancerSubModule.Log("[MPB-BUMP] speed=" + F(speed)
            + " engine=" + F(collisionData.ChargeVelocity)
            + " damage=" + collisionData.InflictedDamage
            + " victim=" + (victim.IsHuman ? "human" : "horse")
            + " vmounted=" + (victim.HasMount ? 1 : 0)
            + " knockback=" + (knockedBack ? 1 : 0)
            + " knockdown=" + (knockedDown ? 1 : 0)
            + " ahorse=" + Combat.HorseId(attacker)
            + " bump_speed=" + F(BalanceSettings.BumpSpeed.Value));
        HitDebug.Notify(attacker, victim, "bump " + F(speed) + " m/s | " + effect);
    }

    private static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
