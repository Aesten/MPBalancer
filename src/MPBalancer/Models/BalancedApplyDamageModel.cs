using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace MPBalancer.Models;

// Runs last in the damage pipeline (after armor, body part and perks), so these settings act on
// the damage the victim actually takes. See docs/mod-damage.md.
public class BalancedApplyDamageModel : MultiplayerAgentApplyDamageModel
{
    public override float ApplyGeneralDamageModifiers(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
    {
        float before = base.ApplyGeneralDamageModifiers(in attackInformation, in collisionData, baseDamage);
        DamageTypes damageType = Combat.EffectiveDamageType(in attackInformation, in collisionData);
        // What vanilla would have dealt: undo the swing speed setting (see VanillaEstimate).
        float vanilla = before * VanillaEstimate.ConsumeRatio(in attackInformation, in collisionData);
        float damage = before;

        // Shield hits are damage to the shield, not to the player.
        if (!collisionData.AttackBlockedWithShield && !attackInformation.IsVictimAgentNull)
        {
            if (attackInformation.IsAttackerAgentDoingPassiveAttack && !collisionData.IsMissile)
            {
                // Couched lances and braced polearms: damage is almost entirely speed, so they get their
                // own curve, applied to every victim (players and horses).
                damage = Combat.SoftCap(damage * BalanceSettings.Couch.Value, BalanceSettings.CouchKnee.Value, BalanceSettings.CouchCap.Value);
            }
            else if (damageType == DamageTypes.Pierce)
            {
                damage *= collisionData.IsMissile ? BalanceSettings.PierceMissile.Value : ThrustMultiplier(in attackInformation);
                if (attackInformation.IsVictimAgentHuman)
                {
                    damage = Combat.SoftCap(damage, BalanceSettings.PierceKnee.Value, BalanceSettings.PierceCap.Value);
                }
            }
        }

        HitDebug.Report(in attackInformation, in collisionData, damageType, vanilla, before, damage);
        return damage;
    }

    // Melee pierce thrusts: % of vanilla by relative speed (and, on players, by armor at the hit location),
    // times the weapon factors. Vanilla speed physics stays untouched, so weapons keep their vanilla relation
    // to each other at every speed (lances gain more from speed than spears).
    private static float ThrustMultiplier(in AttackInformation attackInformation)
    {
        float speed = Combat.RelativeSpeed(in attackInformation);
        float curve = attackInformation.IsVictimAgentMount
            ? BalanceSettings.ThrustCurveHorse.Evaluate(speed)
            : BalanceSettings.ThrustCurve.Evaluate(speed) * BalanceSettings.ThrustArmorCurve.Evaluate(attackInformation.ArmorAmountFloat);
        return curve * WeaponFactors.ThrustMultiplier(in attackInformation);
    }
}
