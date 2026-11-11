using System;
using System.Collections.Generic;
using System.IO;

namespace PowerPane.Services
{
    /// <summary>
    /// Remembers expand/collapse choices so they survive pane rebuilds, profile switches, window
    /// switches, and restarts. Keyed by a stable id (section type, group folder, tree path) and stored
    /// as "key|0/1" lines in %APPDATA%\PowerPane\uistate.txt.
    /// </summary>
    public static class UiState
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, bool> Map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static string _file;
        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "PowerPane");
                _file = Path.Combine(dir, "uistate.txt");
                if (File.Exists(_file))
                {
                    foreach (var line in File.ReadAllLines(_file))
                    {
                        int bar = line.LastIndexOf('|');
                        if (bar <= 0) continue;
                        Map[MapFormerKey(line.Substring(0, bar), appData)] = line.Substring(bar + 1).Trim() == "1";
                    }
                }
            }
            catch (Exception ex) { Log.Error("uistate load", ex); }
        }

        /// <summary>Moves a key that names a folder inside QuickPane's old data folder to the same folder
        /// inside PowerPane's. A group's key is "group:" plus its expanded folder path, so a uistate.txt
        /// copied over from QuickPane would otherwise miss every group and open each one at its
        /// default. Section keys, and tree keys for folders anywhere else, come back unchanged.</summary>
        internal static string MapFormerKey(string key, string appData)
        {
            if (string.IsNullOrEmpty(key)) return key;
            foreach (var prefix in new[] { "group:", "tree:" })
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return key.Substring(0, prefix.Length) +
                        Models.AppSettings.MapFormerDataPath(key.Substring(prefix.Length), appData);
            }
            return key;
        }

        public static bool GetExpanded(string key, bool dflt = true)
        {
            if (string.IsNullOrEmpty(key)) return dflt;
            lock (Gate)
            {
                EnsureLoaded();
                bool v;
                return Map.TryGetValue(key, out v) ? v : dflt;
            }
        }

        public static void SetExpanded(string key, bool value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (Gate)
            {
                EnsureLoaded();
                Map[key] = value;
                Save();
            }
        }

        private static void Save()
        {
            try
            {
                if (_file == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                var lines = new List<string>();
                foreach (var kv in Map) lines.Add(kv.Key + "|" + (kv.Value ? "1" : "0"));
                File.WriteAllLines(_file, lines);
            }
            catch (Exception ex) { Log.Error("uistate save", ex); }
        }
    }
}
