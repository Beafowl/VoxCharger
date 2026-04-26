using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace VoxCharger
{
    // Persistent sidecar that remembers which ksm.dev URL each chart in a mix
    // was imported from. Lives at <mix>/voxcharger_urls.json, indexed by music
    // id so it survives chart re-numbering as long as the operator keeps the
    // same id.
    //
    // We use this for three things:
    //   1) Reconvert-from-URL — pull the latest version of the chart from the
    //      same ksm.dev source without making the user paste the URL again.
    //   2) Export Mix — bake the URL list into a portable JSON manifest so
    //      another VoxCharger install can rebuild the mix from scratch.
    //   3) Import Mix — read that manifest, download every chart, batch-import.
    //
    // JSON shape:
    //   { "2800": "https://ksm.dev/songs/<uuid>", "2801": "..." }
    //
    // Uses System.Web.Script.Serialization.JavaScriptSerializer because it's
    // already pulled in elsewhere — no new NuGet dependency just to handle a
    // tiny key/value file.
    public static class MixUrlStore
    {
        public const string FileName = "voxcharger_urls.json";

        public static string GetSidecarPath(string mixPath)
        {
            return Path.Combine(mixPath ?? string.Empty, FileName);
        }

        public static Dictionary<int, string> Load(string mixPath)
        {
            string path = GetSidecarPath(mixPath);
            var result = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(mixPath) || !File.Exists(path))
                return result;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return result;

                var ser = new JavaScriptSerializer();
                // Decode as Dictionary<string, object> first — JavaScriptSerializer
                // only natively handles string keys. Convert the keys to int
                // ourselves so the public API is type-safe.
                var raw = ser.Deserialize<Dictionary<string, object>>(text);
                if (raw == null) return result;
                foreach (var kv in raw)
                {
                    if (!int.TryParse(kv.Key, out int id)) continue;
                    string url = kv.Value as string;
                    if (!string.IsNullOrEmpty(url))
                        result[id] = url;
                }
            }
            catch
            {
                // Malformed sidecar shouldn't block the user from opening the
                // mix — fall back to "no URLs known".
            }
            return result;
        }

        public static void Save(string mixPath, Dictionary<int, string> map)
        {
            if (string.IsNullOrEmpty(mixPath) || !Directory.Exists(mixPath)) return;
            string path = GetSidecarPath(mixPath);

            // Re-key as string so JavaScriptSerializer is happy. Sort by id
            // for stable diffs when the file is checked into source control.
            var stringKeyed = new Dictionary<string, string>();
            foreach (var kv in map.OrderBy(k => k.Key))
                stringKeyed[kv.Key.ToString()] = kv.Value;

            var ser = new JavaScriptSerializer();
            string json = ser.Serialize(stringKeyed);
            // Pretty-print so the sidecar is human-readable; JavaScriptSerializer
            // emits a single line. Insert newlines between entries.
            json = json.Replace("\",\"", "\",\n  \"")
                       .Replace("{\"", "{\n  \"")
                       .Replace("\"}", "\"\n}");
            File.WriteAllText(path, json + "\n", Encoding.UTF8);
        }

        public static string GetUrl(string mixPath, int musicId)
        {
            var map = Load(mixPath);
            return map.TryGetValue(musicId, out string url) ? url : null;
        }

        public static void SetUrl(string mixPath, int musicId, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var map = Load(mixPath);
            map[musicId] = url;
            Save(mixPath, map);
        }

        public static void RemoveUrl(string mixPath, int musicId)
        {
            var map = Load(mixPath);
            if (map.Remove(musicId))
                Save(mixPath, map);
        }

        public static void RenameUrl(string mixPath, int oldId, int newId)
        {
            if (oldId == newId) return;
            var map = Load(mixPath);
            if (!map.TryGetValue(oldId, out string url)) return;
            map.Remove(oldId);
            map[newId] = url;
            Save(mixPath, map);
        }
    }
}
