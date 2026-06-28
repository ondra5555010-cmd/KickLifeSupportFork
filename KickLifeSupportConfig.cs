using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KickLifeSupport
{
    internal static class KickLifeSupportConfig
    {
        static ConfigNode settings;
        static readonly Dictionary<string, double> values =
            new Dictionary<string, double>();

        internal static double GetDouble(string key, double fallback)
        {
            if (values.TryGetValue(key, out double cached)) return cached;

            ConfigNode node = GetSettings();
            string raw = node != null ? node.GetValue(key) : null;
            if (raw != null &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                values[key] = value;
                return value;
            }

            Debug.LogWarning(
                $"[KickLifeSupport] Missing or invalid KICKLS_SETTINGS value '{key}'; using {fallback.ToString(CultureInfo.InvariantCulture)}.");
            values[key] = fallback;
            return fallback;
        }

        internal static float GetFloat(string key, float fallback)
        {
            return (float)GetDouble(key, fallback);
        }

        static ConfigNode GetSettings()
        {
            if (settings != null) return settings;

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("KICKLS_SETTINGS");
            if (nodes.Length > 0) settings = nodes[0];
            return settings;
        }
    }
}
