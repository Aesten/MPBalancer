using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.Library;

namespace MPBalancer;

// Every damaging hit, as CSV rows for analysis (scripts/hit-stats.py), one set of files per server
// run in MPB_DATA_DIR (run-server.sh points it at ~/bannerlord/metrics, where earlier runs are gzipped and kept
// 30 days; without it, Modules/MPBalancer/data):
//
//   hits-<start>.csv      time, settings, type, src, part, victim, mounted, speed, bonus, armor, vanilla, before,
//                         after, weapon, stat, aspeed, vspeed, ahorse, vhorse, closing
//                         (the old "[MPB-HIT]" log fields; vanilla = what the game would have dealt, before = the
//                         damage before the final-stage settings, after = the damage dealt)
//   settings-<start>.csv  time, settings, values: which balance settings each "settings" number stands for (a new
//                         number after every !mpb change), so hits can be compared per configuration
//
// Combat code only queues a struct of values (no strings built, no disk access, nothing allocated but the queue
// node); a background thread formats and writes the queued rows once a second.
internal static class HitLog
{
    internal struct Hit
    {
        public DateTime Time;
        public int Settings;
        public string Type, Source, Part, Victim, Weapon, AttackerHorse, VictimHorse;
        public bool Mounted;
        public float Speed, Bonus, Armor, Vanilla, Before, After, AttackerSpeed, VictimSpeed;
        public float Closing; // melee pierce thrusts: the closing speed along the thrust the speed curve used; else -1
        public int Stat;
    }

    private const int WriteIntervalMs = 1000;

    private static readonly ConcurrentQueue<Hit> Hits = new ConcurrentQueue<Hit>();
    private static readonly ConcurrentQueue<(DateTime Time, int Version, string Values)> SettingsChanges = new ConcurrentQueue<(DateTime, int, string)>();

    private static int _started;
    private static StreamWriter _hits, _settings;

    // The current balance configuration's number, in every row.
    public static int SettingsVersion { get; private set; }

    // Opens the files and starts the writer, once (called at the first mission, on the dedicated server).
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }
        try
        {
            string dir = Environment.GetEnvironmentVariable("MPB_DATA_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(BasePath.Name, "Modules", "MPBalancer", "data");
            }
            Directory.CreateDirectory(dir);
            string start = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            _hits = Open(dir, "hits-" + start + ".csv",
                "time,settings,type,src,part,victim,mounted,speed,bonus,armor,vanilla,before,after,weapon,stat,aspeed,vspeed,ahorse,vhorse,closing");
            _settings = Open(dir, "settings-" + start + ".csv", "time,settings,values");
            SettingsChanged();
            new Thread(WriteLoop) { IsBackground = true, Name = "MPBalancer hit log" }.Start();
            MPBalancerSubModule.Log("Writing hits to " + dir + " (hits-" + start + ".csv).");
        }
        catch (Exception e)
        {
            _hits = _settings = null;
            MPBalancerSubModule.Log("Could not open the hit log: " + e.Message);
        }
    }

    private static StreamWriter Open(string dir, string name, string header)
    {
        StreamWriter writer = new StreamWriter(Path.Combine(dir, name), append: false, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine(header);
        writer.Flush();
        return writer;
    }

    // Called after every settings change (BalanceSettings.ApplyLive) and at start.
    public static void SettingsChanged()
    {
        SettingsVersion++;
        SettingsChanges.Enqueue((DateTime.UtcNow, SettingsVersion, BalanceSettings.Summary()));
    }

    public static void Record(in Hit hit)
    {
        if (_hits != null)
        {
            Hits.Enqueue(hit);
        }
    }

    private static void WriteLoop()
    {
        while (true)
        {
            Thread.Sleep(WriteIntervalMs);
            try
            {
                WritePending();
            }
            catch (Exception e)
            {
                MPBalancerSubModule.Log("Hit log write failed: " + e.Message);
            }
        }
    }

    private static void WritePending()
    {
        if (!SettingsChanges.IsEmpty)
        {
            while (SettingsChanges.TryDequeue(out var change))
            {
                _settings.WriteLine(Time(change.Time) + "," + change.Version + "," + Quote(change.Values));
            }
            _settings.Flush();
        }
        if (!Hits.IsEmpty)
        {
            StringBuilder line = new StringBuilder(256);
            while (Hits.TryDequeue(out Hit h))
            {
                line.Clear()
                    .Append(Time(h.Time)).Append(',').Append(h.Settings).Append(',')
                    .Append(h.Type).Append(',').Append(h.Source).Append(',').Append(h.Part).Append(',')
                    .Append(h.Victim).Append(',').Append(h.Mounted ? 1 : 0).Append(',')
                    .Append(F1(h.Speed)).Append(',').Append(MathF.Round(h.Bonus)).Append(',')
                    .Append(MathF.Round(h.Armor)).Append(',')
                    .Append(F1(h.Vanilla)).Append(',').Append(F1(h.Before)).Append(',').Append(F1(h.After)).Append(',')
                    .Append(h.Weapon).Append(',').Append(h.Stat).Append(',')
                    .Append(F1(h.AttackerSpeed)).Append(',').Append(F1(h.VictimSpeed)).Append(',')
                    .Append(h.AttackerHorse).Append(',').Append(h.VictimHorse).Append(',').Append(h.Closing >= 0f ? F1(h.Closing) : "");
                _hits.WriteLine(line.ToString());
            }
            _hits.Flush();
        }
    }

    private static string Time(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string F1(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
