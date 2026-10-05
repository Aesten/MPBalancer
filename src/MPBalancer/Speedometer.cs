using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// "!b speedo": every second, tells the player how fast their horse moves as the server sees it
// (measured from positions and from the engine's velocity) and its top speed stat. This is how we found
// that the server is in charge of movement, so server-side horse speed changes really slow horses.
internal static class Speedometer
{
    private sealed class Track
    {
        public Agent Mount;
        public Vec2 LastPosition;
        public float Distance;
        public float Elapsed;
        public float Top;
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
            if (track.Mount != mount)
            {
                // New horse (or remounted): start measuring from here.
                track.Mount = mount;
                track.LastPosition = position;
                track.Distance = 0f;
                track.Elapsed = 0f;
                track.Top = 0f;
                continue;
            }
            track.Distance += (position - track.LastPosition).Length;
            track.LastPosition = position;
            track.Elapsed += dt;
            if (track.Elapsed < 1f)
            {
                continue;
            }
            float measured = track.Distance / track.Elapsed;
            track.Top = MathF.Max(track.Top, measured);
            float engine = mount.MovementVelocity.Length;
            float stat = mount.GetAgentDrivenPropertyValue(DrivenProperty.MountSpeed);
            string horse = mount.SpawnEquipment?[EquipmentIndex.Horse].Item?.StringId ?? "?";
            string line = "speed " + F(measured) + " m/s (top " + F(track.Top) + ") | engine " + F(engine)
                + " | MountSpeed stat " + F(stat) + " | horse_speed " + F(BalanceSettings.HorseSpeed.Value);
            ChatCommands.Send(peer, "[MPB] " + line);
            MPBalancerSubModule.Log("[MPB-SPEED] peer=" + peer.UserName.Replace(' ', '_') + " horse=" + horse
                + " measured=" + F(measured) + " engine=" + F(engine) + " stat=" + F(stat)
                + " horse_speed=" + F(BalanceSettings.HorseSpeed.Value));
            track.Distance = 0f;
            track.Elapsed = 0f;
        }
        foreach (NetworkCommunicator peer in disconnected)
        {
            _tracks.Remove(peer);
        }
    }

    private static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
