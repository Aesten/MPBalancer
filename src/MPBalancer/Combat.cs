using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Small helpers shared by the models and the hit log.
internal static class Combat
{
    // Unchanged below the knee; above it, approaches the cap smoothly (slope 1 at the knee) and never exceeds it.
    public static float SoftCap(float value, float knee, float cap)
    {
        if (value <= knee)
        {
            return value;
        }
        if (cap <= knee)
        {
            return MathF.Min(value, cap);
        }
        float range = cap - knee;
        return knee + range * MathF.Tanh((value - knee) / range);
    }

    // Same rule the game uses: arm/hilt hits, kicks/bashes, falls and horse charges count as blunt.
    public static DamageTypes EffectiveDamageType(in AttackInformation attackInformation, in AttackCollisionData collisionData)
    {
        bool hitWithAnotherBone = MissionCombatMechanicsHelper.IsCollisionBoneDifferentThanWeaponAttachBone(in collisionData, attackInformation.WeaponAttachBoneIndex);
        if (attackInformation.AttackerWeapon.IsEmpty || hitWithAnotherBone || collisionData.IsAlternativeAttack || collisionData.IsFallDamage || collisionData.IsHorseCharge)
        {
            return DamageTypes.Blunt;
        }
        return (DamageTypes)collisionData.DamageType;
    }

    // Magnitude of the attacker/victim relative velocity, as MissionCombatMechanicsHelper computes it
    // (before projecting onto the blow direction). For missiles this is the shooter's movement.
    public static float RelativeSpeed(in AttackInformation a) => (AttackerVelocity(in a) - VictimVelocity(in a)).Length;

    public static float AttackerSpeed(in AttackInformation a) => AttackerVelocity(in a).Length;

    public static float VictimSpeed(in AttackInformation a) => VictimVelocity(in a).Length;

    // The horse an agent rides, or the horse itself; "none" on foot.
    public static string HorseId(Agent agent)
    {
        Agent horse = agent == null ? null : agent.IsMount ? agent : agent.MountAgent;
        return horse?.SpawnEquipment?[EquipmentIndex.Horse].Item?.StringId ?? "none";
    }

    public static Vec2 AttackerVelocity(in AttackInformation a) =>
        Velocity(a.DoesAttackerHaveMountAgent, a.AttackerAgentMovementVelocity, a.AttackerAgentMountMovementDirection, a.AttackerMovementDirectionAsAngle);

    public static Vec2 VictimVelocity(in AttackInformation a) =>
        Velocity(a.DoesVictimHaveMountAgent, a.VictimAgentMovementVelocity, a.VictimAgentMountMovementDirection, a.VictimMovementDirectionAsAngle);

    // Same as MissionCombatMechanicsHelper.GetAgentVelocityContribution (private there).
    private static Vec2 Velocity(bool hasMount, Vec2 movementVelocity, Vec2 mountMovementDirection, float movementDirectionAsAngle)
    {
        if (hasMount)
        {
            return movementVelocity.y * mountMovementDirection;
        }
        Vec2 velocity = movementVelocity;
        velocity.RotateCCW(movementDirectionAsAngle);
        return velocity;
    }
}
