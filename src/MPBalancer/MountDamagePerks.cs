using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// mount_damage_polearm: infantry perks that add damage against horses (MountDamage effects: Cavalry Breaker +100%,
// Spearman and Farmer +50-75%) come with a spear or pike, but the game applies them to any weapon (Cavalry Breaker:
// any melee weapon; Spearman and Farmer: any attack, thrown and ranged included), since their effects have no weapon
// class filter (and the game's filter takes one class, while a spear is a one- or two-handed polearm by grip). With
// this on, an infantry class's mount bonus only counts with a polearm in hand. Ranged classes' perks (Hunter, Big Game
// Hunter, Poacher), which come with their bolts, arrows or throwing spears, are untouched.
//
// The game multiplies the damage by 1 + (mount bonus + other damage bonuses) + damage taken bonuses
// (MultiplayerAgentApplyDamageModel.ApplyGeneralDamageModifiers); this takes the mount bonus back out of that sum.
internal static class MountDamagePerks
{
    private static readonly MethodInfo PerkMountDamage = typeof(MPPerkObject).GetMethod("GetMountDamage",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(Agent), typeof(WeaponComponentData), typeof(DamageTypes), typeof(bool) }, null);

    // The damage with the attacker's mount bonus removed, or the damage unchanged when it applies.
    public static float Adjust(in AttackInformation attackInformation, in AttackCollisionData collisionData, DamageTypes damageType, float damage)
    {
        if (BalanceSettings.MountDamagePolearm.Value < 0.5f || PerkMountDamage == null || !attackInformation.IsVictimAgentMount
            || collisionData.AttackBlockedWithShield)
        {
            return damage;
        }
        Agent attacker = attackInformation.AttackerAgent;
        if (attacker == null || attacker.IsMount || !IsInfantry(attacker))
        {
            return damage;
        }
        WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
        if (IsPolearm(weapon))
        {
            return damage;
        }
        MPPerkObject.MPCombatPerkHandler handler = MPPerkObject.GetCombatPerkHandler(attacker, attackInformation.VictimAgent);
        var perks = attacker.MissionPeer?.SelectedPerks ?? attacker.OwningAgentMissionPeer?.SelectedPerks;
        if (handler == null || perks == null)
        {
            return damage;
        }
        float mount = 0f;
        foreach (MPPerkObject perk in perks)
        {
            mount += (float)PerkMountDamage.Invoke(perk, new object[] { handler.IsWarmup, attacker, weapon, damageType, collisionData.IsAlternativeAttack });
        }
        if (mount <= 0f)
        {
            return damage;
        }
        // The game's multiplier, as it computed it (the mount bonus is inside GetDamage when the victim is a horse).
        float multiplier = MathF.Max(0f, 1f + handler.GetDamage(weapon, damageType, collisionData.IsAlternativeAttack) + handler.GetDamageTaken(weapon, damageType));
        if (attackInformation.IsHeadShot && weapon != null && (weapon.IsConsumable || weapon.IsRangedWeapon))
        {
            multiplier += handler.GetRangedHeadShotDamage();
        }
        if (multiplier <= 0f)
        {
            return damage;
        }
        return damage * MathF.Max(0f, multiplier - mount) / multiplier;
    }

    private static bool IsPolearm(WeaponComponentData weapon)
    {
        return weapon != null && (weapon.WeaponClass == WeaponClass.OneHandedPolearm || weapon.WeaponClass == WeaponClass.TwoHandedPolearm
            || weapon.WeaponClass == WeaponClass.LowGripPolearm);
    }

    private static bool IsInfantry(Agent agent)
    {
        MultiplayerClassDivisions.MPHeroClass heroClass = MultiplayerClassDivisions.GetMPHeroClassForCharacter(agent.Character);
        return heroClass?.StringId != null && heroClass.StringId.Contains("infantry");
    }
}
