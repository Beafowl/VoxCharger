using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VoxCharger
{
    // Window state that survives a restart: the game folder and the last selected mix view.
    // Stored as key=value lines in %AppData%\VoxCharger\ui.txt.
    public static class UiSettings
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxCharger", "ui.txt");

        private static Dictionary<string, string> _values;

        public static string GamePath
        {
            get => Get("gamePath");
            set => Set("gamePath", value);
        }

        // "*all", "*mods" or a mix name
        public static string LastView
        {
            get => Get("lastView");
            set => Set("lastView", value);
        }

        private static string Get(string key)
        {
            Load();
            return _values.TryGetValue(key, out var value) ? value : null;
        }

        private static void Set(string key, string value)
        {
            Load();
            if (string.IsNullOrEmpty(value))
                _values.Remove(key);
            else
                _values[key] = value.Replace("\r", "").Replace("\n", "");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, _values.Select(kv => $"{kv.Key}={kv.Value}"), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Not being able to remember the window state is not worth an error
            }
        }

        private static void Load()
        {
            if (_values != null)
                return;

            _values = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(FilePath))
                    return;

                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                        _values[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
