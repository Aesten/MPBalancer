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

    // The default comes from defaults.txt (BalanceDefaults); vanilla if it's missing or invalid there.
    public BalanceSetting(string name, string description, float min, float max, float vanilla = 1f, string nativeParameter = null)
    {
        NativeParameter = nativeParameter;
        Name = name;
        Description = description;
        Vanilla = vanilla;
        Min = min;
        Max = max;
        string text = BalanceDefaults.Get(name);
        if (text == null)
        {
            BalanceDefaults.Warn(name, "is missing");
            Default = vanilla;
        }
        else if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < min || value > max)
        {
            BalanceDefaults.Warn(name, "has an invalid value '" + text + "'");
            Default = vanilla;
        }
        else
        {
            Default = value;
        }
        Value = Default;
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

    // The default comes from defaults.txt (BalanceDefaults); vanilla if it's missing or invalid there.
    public BalanceCurve(string name, string description, string vanilla = "0:100")
    {
        Name = name;
        Description = description;
        DisplayVanilla = vanilla;
        string text = BalanceDefaults.Get(name);
        if (text == null || Parse(text, out _) == null)
        {
            BalanceDefaults.Warn(name, text == null ? "is missing" : "is invalid");
            text = vanilla;
        }
        DisplayDefault = text;
        Reset();
    }

    // As a plain list when the points are at 0, 1, 2 ... (the curve editor's format), otherwise as x:percent pairs.
    public string DisplayValue
    {
        get
        {
            var points = _points;
            bool plain = points.Length > 1 && points.Select((p, i) => p.Speed == i).All(b => b);
            return plain ? string.Join(" ", points.Select(p => BalanceSetting.Format(p.Percent)))
                : string.Join(" ", points.Select(p => BalanceSetting.Format(p.Speed) + ":" + BalanceSetting.Format(p.Percent)));
        }
    }

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

    // Either x:percent pairs ("0:72 4:63 20:46"), or a plain list of percentages, one per step of 1 starting at 0
    // ("72 70 69 ..." = 0, 1, 2 ... m/s), as scripts/curve-editor.py writes it.
    private (float, float)[] Parse(string text, out string error)
    {
        var points = new List<(float, float)>();
        string[] tokens = text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        bool plainList = tokens.Length > 0 && tokens.All(t => !t.Contains(':'));
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (plainList)
            {
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0f || value > 500f)
                {
                    error = "Invalid percentage '" + token + "' for " + Name + ": a plain list is one percentage per step of 1 from 0, e.g. 72 70 69.";
                    return null;
                }
                points.Add((i, value));
                continue;
            }
            string[] pair = token.Split(':');
            if (pair.Length != 2
                || !float.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)
                || !float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                || speed < 0f || percent < 0f || percent > 500f)
            {
                error = "Invalid point '" + token + "' for " + Name + ": use x:percent pairs (e.g. 0:72 4:63 20:46), or a plain list of percentages, one per step of 1 from 0.";
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

    private readonly float _max;
    private readonly string _valueName, _example;

    // Values are 0.._max; valueName and example are for the error message ("multiplier", "mp_aserai_heavy_lance:0.85").
    // The default comes from defaults.txt (BalanceDefaults); none if it's missing or invalid there.
    public BalanceTable(string name, string description, float max = 5f, string valueName = "multiplier",
        string example = "mp_aserai_heavy_lance:0.85")
    {
        Name = name;
        Description = description;
        _max = max;
        _valueName = valueName;
        _example = example;
        string text = BalanceDefaults.Get(name);
        if (text == null || Parse(text, out _) == null)
        {
            BalanceDefaults.Warn(name, text == null ? "is missing" : "is invalid");
            text = "none";
        }
        DisplayDefault = text;
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

    public IReadOnlyDictionary<string, float> Entries => _entries;

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
                || value < 0f || value > _max)
            {
                error = "Invalid entry '" + token + "' for " + Name + ": use item_id:" + _valueName + " pairs (" + _valueName + " 0 to "
                    + BalanceSetting.Format(_max) + "), e.g. " + _example + ", or none.";
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
        0f, 5f);

    // Melee pierce thrusts (BalancedStrikeMagnitudeModel): vanilla's strength for the hit (perks included) times a
    // percentage for the closing speed along the thrust, from these curves (lances: weapons with a couch usage);
    // vanilla's armor and body part handling then apply as usual. 100% at low speed, falling with speed: vanilla's
    // speed term grows with the square of the speed times the weapon's weight, so fast hits gain less than in vanilla.
    public static readonly BalanceSetting ThrustModel = new BalanceSetting("thrust_model",
        "1 = melee pierce thrusts do a % of vanilla by closing speed (thrust_curve_spear, thrust_curve_lance, thrust_curve_foot); 0 = vanilla",
        0f, 1f, vanilla: 0f);

    public static readonly BalanceCurve ThrustCurveSpear = new BalanceCurve("thrust_curve_spear",
        "mounted melee pierce thrusts with non-lances: % of vanilla by closing speed along the thrust (one value per m/s from 0, or speed:percent pairs)");

    public static readonly BalanceCurve ThrustCurveLance = new BalanceCurve("thrust_curve_lance",
        "mounted melee pierce thrusts with lances (weapons that can couch): % of vanilla by closing speed along the thrust (one value per m/s from 0, or speed:percent pairs)");

    public static readonly BalanceCurve ThrustCurveFoot = new BalanceCurve("thrust_curve_foot",
        "melee pierce thrusts by attackers on foot (infantry, archers in melee, dismounted cavalry): % of vanilla by closing speed along the thrust (one value per m/s from 0, or speed:percent pairs)");

    public static readonly BalanceSetting ThrustHorse = new BalanceSetting("thrust_horse",
        "extra multiplier on melee pierce thrusts on horses, on top of the curves (capped at vanilla)",
        0f, 5f);

    public static readonly BalanceTable WeaponMultipliers = new BalanceTable("weapon_mult",
        "extra multiplier on melee pierce thrusts per weapon item id (item_id:multiplier pairs, or none)");

    // Item weights (ItemWeightOverrides): what carrying and holding items costs in movement speed. Applies to characters
    // spawned after the change; clients keep the vanilla weight for their own movement prediction.
    public static readonly BalanceTable ItemWeights = new BalanceTable("item_weight",
        "item weight in kg per item id (item_id:kg pairs, or none); weapons and shields slow their carrier, armor doesn't in multiplayer",
        max: 100f, valueName: "kg", example: "mp_curved_round_shield:1");

    // Weapons' listed thrust damage (ThrustDamageOverrides): scales the usage's thrust damage factor, so damage at every
    // speed. Applies to the next hit.
    public static readonly BalanceTable ThrustDamage = new BalanceTable("thrust_damage",
        "listed thrust damage per weapon item id (item_id:damage pairs, or none), first thrusting usage; its other usages scale alike",
        max: 200f, valueName: "damage", example: "mp_battania_long_spear:31");

    // Horse health (HorseHealthOverrides): total health per horse item. Applies to horses spawned after the change.
    public static readonly BalanceTable HorseHealth = new BalanceTable("horse_hp",
        "total health per horse item id (item_id:hp pairs, or none); vanilla is 200 plus the horse's extra health",
        max: 2000f, valueName: "hp",
        example: "mp_khuzait_horse:215");

    // Khuzait light cavalry's Small Shield perk (SmallShieldPerk): 2 = a second shield in the free Item3 slot.
    // Applies from the next spawn.
    public static readonly BalanceSetting SmallShields = new BalanceSetting("small_shields",
        "shields given by the Khuzait light cavalry's Small Shield perk (1 = vanilla, 2 = a second shield in Item3)",
        1f, 2f);

    // Missiles and the pierce soft cap (BalancedApplyDamageModel).
    public static readonly BalanceSetting PierceMissile = new BalanceSetting("pierce_missile",
        "multiplies final pierce damage from arrows, bolts and thrown weapons",
        0f, 5f);

    // Missile headshots (BalancedApplyDamageModel.GetDamageMultiplierForBodyPart): head and neck pierce multipliers.
    // Apply to missiles fired after the change.
    public static readonly BalanceSetting MissileHead = new BalanceSetting("missile_head",
        "pierce missile damage multiplier on players' head and neck",
        0f, 5f, vanilla: 2f);

    public static readonly BalanceSetting MissileHeadHorse = new BalanceSetting("missile_head_horse",
        "pierce missile damage multiplier on horses' head and neck",
        0f, 5f, vanilla: 1.2f);

    // Infantry perks' bonus damage against horses only with polearms (MountDamagePerks).
    public static readonly BalanceSetting MountDamagePolearm = new BalanceSetting("mount_damage_polearm",
        "1 = infantry perks' bonus against horses (Cavalry Breaker, Spearman, Farmer) only applies with polearms; 0 = any weapon (vanilla)",
        0f, 1f, vanilla: 0f);

    public static readonly BalanceSetting PierceKnee = new BalanceSetting("pierce_knee",
        "pierce damage to players above this is compressed smoothly toward pierce_cap",
        0f, 1000f, vanilla: 1000f);

    public static readonly BalanceSetting PierceCap = new BalanceSetting("pierce_cap",
        "pierce damage to players never exceeds this",
        0f, 1000f, vanilla: 1000f);

    // Couched lances and braced polearms (BalancedApplyDamageModel).
    public static readonly BalanceSetting Couch = new BalanceSetting("couch",
        "multiplies final damage from couched lances and braced polearms, before the couch curve",
        0f, 5f);

    public static readonly BalanceSetting CouchKnee = new BalanceSetting("couch_knee",
        "couch/brace damage above this is compressed smoothly toward couch_cap (0 = curve starts at 0)",
        0f, 1000f, vanilla: 1000f);

    public static readonly BalanceSetting CouchCap = new BalanceSetting("couch_cap",
        "couch/brace damage never exceeds this",
        0f, 1000f, vanilla: 1000f);

    // Horse top speed (BalancedAgentStatModel); changes apply to every horse immediately.
    public static readonly BalanceSetting HorseSpeed = new BalanceSetting("horse_speed",
        "multiplies horse top speed",
        0.1f, 2f);

    public static readonly BalanceSetting HorseKnee = new BalanceSetting("horse_knee",
        "horse top speeds (m/s, after horse_speed) above this are compressed smoothly toward horse_cap",
        0f, 1000f, vanilla: 1000f);

    public static readonly BalanceSetting HorseCap = new BalanceSetting("horse_cap",
        "horse top speed (m/s) never exceeds this; keep well above 9, the minimum speed to hold a couched lance",
        0f, 1000f, vanilla: 1000f);

    // Horse handling (BalancedAgentStatModel); changes apply to every horse immediately.
    public static readonly BalanceSetting HorseManeuver = new BalanceSetting("horse_maneuver",
        "multiplies horse maneuver (turning)",
        0.1f, 3f);

    public static readonly BalanceSetting HorseAccelTime = new BalanceSetting("horse_accel_time",
        "multiplies the horse's TopSpeedReachDuration stat (vanilla 0.15-0.7); by its name higher = slower to reach top speed, check the [MPB-ACCEL] log",
        0.1f, 5f);

    // Engine (native) parameters, overridden live on the server (NativeParameters). Clients keep their own
    // values, so watch for snapping.
    public static readonly BalanceSetting HorseTurnKeepVelocity = new BalanceSetting("horse_turn_keep_speed",
        "native quadrupedal_delta_rotation_threshold_for_keeping_local_velocity: how sharp a horse can turn before losing its speed",
        0f, 10f, vanilla: 0.6f, nativeParameter: "quadrupedal_delta_rotation_threshold_for_keeping_local_velocity");

    public static readonly BalanceSetting CouchMinSpeed = new BalanceSetting("couch_min_speed",
        "native mounted_passive_action_minimum_speed: minimum horse speed (m/s) to couch a lance",
        0f, 50f, vanilla: 9f, nativeParameter: "mounted_passive_action_minimum_speed");

    // Horse charges ("bumps", HorseCharge). The engine also requires a running gait, so this only raises the bar.
    public static readonly BalanceSetting BumpSpeed = new BalanceSetting("bump_speed",
        "minimum relative speed (m/s) for a horse to bump someone (camels keep their +16%); applies to horses spawned after the change",
        0f, 50f, vanilla: 4.3f);

    public static readonly IReadOnlyList<ISetting> All = new ISetting[]
    {
        SwingSpeedBonus,
        ThrustModel, ThrustCurveSpear, ThrustCurveLance, ThrustCurveFoot, ThrustHorse, WeaponMultipliers,
        PierceMissile, MissileHead, MissileHeadHorse, MountDamagePolearm, PierceKnee, PierceCap,
        Couch, CouchKnee, CouchCap,
        HorseSpeed, HorseKnee, HorseCap, HorseManeuver, HorseAccelTime, HorseTurnKeepVelocity, CouchMinSpeed,
        BumpSpeed, ItemWeights, ThrustDamage, HorseHealth, SmallShields,
    };

    // Re-applies settings that are baked into agents rather than read on every hit.
    public static void ApplyLive()
    {
        BalancedAgentStatModel.RefreshAllMounts();
        HorseCharge.ApplySpeedLimit();
        NativeParameters.Apply();
        ItemWeightOverrides.Apply();
        ThrustDamageOverrides.Apply();
        HorseHealthOverrides.Apply();
        SmallShieldPerk.Apply();
        HitLog.SettingsChanged(); // later hits are logged under a new settings number
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
