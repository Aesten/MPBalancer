using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Per-weapon factors on melee pierce thrusts:
// - thrust_spread: (weapon's listed thrust damage / reference) ^ thrust_spread. Weapons listed above the
//   reference hit harder, weapons below it softer. The listed thrust damage is the number players see in the
//   loadout, so "higher listed damage = more impact" stays readable.
// - lance: extra multiplier for lances (items with a couched usage).
// - weapon_mult: extra multiplier per item id.
internal static class WeaponFactors
{
    private static Dictionary<(WeaponClass, bool), float> _autoReferences;

    public static float ThrustMultiplier(in AttackInformation attackInformation)
    {
        MissionWeapon weapon = attackInformation.AttackerWeapon;
        if (weapon.IsEmpty)
        {
            return 1f;
        }
        bool lance = IsLance(weapon.Item);
        float multiplier = BalanceSettings.WeaponMultipliers.Get(weapon.Item?.StringId);
        if (lance)
        {
            multiplier *= BalanceSettings.LanceThrust.Value;
        }
        float exponent = BalanceSettings.ThrustSpread.Value;
        int stat = ThrustStat(in attackInformation);
        float reference = Reference(weapon.CurrentUsageItem.WeaponClass, lance);
        if (exponent != 0f && stat > 0 && reference > 0f)
        {
            multiplier *= MathF.Pow(stat / reference, exponent);
        }
        return multiplier;
    }

    public static int ThrustStat(in AttackInformation attackInformation)
    {
        return attackInformation.AttackerWeapon.IsEmpty ? 0 : attackInformation.AttackerWeapon.GetModifiedThrustDamageForCurrentUsage();
    }

    // Lances are compared with lances and everything else with non-lances: lances list lower thrust damage
    // than spears because their strength is speed, not the thrust itself.
    public static bool IsLance(ItemObject item)
    {
        if (item?.Weapons == null)
        {
            return false;
        }
        foreach (WeaponComponentData weapon in item.Weapons)
        {
            if (weapon.ItemUsage != null && weapon.ItemUsage.Contains("couch"))
            {
                return true;
            }
        }
        return false;
    }

    // thrust_ref, or (when it is 0) the median listed thrust damage of pierce-thrusting weapons of the same
    // usage class and lance/non-lance group (one-handed lance usages with one-handed lance usages, and so on).
    public static float Reference(WeaponClass weaponClass, bool lance)
    {
        if (BalanceSettings.ThrustReference.Value > 0f)
        {
            return BalanceSettings.ThrustReference.Value;
        }
        EnsureAutoReferences();
        return _autoReferences.TryGetValue((weaponClass, lance), out float reference) ? reference : 0f;
    }

    public static void EnsureAutoReferences()
    {
        if (_autoReferences != null)
        {
            return;
        }
        var stats = new Dictionary<(WeaponClass, bool), List<int>>();
        foreach (ItemObject item in Game.Current.ObjectManager.GetObjectTypeList<ItemObject>())
        {
            if (item.Weapons == null)
            {
                continue;
            }
            bool lance = IsLance(item);
            foreach (WeaponComponentData weapon in item.Weapons)
            {
                if (weapon.IsMeleeWeapon && weapon.ThrustDamageType == DamageTypes.Pierce && weapon.ThrustDamage > 0)
                {
                    if (!stats.TryGetValue((weapon.WeaponClass, lance), out List<int> list))
                    {
                        stats[(weapon.WeaponClass, lance)] = list = new List<int>();
                    }
                    list.Add(weapon.ThrustDamage);
                }
            }
        }
        _autoReferences = new Dictionary<(WeaponClass, bool), float>();
        foreach (KeyValuePair<(WeaponClass, bool), List<int>> entry in stats)
        {
            entry.Value.Sort();
            float median = entry.Value[entry.Value.Count / 2];
            _autoReferences[entry.Key] = median;
            MPBalancerSubModule.Log("thrust_ref auto " + entry.Key.Item1 + (entry.Key.Item2 ? " (lances)" : "") + " = " + median
                + " (median of " + entry.Value.Count + " usages, range " + entry.Value[0] + "-" + entry.Value[entry.Value.Count - 1] + ").");
        }
    }
}
