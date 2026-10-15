using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Records every damaging hit (HitLog, CSV), and shows a per-player
// hit readout in chat (toggled with "!b debug"). Players with it on see every hit they deal
// or take, e.g. "you -> Bob | pierce, neck | 9.4 m/s (+134%) | armor 12 | vanilla 96 -> 67".
// The part is the engine's hit body part (head, neck, chest, abdomen, shoulders, arms, legs), not the
// head/"body" split of IsHeadShot, which lumps everything else together.
// Lines are queued during damage calculation and sent on the next mission tick (ServerTickBehavior),
// never from inside the game's combat or network code.
internal static class HitDebug
{
    private static readonly HashSet<NetworkCommunicator> _subscribers = new HashSet<NetworkCommunicator>();

    private static readonly Queue<(NetworkCommunicator Peer, string Text)> _pending = new Queue<(NetworkCommunicator, string)>();

    public static bool Toggle(NetworkCommunicator peer)
    {
        lock (_subscribers)
        {
            if (_subscribers.Remove(peer))
            {
                return false;
            }
            _subscribers.Add(peer);
            return true;
        }
    }

    // closing: the closing speed along the thrust that the speed curve used (melee pierce thrusts), or -1.
    public static void Report(in AttackInformation attackInformation, in AttackCollisionData collisionData, DamageTypes damageType, float vanillaDamage, float beforeDamage, float finalDamage, float closing)
    {
        // Only hits that land on a player or bot and deal damage (no shield blocks).
        if (collisionData.AttackBlockedWithShield || attackInformation.IsVictimAgentNull || finalDamage <= 0f)
        {
            return;
        }
        float relativeSpeed = Combat.RelativeSpeed(in attackInformation);
        string part = BodyPart(collisionData.VictimHitBodyPart);

        // Every damaging hit goes to the hit log (HitLog: values only, written to CSV off the main thread).
        HitLog.Record(new HitLog.Hit
        {
            Time = System.DateTime.UtcNow,
            Settings = HitLog.SettingsVersion,
            Type = TypeName(damageType),
            Source = collisionData.IsMissile ? "missile"
                : collisionData.IsHorseCharge ? "charge"
                : collisionData.IsFallDamage ? "fall"
                : attackInformation.IsAttackerAgentDoingPassiveAttack ? "couch"
                : (StrikeType)collisionData.StrikeType == StrikeType.Thrust ? "thrust" : "swing",
            Part = part ?? "none",
            Victim = attackInformation.IsVictimAgentHuman ? "human" : "horse",
            Mounted = attackInformation.DoesAttackerHaveMountAgent,
            Speed = relativeSpeed,
            Bonus = collisionData.MovementSpeedDamageModifier * 100f,
            Armor = attackInformation.ArmorAmountFloat,
            Vanilla = vanillaDamage,
            Before = beforeDamage,
            After = finalDamage,
            Weapon = attackInformation.AttackerWeapon.Item?.StringId ?? "none",
            Stat = Combat.ThrustStat(in attackInformation),
            AttackerSpeed = Combat.AttackerSpeed(in attackInformation),
            VictimSpeed = Combat.VictimSpeed(in attackInformation),
            AttackerHorse = Combat.HorseId(attackInformation.AttackerAgent),
            VictimHorse = Combat.HorseId(attackInformation.VictimAgent),
            Closing = closing,
        });

        // Only pierce hits in chat (not bumps; the other damage types stay vanilla), and the text is only built when
        // someone has the readout on.
        if (collisionData.IsHorseCharge || damageType != DamageTypes.Pierce || !AnyoneWatching)
        {
            return;
        }
        string damage = Round(vanillaDamage) == Round(finalDamage)
            ? Round(finalDamage) + " (= vanilla)"
            : "vanilla " + Round(vanillaDamage) + " -> " + Round(finalDamage);
        string text = TypeName(damageType) + (part != null ? ", " + part : "")
            + " | " + (closing >= 0f ? "closing " + closing.ToString("0.0", CultureInfo.InvariantCulture) : relativeSpeed.ToString("0.0", CultureInfo.InvariantCulture)) + " m/s"
            + " | armor " + MathF.Round(attackInformation.ArmorAmountFloat)
            + " | " + damage;
        Notify(attackInformation.AttackerAgent, attackInformation.VictimAgent, text);
    }

    // Whether anyone has the readout on: a cheap check before building readout text.
    public static bool AnyoneWatching
    {
        get
        {
            lock (_subscribers)
            {
                return _subscribers.Count > 0;
            }
        }
    }

    private static string TypeName(DamageTypes type)
    {
        switch (type)
        {
            case DamageTypes.Cut: return "cut";
            case DamageTypes.Pierce: return "pierce";
            case DamageTypes.Blunt: return "blunt";
            default: return "invalid";
        }
    }

    // Queues a readout line for the attacker and the victim, if they have the readout on.
    public static void Notify(Agent attackerAgent, Agent victimAgent, string text)
    {
        NetworkCommunicator attacker = GetPeer(attackerAgent);
        NetworkCommunicator victim = GetPeer(victimAgent);
        bool attackerSubscribed;
        bool victimSubscribed;
        lock (_subscribers)
        {
            attackerSubscribed = attacker != null && _subscribers.Contains(attacker);
            victimSubscribed = victim != null && _subscribers.Contains(victim);
        }
        if (!attackerSubscribed && !victimSubscribed)
        {
            return;
        }

        lock (_pending)
        {
            if (attackerSubscribed)
            {
                _pending.Enqueue((attacker, "you -> " + Name(victimAgent) + " | " + text));
            }
            if (victimSubscribed && victim != attacker)
            {
                _pending.Enqueue((victim, Name(attackerAgent) + " -> you | " + text));
            }
        }
    }

    public static void ProcessPending()
    {
        while (true)
        {
            (NetworkCommunicator Peer, string Text) item;
            lock (_pending)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
                item = _pending.Dequeue();
            }
            if (item.Peer.IsConnectionActive)
            {
                ChatCommands.Send(item.Peer, "[MPB] " + item.Text);
            }
        }
    }

    private static NetworkCommunicator GetPeer(Agent agent)
    {
        // A mount's rider is the one who should see hits on or by the horse.
        Agent controller = agent != null && agent.IsMount ? agent.RiderAgent : agent;
        return controller?.MissionPeer?.GetNetworkPeer();
    }

    private static string Name(Agent agent)
    {
        if (agent == null)
        {
            return "?";
        }
        if (agent.IsMount)
        {
            return agent.RiderAgent != null ? Name(agent.RiderAgent) + "'s horse" : "horse";
        }
        return agent.MissionPeer?.GetNetworkPeer()?.UserName ?? agent.Name?.ToString() ?? "?";
    }

    private static int Round(float value) => MathF.Round(value);

    private static string BodyPart(BoneBodyPartType part)
    {
        switch (part)
        {
            case BoneBodyPartType.Head: return "head";
            case BoneBodyPartType.Neck: return "neck";
            case BoneBodyPartType.Chest: return "chest";
            case BoneBodyPartType.Abdomen: return "abdomen";
            case BoneBodyPartType.ShoulderLeft:
            case BoneBodyPartType.ShoulderRight: return "shoulder";
            case BoneBodyPartType.ArmLeft:
            case BoneBodyPartType.ArmRight: return "arm";
            case BoneBodyPartType.Legs: return "legs";
            default: return null;
        }
    }
}
