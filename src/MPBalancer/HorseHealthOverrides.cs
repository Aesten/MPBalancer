using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace MPBalancer;

// horse_hp: a horse's total health. Health is the mount type's base (200 for horses and camels) plus the horse item's
// extra health (HorseComponent.HitPointBonus, private setter), read when the horse spawns (Mission.SpawnMount); this
// sets the extra health so the total comes out as asked.
//
// Horses spawned after the change get it. Clients work out their own horse's maximum health from the vanilla item, so
// its health bar can show more than full until the horse is below its vanilla maximum; damage and death are the
// server's. Horses taken out of the table get their original extra health back.
internal static class HorseHealthOverrides
{
    private static readonly MethodInfo SetBonus = typeof(HorseComponent).GetProperty("HitPointBonus")?.GetSetMethod(nonPublic: true);
    // Original extra health per item id. Only ids and values are kept: a HorseComponent's Item link isn't reliable,
    // and on the Linux server a null access crashes the process (it did, from a log line reading horse.Item).
    private static readonly Dictionary<string, (HorseComponent Horse, int Bonus)> Original = new Dictionary<string, (HorseComponent, int)>();

    public static void Apply()
    {
        if (SetBonus == null || MBObjectManager.Instance == null)
        {
            return;
        }
        var wanted = new Dictionary<string, (HorseComponent Horse, int Health)>();
        foreach (KeyValuePair<string, float> entry in BalanceSettings.HorseHealth.Entries)
        {
            ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(entry.Key);
            HorseComponent horse = item?.HorseComponent;
            if (horse == null || horse.Monster == null)
            {
                MPBalancerSubModule.Log("horse_hp: no horse " + entry.Key + ".");
                continue;
            }
            wanted[entry.Key] = (horse, (int)entry.Value);
        }
        foreach (string id in new List<string>(Original.Keys))
        {
            if (!wanted.ContainsKey(id))
            {
                SetBonus.Invoke(Original[id].Horse, new object[] { Original[id].Bonus });
                Original.Remove(id);
            }
        }
        foreach (KeyValuePair<string, (HorseComponent Horse, int Health)> entry in wanted)
        {
            HorseComponent horse = entry.Value.Horse;
            if (!Original.ContainsKey(entry.Key))
            {
                Original[entry.Key] = (horse, horse.HitPointBonus);
            }
            int baseHealth = horse.Monster.HitPoints;
            int bonus = entry.Value.Health - baseHealth;
            if (horse.HitPointBonus != bonus)
            {
                SetBonus.Invoke(horse, new object[] { bonus });
                MPBalancerSubModule.Log("horse_hp: " + entry.Key + " " + (baseHealth + Original[entry.Key].Bonus) + " -> "
                    + entry.Value.Health + ".");
            }
        }
    }
}
