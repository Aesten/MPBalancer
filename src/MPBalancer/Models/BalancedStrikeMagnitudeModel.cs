using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer.Models;

// Melee pierce thrusts: a percentage of vanilla's strength (swings are vanilla).
//
// Vanilla's thrust strength is 0.0625 x (weapon weight + 2.5) x (thrust speed + closing speed / 2)^2 x the weapon's
// thrust damage factor (the weapon's listed thrust damage is exactly that with no closing speed, Crafting
// .CalculateThrustBaseDamage); speed bonus perks (Lancer, Line Breaker...) lengthen the closing speed term. The squared
// speed term, multiplied by the weapon's weight, made fast hits from heavy lances run away. Here the strength is
// vanilla's for the same hit (perks included) times the speed curve (on foot thrust_curve_foot; mounted
// thrust_curve_lance for weapons that can couch, thrust_curve_spear otherwise) at the closing speed along the thrust (vanilla's: the relative speed projected on the
// thrust, +0.2 alignment). The curves are 100% at low speed and fall with speed, so slow hits are vanilla's and fast
// ones gain less; vanilla's order between weapons (weight, thrust speed, perks) stays. Armor and body part then apply
// as in vanilla; in multiplayer they're proportional to this.
//
// Subclasses the Multiplayer model (not the Default one) so perk effects still apply. The combat log's "+X% speed
// bonus" re-runs this with extraLinearSpeed = 0, so it shows the bonus over standing still.
public class BalancedStrikeMagnitudeModel : MultiplayerStrikeMagnitudeModel
{
    public override float CalculateStrikeMagnitudeForThrust(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float thrustWeaponSpeed, float extraLinearSpeed, bool isThrown = false)
    {
        float vanilla = base.CalculateStrikeMagnitudeForThrust(in attackInformation, in collisionData, in weapon, thrustWeaponSpeed, extraLinearSpeed, isThrown);
        WeaponComponentData usage = weapon.CurrentUsageItem;
        if (BalanceSettings.ThrustModel.Value < 0.5f || isThrown || collisionData.IsMissile || weapon.IsEmpty || usage == null
            || usage.ThrustDamageType != DamageTypes.Pierce || attackInformation.IsVictimAgentNull)
        {
            return vanilla;
        }
        // On foot (infantry, archers in melee, dismounted cavalry): thrust_curve_foot. Mounted: by weapon.
        BalanceCurve curve = !attackInformation.DoesAttackerHaveMountAgent ? BalanceSettings.ThrustCurveFoot
            : Combat.IsLance(weapon.Item) ? BalanceSettings.ThrustCurveLance : BalanceSettings.ThrustCurveSpear;
        // The game passes half the closing speed for thrusts (CalculateBaseMeleeBlowMagnitude).
        float closing = MathF.Max(0f, 2f * extraLinearSpeed);
        // Never more than vanilla would deal for this hit (curves above 100% and weapon_mult above 1 are capped).
        // Thrusts on horses get thrust_horse on top (the curves were set on players; horses had their own, higher curve).
        float horse = attackInformation.IsVictimAgentMount ? BalanceSettings.ThrustHorse.Value : 1f;
        float ours = MathF.Min(vanilla * curve.Evaluate(closing) * BalanceSettings.WeaponMultipliers.Get(weapon.Item?.StringId) * horse, vanilla);
        // The first call per hit is the real one (the speed bonus display comes after, with no speed).
        VanillaEstimate.Record(in attackInformation, in collisionData, ours, vanilla, closing, firstOnly: true);
        return ours;
    }
}
