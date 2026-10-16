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
        // What vanilla would have dealt: undo the swing speed setting and the thrust speed curves (see VanillaEstimate).
        float vanilla = before * VanillaEstimate.ConsumeRatio(in attackInformation, in collisionData, out float closing)
            * VanillaBodyPartRatio(in attackInformation, in collisionData) * ThrustDamageOverrides.VanillaRatio(in attackInformation, in collisionData);
        // Infantry perks' bonus against horses, with polearms only (mount_damage_polearm).
        float damage = MountDamagePerks.Adjust(in attackInformation, in collisionData, damageType, before);

        // Shield hits are damage to the shield, not to the player.
        if (!collisionData.AttackBlockedWithShield && !attackInformation.IsVictimAgentNull)
        {
            if (attackInformation.IsAttackerAgentDoingPassiveAttack && !collisionData.IsMissile)
            {
                // Couched lances and braced polearms: damage is almost entirely speed, so they get their
                // own curve, applied to every victim (players and horses).
                // cap x tanh(damage / cap): smooth from 0, never above the cap (couch_cap 1000 = off).
                damage *= BalanceSettings.Couch.Value;
                if (BalanceSettings.CouchCap.Value < 1000f)
                {
                    damage = Combat.SoftCap(damage, 0f, BalanceSettings.CouchCap.Value);
                }
            }
            else if (damageType == DamageTypes.Pierce && collisionData.IsMissile)
            {
                // Melee thrusts are shaped in the strike (BalancedStrikeMagnitudeModel); missiles here.
                damage *= BalanceSettings.PierceMissile.Value;
            }
        }

        HitDebug.Report(in attackInformation, in collisionData, damageType, vanilla, before, damage, closing);
        return damage;
    }

    // Vanilla's body part multiplier over ours for this hit (missile headshots), so the hit log's vanilla column is
    // vanilla's: the multiplier scales the damage proportionally (MissionCombatMechanicsHelper.ComputeBlowDamage).
    private float VanillaBodyPartRatio(in AttackInformation attackInformation, in AttackCollisionData collisionData)
    {
        if (!collisionData.IsMissile || collisionData.AttackBlockedWithShield)
        {
            return 1f;
        }
        BoneBodyPartType part = collisionData.VictimHitBodyPart;
        DamageTypes type = (DamageTypes)collisionData.DamageType;
        float ours = GetDamageMultiplierForBodyPart(part, type, attackInformation.IsVictimAgentHuman, isMissile: true);
        return ours > 0f ? base.GetDamageMultiplierForBodyPart(part, type, attackInformation.IsVictimAgentHuman, isMissile: true) / ours : 1f;
    }

    // Missile headshots (head and neck, pierce): missile_head on players (vanilla 2), missile_head_horse on horses
    // (vanilla 1.2, as for every pierce hit to a horse's head). Everything else is vanilla. The game reads this when it
    // builds the hit, before the damage (AttackInformation.DamageMultiplierOfBone).
    public override float GetDamageMultiplierForBodyPart(BoneBodyPartType bodyPart, DamageTypes type, bool isHuman, bool isMissile)
    {
        if (isMissile && type == DamageTypes.Pierce && (bodyPart == BoneBodyPartType.Head || bodyPart == BoneBodyPartType.Neck))
        {
            return isHuman ? BalanceSettings.MissileHead.Value : BalanceSettings.MissileHeadHorse.Value;
        }
        return base.GetDamageMultiplierForBodyPart(bodyPart, type, isHuman, isMissile);
    }
}
