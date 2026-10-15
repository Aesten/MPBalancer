using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MPBalancer;

// The settings' default values, from defaults.txt, built into the DLL as an embedded resource (MPBalancer.defaults.txt):
// "name = value" lines, # for comments. Read once, the first time a setting is created.
internal static class BalanceDefaults
{
    private static Dictionary<string, string> _values;

    // The default text for a setting, or null if defaults.txt doesn't have it (the setting then uses its vanilla value).
    public static string Get(string name)
    {
        if (_values == null)
        {
            _values = Load();
        }
        return _values.TryGetValue(name, out string value) ? value : null;
    }

    // Called by a setting whose line is missing or invalid.
    public static void Warn(string name, string problem)
    {
        MPBalancerSubModule.Log("defaults.txt: " + name + " " + problem + "; using the vanilla value.");
    }

    private static Dictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MPBalancer.defaults.txt");
        if (stream == null)
        {
            MPBalancerSubModule.Log("defaults.txt is missing from the build; every setting starts at its vanilla value.");
            return values;
        }
        using var reader = new StreamReader(stream);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            int equals = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith("#") || equals <= 0)
            {
                continue;
            }
            values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
        }
        return values;
    }
}
