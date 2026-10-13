using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace MPBalancer;

// item_weight: sets the weight of items on the server (ItemObject.Weight, private setter), e.g. the Khuzait Rabble's
// 5 kg Curved Round Shield. In multiplayer, movement speed falls with the weight of weapons and shields carried, and
// more with those held (MultiplayerAgentStatCalculateModel); armor weight plays no part.
//
// Characters spawned after the change use the new weight (each spawn computes its equipment weight afresh). Clients
// keep the vanilla weight for predicting their own movement; the server's position wins, so watch for snapping. Items
// taken out of the table get their original weight back.
internal static class ItemWeightOverrides
{
    private static readonly MethodInfo SetWeight = typeof(ItemObject).GetProperty("Weight")?.GetSetMethod(nonPublic: true);
    private static readonly Dictionary<ItemObject, float> Original = new Dictionary<ItemObject, float>();

    public static void Apply()
    {
        if (SetWeight == null || MBObjectManager.Instance == null)
        {
            return;
        }
        var wanted = new Dictionary<ItemObject, float>();
        foreach (KeyValuePair<string, float> entry in BalanceSettings.ItemWeights.Entries)
        {
            ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(entry.Key);
            if (item == null)
            {
                MPBalancerSubModule.Log("item_weight: no item " + entry.Key + ".");
                continue;
            }
            wanted[item] = entry.Value;
        }
        // Restore items no longer listed.
        foreach (ItemObject item in new List<ItemObject>(Original.Keys))
        {
            if (!wanted.ContainsKey(item))
            {
                SetWeight.Invoke(item, new object[] { Original[item] });
                Original.Remove(item);
            }
        }
        foreach (KeyValuePair<ItemObject, float> entry in wanted)
        {
            if (!Original.ContainsKey(entry.Key))
            {
                Original[entry.Key] = entry.Key.Weight;
            }
            if (entry.Key.Weight != entry.Value)
            {
                SetWeight.Invoke(entry.Key, new object[] { entry.Value });
                MPBalancerSubModule.Log("item_weight: " + entry.Key.StringId + " " + BalanceSetting.Format(Original[entry.Key])
                    + " -> " + BalanceSetting.Format(entry.Value) + " kg.");
            }
        }
    }
}
