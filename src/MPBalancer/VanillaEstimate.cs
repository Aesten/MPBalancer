using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Estimates what a hit would have done with vanilla speed settings, for the hit log and readout.
// Only swings change the magnitude (the swing setting); everything else is changed on the final damage.
// The strike model records, per hit, the magnitude with our speed factor and with the vanilla one;
// everything after the magnitude (armor, body part, perks) is linear in it, so the ratio carries
// through to the final damage. Hits are identified by attacker, victim and collision position,
// because the game calls the strike model several times per hit (swings try 5 impact points,
// and the speed bonus display re-runs it with zero speed).
internal static class VanillaEstimate
{
    private static int _attacker = -1;
    private static int _victim = -1;
    private static Vec3 _position;
    private static float _ours;
    private static float _vanilla;

    public static void Record(in AttackInformation attackInformation, in AttackCollisionData collisionData, float ours, float vanilla)
    {
        if (!IsCurrent(in attackInformation, in collisionData))
        {
            _attacker = attackInformation.AttackerAgent?.Index ?? -1;
            _victim = attackInformation.VictimAgent?.Index ?? -1;
            _position = collisionData.CollisionGlobalPosition;
            _ours = 0f;
            _vanilla = 0f;
        }
        // Swings use the best of several impact points, so keep the maximum of each.
        _ours = MathF.Max(_ours, ours);
        _vanilla = MathF.Max(_vanilla, vanilla);
    }

    // Vanilla damage / our damage for this hit (1 if nothing was recorded), then forgets the hit.
    public static float ConsumeRatio(in AttackInformation attackInformation, in AttackCollisionData collisionData)
    {
        float ratio = 1f;
        if (IsCurrent(in attackInformation, in collisionData) && _ours > 0f)
        {
            ratio = _vanilla / _ours;
        }
        _attacker = -1;
        _victim = -1;
        return ratio;
    }

    private static bool IsCurrent(in AttackInformation attackInformation, in AttackCollisionData collisionData)
    {
        return _attacker == (attackInformation.AttackerAgent?.Index ?? -1)
            && _victim == (attackInformation.VictimAgent?.Index ?? -1)
            && _position.x == collisionData.CollisionGlobalPosition.x
            && _position.y == collisionData.CollisionGlobalPosition.y
            && _position.z == collisionData.CollisionGlobalPosition.z;
    }
}
