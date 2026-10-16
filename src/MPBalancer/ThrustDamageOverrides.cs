using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace MPBalancer;

// thrust_damage: sets a weapon's listed thrust damage on the server, e.g. mp_battania_long_spear:30 (29 vanilla).
// A thrust's damage is its strike times the usage's ThrustDamageFactor (MissionCombatMechanicsHelper
// .ConvertBaseAttackMagnitude), and the listed damage is the standing strike times that factor, so scaling the factor
// scales the damage at every speed, as if the weapon were made that way. The value is the item's first thrusting
// usage's damage (the one-handed one for cavalry spears); its other thrusting usages get the same ratio (two-handed 34 -> 35).
// Couched and braced usages (passive attacks, which also take their damage from the usage's thrust factor) get it
// too, before the couch curve.
//
// Applies to the next hit. Clients still list the vanilla number in the loadout. Items taken out of the table get
// their original values back.
internal static class ThrustDamageOverrides
{
    private static readonly MethodInfo SetFactor = typeof(WeaponComponentData).GetProperty("ThrustDamageFactor")?.GetSetMethod(nonPublic: true);
    private static readonly MethodInfo SetDamage = typeof(WeaponComponentData).GetProperty("ThrustDamage")?.GetSetMethod(nonPublic: true);
    private static readonly Dictionary<WeaponComponentData, (float Factor, int Damage)> Original = new Dictionary<WeaponComponentData, (float, int)>();

    public static void Apply()
    {
        if (SetFactor == null || SetDamage == null || MBObjectManager.Instance == null)
        {
            return;
        }
        var wanted = new Dictionary<WeaponComponentData, float>(); // usage -> ratio to its original
        var listed = new Dictionary<ItemObject, float>();
        foreach (KeyValuePair<string, float> entry in BalanceSettings.ThrustDamage.Entries)
        {
            ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(entry.Key);
            WeaponComponentData main = null;
            if (item?.Weapons != null)
            {
                foreach (WeaponComponentData usage in item.Weapons)
                {
                    if (OriginalDamage(usage) > 0)
                    {
                        main = usage;
                        break;
                    }
                }
            }
            if (main == null)
            {
                MPBalancerSubModule.Log("thrust_damage: no thrusting item " + entry.Key + ".");
                continue;
            }
            float ratio = entry.Value / OriginalDamage(main);
            foreach (WeaponComponentData usage in item.Weapons)
            {
                if (OriginalDamage(usage) > 0)
                {
                    wanted[usage] = ratio;
                }
            }
            listed[item] = entry.Value;
        }
        // Restore usages no longer listed.
        foreach (WeaponComponentData usage in new List<WeaponComponentData>(Original.Keys))
        {
            if (!wanted.ContainsKey(usage))
            {
                Set(usage, Original[usage].Factor, Original[usage].Damage);
                Original.Remove(usage);
            }
        }
        foreach (KeyValuePair<WeaponComponentData, float> entry in wanted)
        {
            WeaponComponentData usage = entry.Key;
            if (!Original.ContainsKey(usage))
            {
                Original[usage] = (usage.ThrustDamageFactor, usage.ThrustDamage);
            }
            (float factor, int damage) = Original[usage];
            int newDamage = (int)System.Math.Round(damage * entry.Value);
            if (usage.ThrustDamage != newDamage || usage.ThrustDamageFactor != factor * entry.Value)
            {
                Set(usage, factor * entry.Value, newDamage);
            }
        }
        foreach (KeyValuePair<ItemObject, float> entry in listed)
        {
            if (!_logged.TryGetValue(entry.Key, out float last) || last != entry.Value)
            {
                _logged[entry.Key] = entry.Value;
                MPBalancerSubModule.Log("thrust_damage: " + entry.Key.StringId + " thrust " + BalanceSetting.Format(entry.Value)
                    + " (factor x" + BalanceSetting.Format(MathRound(entry.Value / OriginalDamage(FirstThrust(entry.Key)), 3)) + ").");
            }
        }
    }

    // Original over current thrust factor for a melee thrust, couch or brace with a changed weapon, else 1: what the
    // hit would have dealt with the vanilla weapon, for the hit log's vanilla column (the factor scales the damage proportionally).
    public static float VanillaRatio(in TaleWorlds.MountAndBlade.AttackInformation attackInformation, in TaleWorlds.MountAndBlade.AttackCollisionData collisionData)
    {
        if (Original.Count == 0 || collisionData.IsMissile || collisionData.StrikeType != (int)StrikeType.Thrust)
        {
            return 1f;
        }
        WeaponComponentData usage = attackInformation.AttackerWeapon.CurrentUsageItem;
        return usage != null && Original.TryGetValue(usage, out var original) && usage.ThrustDamageFactor > 0f
            ? original.Factor / usage.ThrustDamageFactor : 1f;
    }

    private static readonly Dictionary<ItemObject, float> _logged = new Dictionary<ItemObject, float>();

    // The usage's vanilla thrust damage (pierce thrusts only), before any change here.
    private static int OriginalDamage(WeaponComponentData usage)
    {
        if (usage == null || usage.ThrustDamageType != DamageTypes.Pierce)
        {
            return 0;
        }
        return Original.TryGetValue(usage, out var original) ? original.Damage : usage.ThrustDamage;
    }

    private static WeaponComponentData FirstThrust(ItemObject item)
    {
        foreach (WeaponComponentData usage in item.Weapons)
        {
            if (OriginalDamage(usage) > 0)
            {
                return usage;
            }
        }
        return null;
    }

    private static float MathRound(float value, int digits) => (float)System.Math.Round(value, digits);

    private static void Set(WeaponComponentData usage, float factor, int damage)
    {
        SetFactor.Invoke(usage, new object[] { factor });
        SetDamage.Invoke(usage, new object[] { damage });
    }
}
