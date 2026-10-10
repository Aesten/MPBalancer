using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Logs every damaging hit to the server log ("[MPB-HIT] key=value ..."), and shows a per-player
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

    public static void Report(in AttackInformation attackInformation, in AttackCollisionData collisionData, DamageTypes damageType, float vanillaDamage, float beforeDamage, float finalDamage)
    {
        // Only hits that land on a player or bot and deal damage (no shield blocks).
        if (collisionData.AttackBlockedWithShield || attackInformation.IsVictimAgentNull || finalDamage <= 0f)
        {
            return;
        }
        string source = collisionData.IsMissile ? "missile"
            : collisionData.IsHorseCharge ? "charge"
            : collisionData.IsFallDamage ? "fall"
            : attackInformation.IsAttackerAgentDoingPassiveAttack ? "couch/brace"
            : (StrikeType)collisionData.StrikeType == StrikeType.Thrust ? "thrust" : "swing";
        string speedBonus = collisionData.MovementSpeedDamageModifier != 0f
            ? " (" + (collisionData.MovementSpeedDamageModifier > 0f ? "+" : "") + MathF.Round(collisionData.MovementSpeedDamageModifier * 100f) + "%)"
            : "";
        string damage = Round(vanillaDamage) == Round(finalDamage)
            ? Round(finalDamage) + " (= vanilla)"
            : "vanilla " + Round(vanillaDamage) + " -> " + Round(finalDamage);
        float relativeSpeed = Combat.RelativeSpeed(in attackInformation);
        string part = BodyPart(collisionData.VictimHitBodyPart);
        string text = damageType.ToString().ToLowerInvariant() + (part != null ? ", " + part : "")
            + " | " + relativeSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s" + speedBonus
            + " | armor " + MathF.Round(attackInformation.ArmorAmountFloat)
            + " | " + damage;

        // Every damaging hit goes to the server log, for analysis (grep "[MPB-HIT]").
        MPBalancerSubModule.Log("[MPB-HIT] type=" + damageType.ToString().ToLowerInvariant()
            + " src=" + source.Replace("couch/brace", "couch")
            + " part=" + (part ?? "none")
            + " victim=" + (attackInformation.IsVictimAgentHuman ? "human" : "horse")
            + " mounted=" + (attackInformation.DoesAttackerHaveMountAgent ? 1 : 0)
            + " speed=" + relativeSpeed.ToString("0.0", CultureInfo.InvariantCulture)
            + " bonus=" + MathF.Round(collisionData.MovementSpeedDamageModifier * 100f)
            + " armor=" + MathF.Round(attackInformation.ArmorAmountFloat)
            + " vanilla=" + vanillaDamage.ToString("0.#", CultureInfo.InvariantCulture)
            + " before=" + beforeDamage.ToString("0.#", CultureInfo.InvariantCulture)
            + " after=" + finalDamage.ToString("0.#", CultureInfo.InvariantCulture)
            + " weapon=" + (attackInformation.AttackerWeapon.Item?.StringId ?? "none")
            + " stat=" + WeaponFactors.ThrustStat(in attackInformation)
            + " aspeed=" + Combat.AttackerSpeed(in attackInformation).ToString("0.0", CultureInfo.InvariantCulture)
            + " vspeed=" + Combat.VictimSpeed(in attackInformation).ToString("0.0", CultureInfo.InvariantCulture)
            + " ahorse=" + Combat.HorseId(attackInformation.AttackerAgent)
            + " vhorse=" + Combat.HorseId(attackInformation.VictimAgent));

        if (collisionData.IsHorseCharge)
        {
            // Bumps get their own readout (speed and knock outcome) from HorseCharge.Report.
            return;
        }
        // Only pierce in chat: the other damage types stay vanilla (they're still logged above).
        if (damageType != DamageTypes.Pierce)
        {
            return;
        }
        Notify(attackInformation.AttackerAgent, attackInformation.VictimAgent, text);
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
