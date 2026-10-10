using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MPBalancer.Models;

namespace MPBalancer;

// A tunable setting that can be changed at runtime (see ChatCommands).
public interface ISetting
{
    string Name { get; }
    string Description { get; }
    string DisplayValue { get; }
    string DisplayDefault { get; }
    string DisplayVanilla { get; }
    void Reset();
    void SetVanilla();
    bool TrySet(string text, out string error);
}

// A single number, read on every hit.
public sealed class BalanceSetting : ISetting
{
    public string Name { get; }
    public string Description { get; }
    public float Default { get; }
    public float Vanilla { get; }
    public float Min { get; }
    public float Max { get; }
    public float Value { get; set; }

    // Engine parameter this setting overrides (native_parameters.xml id), or null. See NativeParameters.
    public string NativeParameter { get; }

    public BalanceSetting(string name, string description, float defaultValue, float min, float max, float vanilla = 1f, string nativeParameter = null)
    {
        NativeParameter = nativeParameter;
        Name = name;
        Description = description;
        Default = defaultValue;
        Vanilla = vanilla;
        Min = min;
        Max = max;
        Value = defaultValue;
    }

    public string DisplayValue => Format(Value);
    public string DisplayDefault => Format(Default);
    public string DisplayVanilla => Format(Vanilla);

    public void Reset() => Value = Default;

    public void SetVanilla() => Value = Vanilla;

    public bool TrySet(string text, out string error)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < Min || value > Max)
        {
            error = "Invalid value '" + text + "' for " + Name + " (allowed " + Format(Min) + " to " + Format(Max) + ").";
            return false;
        }
        Value = value;
        error = null;
        return true;
    }

    public override string ToString() => Name + " = " + DisplayValue;

    public static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

// A curve of "x:percent" points, e.g. "0:72 4:63 20:46": percent of vanilla damage at a given x
// (relative speed in m/s, or armor), linearly interpolated between points and held constant beyond the first and last.
public sealed class BalanceCurve : ISetting
{
    private (float Speed, float Percent)[] _points;

    public string Name { get; }
    public string Description { get; }
    public string DisplayDefault { get; }
    public string DisplayVanilla { get; }

    public BalanceCurve(string name, string description, string defaultValue, string vanilla = "0:100")
    {
        Name = name;
        Description = description;
        DisplayDefault = defaultValue;
        DisplayVanilla = vanilla;
        Reset();
    }

    public string DisplayValue => string.Join(" ", _points.Select(p => BalanceSetting.Format(p.Speed) + ":" + BalanceSetting.Format(p.Percent)));

    public void Reset() => _points = Parse(DisplayDefault, out _);

    public void SetVanilla() => _points = Parse(DisplayVanilla, out _);

    public bool TrySet(string text, out string error)
    {
        var points = Parse(text, out error);
        if (points == null)
        {
            return false;
        }
        _points = points;
        return true;
    }

    // Multiplier (1.0 = vanilla) at the given relative speed.
    public float Evaluate(float speed)
    {
        var points = _points;
        if (speed <= points[0].Speed)
        {
            return points[0].Percent / 100f;
        }
        for (int i = 1; i < points.Length; i++)
        {
            if (speed <= points[i].Speed)
            {
                var (s0, p0) = points[i - 1];
                var (s1, p1) = points[i];
                return (p0 + (p1 - p0) * (speed - s0) / (s1 - s0)) / 100f;
            }
        }
        return points[points.Length - 1].Percent / 100f;
    }

    private (float, float)[] Parse(string text, out string error)
    {
        var points = new List<(float, float)>();
        foreach (string token in text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = token.Split(':');
            if (pair.Length != 2
                || !float.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)
                || !float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                || speed < 0f || percent < 0f || percent > 500f)
            {
                error = "Invalid point '" + token + "' for " + Name + ": use x:percent pairs, e.g. 0:72 4:63 20:46.";
                return null;
            }
            points.Add((speed, percent));
        }
        if (points.Count == 0)
        {
            error = Name + " needs at least one speed:percent point.";
            return null;
        }
        points.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        error = null;
        return points.ToArray();
    }

    public override string ToString() => Name + " = " + DisplayValue;
}

// A list of "id:multiplier" entries, e.g. "mp_aserai_heavy_lance:0.85"; ids not listed use 1.
// "none" clears the list.
public sealed class BalanceTable : ISetting
{
    private Dictionary<string, float> _entries;

    public string Name { get; }
    public string Description { get; }
    public string DisplayDefault { get; }
    public string DisplayVanilla => "none";

    public BalanceTable(string name, string description, string defaultValue)
    {
        Name = name;
        Description = description;
        DisplayDefault = defaultValue;
        Reset();
    }

    public string DisplayValue => _entries.Count == 0 ? "none"
        : string.Join(" ", _entries.Select(e => e.Key + ":" + BalanceSetting.Format(e.Value)));

    public void Reset() => _entries = Parse(DisplayDefault, out _);

    public void SetVanilla() => _entries = new Dictionary<string, float>();

    public bool TrySet(string text, out string error)
    {
        var entries = Parse(text, out error);
        if (entries == null)
        {
            return false;
        }
        _entries = entries;
        return true;
    }

    public float Get(string id) => id != null && _entries.TryGetValue(id, out float value) ? value : 1f;

    private Dictionary<string, float> Parse(string text, out string error)
    {
        var entries = new Dictionary<string, float>();
        foreach (string token in text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int colon = token.LastIndexOf(':');
            if (colon <= 0 || !float.TryParse(token.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || value < 0f || value > 5f)
            {
                error = "Invalid entry '" + token + "' for " + Name + ": use item_id:multiplier pairs, e.g. mp_aserai_heavy_lance:0.85, or none.";
                return null;
            }
            entries[token.Substring(0, colon)] = value;
        }
        error = null;
        return entries;
    }

    public override string ToString() => Name + " = " + DisplayValue;
}

// Changes made at runtime are lost on server restart.
public static class BalanceSettings
{
    // Swings: scales the relative speed added to the swing's own speed (BalancedStrikeMagnitudeModel).
    // Vanilla by default: the balancing targets pierce, cut damage was never the issue.
    public static readonly BalanceSetting SwingSpeedBonus = new BalanceSetting("swing",
        "scales the attacker/victim relative speed fed into swing magnitude (1 = vanilla)",
        1f, 0f, 5f);

    // Melee pierce thrusts (BalancedApplyDamageModel): % of vanilla damage by speed and armor, then weapon factors.
    public static readonly BalanceCurve ThrustCurve = new BalanceCurve("thrust_curve",
        "melee pierce thrusts on players: % of vanilla damage by relative speed (speed:percent pairs)",
        "0:72 2:67 4:63 6:60 8:57 10:54 12:52 16:49 20:46");

    public static readonly BalanceCurve ThrustArmorCurve = new BalanceCurve("thrust_armor_curve",
        "melee pierce thrusts on players: extra % by the armor at the hit location (armor:percent pairs), on top of thrust_curve",
        "0:125 10:120 20:120 35:127 53:116");

    public static readonly BalanceCurve ThrustCurveHorse = new BalanceCurve("thrust_curve_horse",
        "melee pierce thrusts on horses: % of vanilla damage by relative speed (speed:percent pairs)",
        "0:100 2:94 4:89 6:85 8:81 10:78 12:76 16:73 20:70");

    public static readonly BalanceSetting ThrustSpread = new BalanceSetting("thrust_spread",
        "melee thrust damage x (weapon's listed thrust damage / thrust_ref) ^ this; 0 = all weapons as in vanilla",
        0.5f, 0f, 10f, vanilla: 0f);

    public static readonly BalanceSetting ThrustReference = new BalanceSetting("thrust_ref",
        "listed thrust damage that thrust_spread leaves unchanged; 0 = median of the weapon's usage class, lances and non-lances separately",
        0f, 0f, 200f, vanilla: 0f);

    public static readonly BalanceSetting LanceThrust = new BalanceSetting("lance",
        "extra multiplier on melee thrusts with lances (items that can couch), on players and horses; couching uses couch",
        1.15f, 0f, 5f);

    public static readonly BalanceTable WeaponMultipliers = new BalanceTable("weapon_mult",
        "extra multiplier on melee thrusts per weapon item id (item_id:multiplier pairs, or none)",
        "mp_aserai_heavy_lance:0.85");

    // Missiles and the pierce soft cap (BalancedApplyDamageModel).
    public static readonly BalanceSetting PierceMissile = new BalanceSetting("pierce_missile",
        "multiplies final pierce damage from arrows, bolts and thrown weapons",
        0.85f, 0f, 5f);

    public static readonly BalanceSetting PierceKnee = new BalanceSetting("pierce_knee",
        "pierce damage to players above this is compressed smoothly toward pierce_cap",
        90f, 0f, 1000f, vanilla: 1000f);

    public static readonly BalanceSetting PierceCap = new BalanceSetting("pierce_cap",
        "pierce damage to players never exceeds this",
        130f, 0f, 1000f, vanilla: 1000f);

    // Couched lances and braced polearms (BalancedApplyDamageModel).
    public static readonly BalanceSetting Couch = new BalanceSetting("couch",
        "multiplies final damage from couched lances and braced polearms, before the couch curve",
        1f, 0f, 5f);

    public static readonly BalanceSetting CouchKnee = new BalanceSetting("couch_knee",
        "couch/brace damage above this is compressed smoothly toward couch_cap (0 = curve starts at 0)",
        0f, 0f, 1000f, vanilla: 1000f);

    public static readonly BalanceSetting CouchCap = new BalanceSetting("couch_cap",
        "couch/brace damage never exceeds this",
        200f, 0f, 1000f, vanilla: 1000f);

    // Horse top speed (BalancedAgentStatModel); changes apply to every horse immediately.
    public static readonly BalanceSetting HorseSpeed = new BalanceSetting("horse_speed",
        "multiplies horse top speed",
        0.92f, 0.1f, 2f);

    public static readonly BalanceSetting HorseKnee = new BalanceSetting("horse_knee",
        "horse top speeds (m/s, after horse_speed) above this are compressed smoothly toward horse_cap",
        12.4f, 0f, 1000f, vanilla: 1000f);

    public static readonly BalanceSetting HorseCap = new BalanceSetting("horse_cap",
        "horse top speed (m/s) never exceeds this; keep well above 9, the minimum speed to hold a couched lance",
        14.2f, 0f, 1000f, vanilla: 1000f);

    // Horse handling (BalancedAgentStatModel); changes apply to every horse immediately.
    public static readonly BalanceSetting HorseManeuver = new BalanceSetting("horse_maneuver",
        "multiplies horse maneuver (turning)",
        0.9f, 0.1f, 3f);

    public static readonly BalanceSetting HorseAccelTime = new BalanceSetting("horse_accel_time",
        "multiplies the horse's TopSpeedReachDuration stat (vanilla 0.15-0.7); by its name higher = slower to reach top speed, check the [MPB-ACCEL] log",
        1f, 0.1f, 5f);

    // Engine (native) parameters, overridden live on the server (NativeParameters). Clients keep their own
    // values, so watch for snapping.
    public static readonly BalanceSetting HorseTurnKeepVelocity = new BalanceSetting("horse_turn_keep_speed",
        "native quadrupedal_delta_rotation_threshold_for_keeping_local_velocity: how sharp a horse can turn before losing its speed",
        0.6f, 0f, 10f, vanilla: 0.6f, nativeParameter: "quadrupedal_delta_rotation_threshold_for_keeping_local_velocity");

    public static readonly BalanceSetting CouchMinSpeed = new BalanceSetting("couch_min_speed",
        "native mounted_passive_action_minimum_speed: minimum horse speed (m/s) to couch a lance",
        9f, 0f, 50f, vanilla: 9f, nativeParameter: "mounted_passive_action_minimum_speed");

    // Horse charges ("bumps", HorseCharge). The engine also requires a running gait, so this only raises the bar.
    public static readonly BalanceSetting BumpSpeed = new BalanceSetting("bump_speed",
        "minimum relative speed (m/s) for a horse to bump someone (camels keep their +16%); applies to horses spawned after the change",
        4.8f, 0f, 50f, vanilla: 4.3f);

    public static readonly IReadOnlyList<ISetting> All = new ISetting[]
    {
        SwingSpeedBonus,
        ThrustCurve, ThrustArmorCurve, ThrustCurveHorse, ThrustSpread, ThrustReference, LanceThrust, WeaponMultipliers,
        PierceMissile, PierceKnee, PierceCap,
        Couch, CouchKnee, CouchCap,
        HorseSpeed, HorseKnee, HorseCap, HorseManeuver, HorseAccelTime, HorseTurnKeepVelocity, CouchMinSpeed,
        BumpSpeed,
    };

    // Re-applies settings that are baked into agents rather than read on every hit.
    public static void ApplyLive()
    {
        BalancedAgentStatModel.RefreshAllMounts();
        HorseCharge.ApplySpeedLimit();
        NativeParameters.Apply();
    }

    public static ISetting Find(string name)
    {
        foreach (ISetting setting in All)
        {
            if (setting.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return setting;
            }
        }
        return null;
    }

    // Settings whose name starts with the given text (for typo-friendly lookups).
    public static List<ISetting> FindByPrefix(string prefix)
    {
        return All.Where(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // The setting whose name is closest to the given text (edit distance), for "did you mean" hints.
    public static ISetting Closest(string name)
    {
        return All.OrderBy(s => EditDistance(s.Name, name.ToLowerInvariant())).First();
    }

    public static string Summary() => string.Join(", ", All);

    private static int EditDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }
        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[a.Length, b.Length];
    }
}
