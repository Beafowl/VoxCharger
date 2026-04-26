using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace VoxCharger
{
    // Portable export/import format for a mix's ksm.dev source URLs. Thinks of a
    // mix as "a list of URLs to download charts from" — the actual asset files
    // live at ksm.dev, so the manifest is small (a few KB) and reproducible on
    // any VoxCharger install with network access.
    //
    // JSON shape (kept narrow on purpose; the importer ignores unknown fields
    // so we can extend without breaking older manifests):
    //   {
    //     "format": "voxcharger.mix.url-list",
    //     "version": 1,
    //     "exported_at": "2026-04-26T19:30:00Z",
    //     "mix_name": "asphyxia_custom",
    //     "charts": [
    //       { "id": 2800, "ascii": "qzkago", "title": "...", "artist": "...",
    //         "url": "https://ksm.dev/songs/<uuid>" }
    //     ]
    //   }
    //
    // Charts without a stored URL are skipped on export — there's nothing the
    // importer could do with them anyway. Title/artist/ascii are informational
    // only (used to make the manifest human-readable and to surface progress
    // status during import).
    public static class MixManifest
    {
        public const string FormatTag = "voxcharger.mix.url-list";
        public const int CurrentVersion = 1;

        public class Entry
        {
            public int    id      { get; set; }
            public string ascii   { get; set; }
            public string title   { get; set; }
            public string artist  { get; set; }
            public string url     { get; set; }
        }

        public class Manifest
        {
            public string      format      { get; set; }
            public int         version     { get; set; }
            public string      exported_at { get; set; }
            public string      mix_name    { get; set; }
            public List<Entry> charts      { get; set; }
        }

        public static Manifest Build(string mixName, IEnumerable<VoxHeader> headers, Dictionary<int, string> urlMap)
        {
            var charts = new List<Entry>();
            foreach (var h in headers.OrderBy(h => h.Id))
            {
                if (!urlMap.TryGetValue(h.Id, out string url)) continue;
                if (string.IsNullOrWhiteSpace(url)) continue;

                charts.Add(new Entry
                {
                    id     = h.Id,
                    ascii  = h.Ascii,
                    title  = h.Title,
                    artist = h.Artist,
                    url    = url,
                });
            }

            return new Manifest
            {
                format      = FormatTag,
                version     = CurrentVersion,
                exported_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                mix_name    = mixName,
                charts      = charts,
            };
        }

        public static void Save(string fileName, Manifest manifest)
        {
            var ser = new JavaScriptSerializer();
            // JavaScriptSerializer escapes non-ASCII to \uXXXX which is fine
            // here — every JSON parser handles it, and titles can include
            // shift_jis characters that we don't want to depend on filesystem
            // text encoding for.
            string json = ser.Serialize(manifest);
            File.WriteAllText(fileName, PrettyPrint(json), Encoding.UTF8);
        }

        public static Manifest Load(string fileName)
        {
            string text = File.ReadAllText(fileName, Encoding.UTF8);
            var ser = new JavaScriptSerializer();
            // Allow large manifests — a server with hundreds of charts is
            // a real use case and the default 4MB cap can bite.
            ser.MaxJsonLength = int.MaxValue;
            return ser.Deserialize<Manifest>(text);
        }

        // Small pretty-printer that turns the single-line JavaScriptSerializer
        // output into something readable. We don't need full JSON formatting
        // since the schema is fixed — just newlines between top-level fields
        // and per-chart entries inside the `charts` array.
        private static string PrettyPrint(string compact)
        {
            var sb = new StringBuilder(compact.Length + 256);
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char c = compact[i];
                if (c == '"' && (i == 0 || compact[i - 1] != '\\'))
                    inString = !inString;
                if (inString)
                {
                    sb.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '{':
                    case '[':
                        sb.Append(c);
                        depth++;
                        sb.Append('\n').Append(new string(' ', depth * 2));
                        break;
                    case '}':
                    case ']':
                        depth--;
                        sb.Append('\n').Append(new string(' ', depth * 2)).Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        sb.Append('\n').Append(new string(' ', depth * 2));
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString() + "\n";
        }
    }
}
