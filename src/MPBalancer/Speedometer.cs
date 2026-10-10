using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// "!b speedo": every second, tells the player their horse's speed as the server measures it (from positions),
// and the top speed since mounting. The server log also gets, silently, the engine velocity, horse turn
// rate and stats ([MPB-SPEED]), and the time from a standstill to 9 m/s (couch) and to 95% of top speed
// ([MPB-ACCEL]). This is how we found that the server is in charge of movement, so server-side horse speed
// changes really slow horses.
// "!b speedo stats": prints the current horse's stats once (PrintStats).
internal static class Speedometer
{
    private const float CouchSpeed = 9f;

    private sealed class Track
    {
        public Agent Mount;
        public Vec2 LastPosition;
        public float LastHeading;
        public float Distance;
        public float Turned;
        public float Elapsed;
        public float Top;
        public float TopTurn;
        public float AccelTime = -1f; // < 0: not timing a start
        public bool ReachedCouch;
    }

    private static readonly Dictionary<NetworkCommunicator, Track> _tracks = new Dictionary<NetworkCommunicator, Track>();

    public static bool Toggle(NetworkCommunicator peer)
    {
        if (_tracks.Remove(peer))
        {
            return false;
        }
        _tracks[peer] = new Track();
        return true;
    }

    public static void Tick(float dt)
    {
        if (_tracks.Count == 0)
        {
            return;
        }
        var disconnected = new List<NetworkCommunicator>();
        foreach (KeyValuePair<NetworkCommunicator, Track> entry in _tracks)
        {
            NetworkCommunicator peer = entry.Key;
            Track track = entry.Value;
            if (!peer.IsConnectionActive)
            {
                disconnected.Add(peer);
                continue;
            }
            Agent mount = peer.ControlledAgent?.MountAgent;
            if (mount == null || !mount.IsActive())
            {
                track.Mount = null;
                continue;
            }
            Vec2 position = mount.Position.AsVec2;
            float heading = Heading(mount);
            if (track.Mount != mount)
            {
                // New horse (or remounted): start measuring from here.
                track.Mount = mount;
                track.LastPosition = position;
                track.LastHeading = heading;
                track.Distance = 0f;
                track.Turned = 0f;
                track.Elapsed = 0f;
                track.Top = 0f;
                track.TopTurn = 0f;
                track.AccelTime = -1f;
                continue;
            }
            track.Distance += (position - track.LastPosition).Length;
            track.Turned += MathF.Abs(MBMath.GetSmallestDifferenceBetweenTwoAngles(track.LastHeading, heading));
            track.LastPosition = position;
            track.LastHeading = heading;
            track.Elapsed += dt;

            float engine = mount.MovementVelocity.Length;
            float stat = mount.GetAgentDrivenPropertyValue(DrivenProperty.MountSpeed);
            TimeAcceleration(peer, track, mount, dt, engine, stat);

            if (track.Elapsed < 1f)
            {
                continue;
            }
            float measured = track.Distance / track.Elapsed;
            float turn = track.Turned / track.Elapsed * MathF.RadToDeg;
            track.Top = MathF.Max(track.Top, measured);
            track.TopTurn = MathF.Max(track.TopTurn, turn);
            float maneuver = mount.GetAgentDrivenPropertyValue(DrivenProperty.MountManeuver);
            float accel = mount.GetAgentDrivenPropertyValue(DrivenProperty.TopSpeedReachDuration);
            ChatCommands.Send(peer, "[MPB] speed " + F(measured) + " m/s (top " + F(track.Top) + ")");
            MPBalancerSubModule.Log("[MPB-SPEED] peer=" + PeerName(peer) + " horse=" + HorseId(mount)
                + " measured=" + F(measured) + " engine=" + F(engine) + " turn=" + F0(turn)
                + " stat=" + F(stat) + " maneuver=" + F(maneuver) + " accel=" + F2(accel)
                + " horse_speed=" + F(BalanceSettings.HorseSpeed.Value)
                + " horse_maneuver=" + F2(BalanceSettings.HorseManeuver.Value)
                + " horse_accel_time=" + F2(BalanceSettings.HorseAccelTime.Value));
            track.Distance = 0f;
            track.Turned = 0f;
            track.Elapsed = 0f;
        }
        foreach (NetworkCommunicator peer in disconnected)
        {
            _tracks.Remove(peer);
        }
    }

    // Times a start from a standstill: to couch speed, then to 95% of the top speed stat.
    private static void TimeAcceleration(NetworkCommunicator peer, Track track, Agent mount, float dt, float engine, float stat)
    {
        if (engine < 1f)
        {
            track.AccelTime = 0f;
            track.ReachedCouch = false;
            return;
        }
        if (track.AccelTime < 0f)
        {
            return;
        }
        track.AccelTime += dt;
        if (!track.ReachedCouch && engine >= CouchSpeed)
        {
            track.ReachedCouch = true;
            ReportAcceleration(peer, mount, "0 -> " + F0(CouchSpeed) + " m/s (couch)", track.AccelTime);
        }
        if (engine >= stat * 0.95f)
        {
            ReportAcceleration(peer, mount, "0 -> " + F(engine) + " m/s (95% of top)", track.AccelTime);
            track.AccelTime = -1f;
        }
    }

    private static void ReportAcceleration(NetworkCommunicator peer, Agent mount, string what, float seconds)
    {
        MPBalancerSubModule.Log("[MPB-ACCEL] peer=" + PeerName(peer) + " horse=" + HorseId(mount)
            + " what=" + what.Replace(' ', '_') + " seconds=" + F2(seconds)
            + " horse_accel_time=" + F2(BalanceSettings.HorseAccelTime.Value));
    }

    // One-off readout of the player's horse: item stats, then the server's stats (after settings, riding
    // skill and perks), and the bump threshold.
    public static void PrintStats(NetworkCommunicator peer)
    {
        Agent mount = peer.ControlledAgent?.MountAgent;
        if (mount == null || !mount.IsActive())
        {
            ChatCommands.Send(peer, "[MPB] You're not on a horse.");
            return;
        }
        EquipmentElement horse = mount.SpawnEquipment[EquipmentIndex.Horse];
        HorseComponent item = horse.Item?.HorseComponent;
        string itemStats = item == null ? "?"
            : "speed " + item.Speed + ", maneuver " + item.Maneuver + ", charge " + item.ChargeDamage + ", hp " + item.HitPoints;
        ChatCommands.Send(peer, "[MPB] " + HorseId(mount) + " | item: " + itemStats);
        ChatCommands.Send(peer, "[MPB] server: top speed " + F(mount.GetAgentDrivenPropertyValue(DrivenProperty.MountSpeed)) + " m/s"
            + ", maneuver " + F(mount.GetAgentDrivenPropertyValue(DrivenProperty.MountManeuver))
            + ", accel time " + F2(mount.GetAgentDrivenPropertyValue(DrivenProperty.TopSpeedReachDuration))
            + ", charge " + F2(mount.GetAgentDrivenPropertyValue(DrivenProperty.MountChargeDamage))
            + ", health " + F0(mount.Health) + "/" + F0(mount.HealthLimit));
        ChatCommands.Send(peer, "[MPB] bump threshold " + F(mount.Monster.RelativeSpeedLimitForCharge) + " m/s"
            + " (for horses spawned from now; yours uses the value from when it spawned)");
    }

    private static float Heading(Agent mount)
    {
        Vec3 forward = mount.Frame.rotation.f;
        return MathF.Atan2(forward.y, forward.x);
    }

    private static string HorseId(Agent mount) => mount.SpawnEquipment?[EquipmentIndex.Horse].Item?.StringId ?? "?";

    private static string PeerName(NetworkCommunicator peer) => peer.UserName.Replace(' ', '_');

    private static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string F0(float value) => value.ToString("0", CultureInfo.InvariantCulture);

    private static string F2(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
