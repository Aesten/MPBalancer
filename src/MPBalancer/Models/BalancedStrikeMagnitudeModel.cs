using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer.Models;

// Swings: scales the speed bonus (the swing setting). Melee pierce thrusts: a percentage of vanilla's strength.
//
// Vanilla's thrust strength is 0.0625 x (weapon weight + 2.5) x (thrust speed + closing speed / 2)^2 x the weapon's
// thrust damage factor (the weapon's listed thrust damage is exactly that with no closing speed, Crafting
// .CalculateThrustBaseDamage); speed bonus perks (Lancer, Line Breaker...) lengthen the closing speed term. The squared
// speed term, multiplied by the weapon's weight, made fast hits from heavy lances run away. Here the strength is
// vanilla's for the same hit (perks included) times the speed curve (thrust_curve_lance for weapons that can couch,
// thrust_curve_spear otherwise) at the closing speed along the thrust (vanilla's: the relative speed projected on the
// thrust, +0.2 alignment). The curves are 100% at low speed and fall with speed, so slow hits are vanilla's and fast
// ones gain less; vanilla's order between weapons (weight, thrust speed, perks) stays. Armor and body part then apply
// as in vanilla; in multiplayer they're proportional to this.
//
// Subclasses the Multiplayer model (not the Default one) so perk effects still apply. The combat log's "+X% speed
// bonus" re-runs this with extraLinearSpeed = 0, so it shows the bonus over standing still.
public class BalancedStrikeMagnitudeModel : MultiplayerStrikeMagnitudeModel
{
    public override float CalculateStrikeMagnitudeForSwing(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float swingSpeed, float impactPoint, float extraLinearSpeed)
    {
        float ours = base.CalculateStrikeMagnitudeForSwing(in attackInformation, in collisionData, in weapon, swingSpeed, impactPoint, extraLinearSpeed * BalanceSettings.SwingSpeedBonus.Value);
        if (extraLinearSpeed != 0f)
        {
            // Also compute the vanilla magnitude, so the hit log can show what vanilla would have done.
            float vanilla = base.CalculateStrikeMagnitudeForSwing(in attackInformation, in collisionData, in weapon, swingSpeed, impactPoint, extraLinearSpeed);
            VanillaEstimate.Record(in attackInformation, in collisionData, ours, vanilla);
        }
        return ours;
    }

    public override float CalculateStrikeMagnitudeForThrust(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float thrustWeaponSpeed, float extraLinearSpeed, bool isThrown = false)
    {
        float vanilla = base.CalculateStrikeMagnitudeForThrust(in attackInformation, in collisionData, in weapon, thrustWeaponSpeed, extraLinearSpeed, isThrown);
        WeaponComponentData usage = weapon.CurrentUsageItem;
        if (BalanceSettings.ThrustModel.Value < 0.5f || isThrown || collisionData.IsMissile || weapon.IsEmpty || usage == null
            || usage.ThrustDamageType != DamageTypes.Pierce || attackInformation.IsVictimAgentNull)
        {
            return vanilla;
        }
        BalanceCurve curve = Combat.IsLance(weapon.Item) ? BalanceSettings.ThrustCurveLance : BalanceSettings.ThrustCurveSpear;
        // The game passes half the closing speed for thrusts (CalculateBaseMeleeBlowMagnitude).
        float closing = MathF.Max(0f, 2f * extraLinearSpeed);
        // Never more than vanilla would deal for this hit (curves above 100% and weapon_mult above 1 are capped).
        float ours = MathF.Min(vanilla * curve.Evaluate(closing) * BalanceSettings.WeaponMultipliers.Get(weapon.Item?.StringId), vanilla);
        // The first call per hit is the real one (the speed bonus display comes after, with no speed).
        VanillaEstimate.Record(in attackInformation, in collisionData, ours, vanilla, closing, firstOnly: true);
        return ours;
    }
}
