using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Gameplay.Perks.Effects;

namespace MPBalancer;

// small_shields: how many shields the Khuzait light cavalry's Small Shield perk gives (vanilla 1, in Item2). With 2,
// a copy of the perk's shield effect puts a second Steppe Guardian Shield in Item3, which that class never uses with
// this perk (Item3 is the bow of Nomad Archer, in the same perk list).
//
// The perk's effects are a list in the class's perk template (mpclassdivisions.xml); each player's perk is a copy made
// when they pick it (MPPerkObject.Clone, a new list holding the same effect objects). The extra effect goes into the
// template and into the copies players hold, so it applies from the next spawn, in every game mode, to players and
// their troops. The server sends spawned equipment (and the pre-spawn preview) to clients; their class selection
// screen, built from their own files, still shows one shield.
internal static class SmallShieldPerk
{
    private const string ClassId = "mp_light_cavalry_khuzait";
    private const string PerkNameId = "{=W4iKIM2s}"; // "Small Shield"
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo ClassPerks = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("_perks", Instance);
    private static readonly FieldInfo PerkName = typeof(MPPerkObject).GetField("_name", Instance);
    private static readonly FieldInfo PerkEffects = typeof(MPPerkObject).GetField("_effects", Instance);
    private static readonly FieldInfo EffectIndex = typeof(AlternativeEquipmentEffect).GetField("_index", Instance);
    private static readonly FieldInfo PeerSelectedPerks = typeof(MissionPeer).GetField("_selectedPerks", Instance);
    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", Instance);

    private static MPPerkEffectBase _shield, _extra;
    private static bool _warned;

    public static void Apply()
    {
        try
        {
            ApplyUnsafe();
        }
        catch (System.Exception e)
        {
            MPBalancerSubModule.Log("small_shields: failed (" + e.GetType().Name + ": " + e.Message + "); left as it was.");
        }
    }

    private static void ApplyUnsafe()
    {
        bool wanted = BalanceSettings.SmallShields.Value >= 1.5f;
        if (!Find(out MPPerkObject template))
        {
            if (wanted && !_warned)
            {
                _warned = true;
                MPBalancerSubModule.Log("small_shields: couldn't find the Small Shield perk of " + ClassId + "; it stays vanilla.");
            }
            return;
        }
        bool changed = Update((List<MPPerkEffectBase>)PerkEffects.GetValue(template), wanted);
        foreach (MPPerkObject perk in HeldCopies())
        {
            Update((List<MPPerkEffectBase>)PerkEffects.GetValue(perk), wanted);
        }
        if (changed)
        {
            MPBalancerSubModule.Log("small_shields: " + ClassId + " Small Shield gives " + (wanted ? "2 shields (Item2, Item3)." : "1 shield (vanilla)."));
        }
    }

    // Adds or removes the extra effect in one perk's effect list (the template's or a player's copy). True if changed.
    private static bool Update(List<MPPerkEffectBase> effects, bool wanted)
    {
        if (effects == null || !effects.Contains(_shield))
        {
            return false;
        }
        bool has = effects.Contains(_extra);
        if (wanted && !has)
        {
            effects.Add(_extra);
            return true;
        }
        if (!wanted && has)
        {
            effects.Remove(_extra);
            return true;
        }
        return false;
    }

    // The perk template, its shield effect (_shield) and the Item3 copy of it (_extra), found once.
    private static bool Find(out MPPerkObject template)
    {
        template = null;
        if (ClassPerks == null || PerkName == null || PerkEffects == null || EffectIndex == null || Clone == null)
        {
            return false;
        }
        var classes = MultiplayerClassDivisions.GetMPHeroClasses();
        if (classes == null)
        {
            return false;
        }
        MultiplayerClassDivisions.MPHeroClass heroClass = null;
        foreach (MultiplayerClassDivisions.MPHeroClass c in classes)
        {
            if (c.StringId == ClassId)
            {
                heroClass = c;
                break;
            }
        }
        if (heroClass == null || !(ClassPerks.GetValue(heroClass) is IEnumerable perks))
        {
            return false;
        }
        foreach (object perk in perks)
        {
            if (perk is MPPerkObject p && ((string)PerkName.GetValue(p))?.StartsWith(PerkNameId) == true)
            {
                template = p;
                break;
            }
        }
        if (template == null)
        {
            return false;
        }
        if (_shield == null)
        {
            foreach (MPPerkEffectBase effect in (List<MPPerkEffectBase>)PerkEffects.GetValue(template))
            {
                if (effect is AlternativeEquipmentEffect && (EquipmentIndex)EffectIndex.GetValue(effect) == EquipmentIndex.Weapon2)
                {
                    _shield = effect;
                    _extra = (MPPerkEffectBase)Clone.Invoke(effect, null);
                    EffectIndex.SetValue(_extra, EquipmentIndex.Weapon3);
                    break;
                }
            }
        }
        return _shield != null;
    }

    // The perk copies players currently hold (MissionPeer._selectedPerks: (troop index, perks)).
    private static IEnumerable<MPPerkObject> HeldCopies()
    {
        if (PeerSelectedPerks == null || GameNetwork.NetworkPeers == null)
        {
            yield break;
        }
        foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
        {
            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
            {
                continue;
            }
            object selected = PeerSelectedPerks.GetValue(missionPeer);
            if (selected?.GetType().GetField("Item2")?.GetValue(selected) is IEnumerable perks)
            {
                foreach (object perk in perks)
                {
                    if (perk is MPPerkObject p)
                    {
                        yield return p;
                    }
                }
            }
        }
    }
}
