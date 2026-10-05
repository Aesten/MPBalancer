using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace MPBalancer.Models;

// Scales the speed bonus of swings. Thrusts, couches and missiles keep vanilla physics here; they are
// shaped on the final damage instead (BalancedApplyDamageModel).
// Subclasses the Multiplayer model (not the Default one) so perk speed-bonus effects still apply.
// The combat log's "+X% speed bonus" re-runs this with extraLinearSpeed = 0, which scaling leaves at 0,
// so the displayed bonus stays accurate.
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
}
