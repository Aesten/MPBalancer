using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Estimates what a hit would have done in vanilla, for the hit log and readout. Swings (the swing setting) and melee
// pierce thrusts (the speed curves) change the strike magnitude: the strike model records, per hit, our magnitude and
// the vanilla one; everything after the magnitude (armor, body part, perks) is proportional to it in multiplayer, so
// the ratio carries through to the final damage. Thrusts also record the closing speed the curve used. Hits are identified by attacker, victim and collision position,
// because the game calls the strike model several times per hit (swings try 5 impact points,
// and the speed bonus display re-runs it with zero speed).
internal static class VanillaEstimate
{
    private static int _attacker = -1;
    private static int _victim = -1;
    private static Vec3 _position;
    private static float _ours;
    private static float _vanilla;
    private static float _closing = -1f;

    // firstOnly: keep the first record of the hit (thrusts: the real call comes before the speed bonus display's).
    public static void Record(in AttackInformation attackInformation, in AttackCollisionData collisionData, float ours, float vanilla,
        float closing = -1f, bool firstOnly = false)
    {
        bool current = IsCurrent(in attackInformation, in collisionData);
        if (current && firstOnly)
        {
            return;
        }
        if (!current)
        {
            _attacker = attackInformation.AttackerAgent?.Index ?? -1;
            _victim = attackInformation.VictimAgent?.Index ?? -1;
            _position = collisionData.CollisionGlobalPosition;
            _ours = 0f;
            _vanilla = 0f;
            _closing = closing;
        }
        // Swings use the best of several impact points, so keep the maximum of each.
        _ours = MathF.Max(_ours, ours);
        _vanilla = MathF.Max(_vanilla, vanilla);
    }

    // Vanilla damage / our damage for this hit (1 if nothing was recorded), and the closing speed a thrust's curve used
    // (-1 if none), then forgets the hit.
    public static float ConsumeRatio(in AttackInformation attackInformation, in AttackCollisionData collisionData, out float closing)
    {
        float ratio = 1f;
        closing = -1f;
        if (IsCurrent(in attackInformation, in collisionData))
        {
            closing = _closing;
            if (_ours > 0f)
            {
                ratio = _vanilla / _ours;
            }
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
