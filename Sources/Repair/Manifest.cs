using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace VoxCharger
{
    // Per-mix sidecar manifest tracking, for each imported chart, the SHA256
    // hashes of every source file we read and every output file we wrote.
    // Used by MixRepairer to detect sync corruption ("the file got truncated
    // during transfer"), source drift ("the user updated their KSH"), and
    // VoxCharger version drift ("our radar coefficients changed since this
    // chart was exported"). Stored at <mix>/voxcharger_manifest.json.
    public class Manifest
    {
        public const int CurrentVersion = 1;
        public const string FileName = "voxcharger_manifest.json";

        public int Version { get; set; } = CurrentVersion;
        public Dictionary<string, ManifestEntry> Charts { get; set; } = new Dictionary<string, ManifestEntry>();

        public static string ResolvePath(string mixPath)
            => Path.Combine(mixPath, FileName);

        public static Manifest Load(string mixPath)
        {
            string path = ResolvePath(mixPath);
            if (!File.Exists(path))
                return new Manifest();

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonReader.Read(json);
            }
            catch (Exception)
            {
                // A corrupt manifest shouldn't block exports — start a fresh one.
                // The old file is left on disk for the user to inspect.
                return new Manifest();
            }
        }

        public void Save(string mixPath)
        {
            string path = ResolvePath(mixPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonWriter.Write(this), Encoding.UTF8);
        }

        public ManifestEntry GetOrCreate(int id)
        {
            string key = id.ToString(CultureInfo.InvariantCulture);
            if (!Charts.TryGetValue(key, out var entry))
                Charts[key] = entry = new ManifestEntry { Id = id };
            return entry;
        }

        public bool TryGet(int id, out ManifestEntry entry)
            => Charts.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out entry);

        public static string CurrentVoxchargerVersion
            => Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    public class ManifestEntry
    {
        public int Id { get; set; }
        public string Ascii { get; set; } = string.Empty;
        public string CodeName { get; set; } = string.Empty;
        public string VoxchargerVersion { get; set; } = string.Empty;
        public string ExportedAt { get; set; } = string.Empty;
        public string AudioFormat { get; set; } = "Iidx";

        // Absolute directory on the user's machine where the source KSH and
        // sidecar audio/jacket lived at export time. May not exist anymore.
        public string SourceDir { get; set; } = string.Empty;

        // Source files keyed by their *file name* (not path) within SourceDir
        // so a manifest written on machine A can be inspected on machine B
        // even if SourceDir doesn't resolve. Hashes are SHA256 hex.
        public Dictionary<string, string> SourceFiles { get; set; } = new Dictionary<string, string>();

        // Output files written by VoxCharger, keyed by path *relative to the
        // mix root* (e.g. "music/0001_albida_muryoku/0001_albida_muryoku_1n.vox").
        public Dictionary<string, string> Outputs { get; set; } = new Dictionary<string, string>();
    }

    public static class ManifestHash
    {
        public static string FileSha256(string path)
        {
            if (!File.Exists(path))
                return null;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    // Tiny hand-rolled JSON for the fixed Manifest schema. Avoids pulling in
    // Newtonsoft for one file. Only handles strings, ints, and string/string
    // dictionaries; keys are quoted; values are escaped per RFC 8259.
    internal static class JsonWriter
    {
        public static string Write(Manifest m)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(m.Version).Append(",\n");
            sb.Append("  \"voxcharger_version\": ").Append(EscapeString(Manifest.CurrentVoxchargerVersion)).Append(",\n");
            sb.Append("  \"charts\": {");

            bool firstChart = true;
            foreach (var kv in m.Charts)
            {
                if (!firstChart) sb.Append(",");
                firstChart = false;
                sb.Append("\n    ").Append(EscapeString(kv.Key)).Append(": {");
                WriteEntry(sb, kv.Value);
                sb.Append("\n    }");
            }

            sb.Append(firstChart ? "}\n" : "\n  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void WriteEntry(StringBuilder sb, ManifestEntry e)
        {
            sb.Append("\n      \"id\": ").Append(e.Id).Append(",");
            sb.Append("\n      \"ascii\": ").Append(EscapeString(e.Ascii)).Append(",");
            sb.Append("\n      \"code_name\": ").Append(EscapeString(e.CodeName)).Append(",");
            sb.Append("\n      \"voxcharger_version\": ").Append(EscapeString(e.VoxchargerVersion)).Append(",");
            sb.Append("\n      \"exported_at\": ").Append(EscapeString(e.ExportedAt)).Append(",");
            sb.Append("\n      \"audio_format\": ").Append(EscapeString(e.AudioFormat)).Append(",");
            sb.Append("\n      \"source_dir\": ").Append(EscapeString(e.SourceDir)).Append(",");
            sb.Append("\n      \"source_files\": ");
            WriteStringMap(sb, e.SourceFiles, "        ");
            sb.Append(",");
            sb.Append("\n      \"outputs\": ");
            WriteStringMap(sb, e.Outputs, "        ");
        }

        private static void WriteStringMap(StringBuilder sb, Dictionary<string, string> map, string indent)
        {
            sb.Append("{");
            bool first = true;
            foreach (var kv in map)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\n").Append(indent).Append(EscapeString(kv.Key)).Append(": ").Append(EscapeString(kv.Value));
            }
            sb.Append(first ? "}" : "\n" + indent.Substring(2) + "}");
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else          sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    // Minimal JSON reader for the Manifest schema. Tolerant of whitespace
    // and trailing commas; not a full RFC-compliant parser. If anything looks
    // off it falls back to returning a fresh empty manifest (caller treats
    // that as "no manifest").
    internal static class JsonReader
    {
        public static Manifest Read(string json)
        {
            var p = new Parser(json);
            p.SkipWs();
            p.Expect('{');

            var m = new Manifest { Charts = new Dictionary<string, ManifestEntry>() };
            while (true)
            {
                p.SkipWs();
                if (p.Peek() == '}') { p.Read(); break; }
                string key = p.ReadString();
                p.SkipWs(); p.Expect(':'); p.SkipWs();

                if (key == "version")               m.Version = p.ReadInt();
                else if (key == "voxcharger_version") p.ReadString();    // discard top-level version
                else if (key == "charts")           ReadCharts(p, m);
                else                                p.SkipValue();

                p.SkipWs();
                if (p.Peek() == ',') { p.Read(); continue; }
            }
            return m;
        }

        private static void ReadCharts(Parser p, Manifest m)
        {
            p.Expect('{');
            while (true)
            {
                p.SkipWs();
                if (p.Peek() == '}') { p.Read(); break; }
                string id = p.ReadString();
                p.SkipWs(); p.Expect(':'); p.SkipWs();
                var entry = ReadEntry(p);
                m.Charts[id] = entry;
                p.SkipWs();
                if (p.Peek() == ',') { p.Read(); continue; }
            }
        }

        private static ManifestEntry ReadEntry(Parser p)
        {
            var e = new ManifestEntry();
            p.Expect('{');
            while (true)
            {
                p.SkipWs();
                if (p.Peek() == '}') { p.Read(); break; }
                string key = p.ReadString();
                p.SkipWs(); p.Expect(':'); p.SkipWs();
                switch (key)
                {
                    case "id":                  e.Id = p.ReadInt();                          break;
                    case "ascii":               e.Ascii = p.ReadString();                    break;
                    case "code_name":           e.CodeName = p.ReadString();                 break;
                    case "voxcharger_version":  e.VoxchargerVersion = p.ReadString();        break;
                    case "exported_at":         e.ExportedAt = p.ReadString();               break;
                    case "audio_format":        e.AudioFormat = p.ReadString();              break;
                    case "source_dir":          e.SourceDir = p.ReadString();                break;
                    case "source_files":        e.SourceFiles = p.ReadStringMap();           break;
                    case "outputs":             e.Outputs = p.ReadStringMap();               break;
                    default:                    p.SkipValue();                                break;
                }
                p.SkipWs();
                if (p.Peek() == ',') { p.Read(); continue; }
            }
            return e;
        }

        private class Parser
        {
            private readonly string _src;
            private int _pos;

            public Parser(string src) { _src = src; _pos = 0; }

            public char Peek() => _pos < _src.Length ? _src[_pos] : '\0';
            public char Read() => _pos < _src.Length ? _src[_pos++] : '\0';
            public void Expect(char c)
            {
                char got = Read();
                if (got != c) throw new FormatException($"expected '{c}' at {_pos}, got '{got}'");
            }
            public void SkipWs()
            {
                while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t' || _src[_pos] == '\n' || _src[_pos] == '\r'))
                    _pos++;
            }

            public string ReadString()
            {
                SkipWs();
                if (Peek() == 'n') { _pos += 4; return null; } // null
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Read();
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        char esc = Read();
                        switch (esc)
                        {
                            case '"': sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_pos + 4 > _src.Length) throw new FormatException("bad \\u escape");
                                int cp = int.Parse(_src.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                _pos += 4;
                                sb.Append((char)cp);
                                break;
                            default: sb.Append(esc); break;
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            public int ReadInt()
            {
                SkipWs();
                int start = _pos;
                if (Peek() == '-') _pos++;
                while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9') _pos++;
                return int.Parse(_src.Substring(start, _pos - start), CultureInfo.InvariantCulture);
            }

            public Dictionary<string, string> ReadStringMap()
            {
                var d = new Dictionary<string, string>();
                SkipWs();
                Expect('{');
                while (true)
                {
                    SkipWs();
                    if (Peek() == '}') { Read(); break; }
                    string k = ReadString();
                    SkipWs(); Expect(':'); SkipWs();
                    string v = ReadString();
                    d[k] = v;
                    SkipWs();
                    if (Peek() == ',') { Read(); continue; }
                }
                return d;
            }

            // Skip over an unrecognized JSON value (so unknown keys don't break us).
            public void SkipValue()
            {
                SkipWs();
                char c = Peek();
                if (c == '"') { ReadString(); return; }
                if (c == '{' || c == '[')
                {
                    char open = Read();
                    char close = open == '{' ? '}' : ']';
                    int depth = 1;
                    while (_pos < _src.Length && depth > 0)
                    {
                        char ch = Read();
                        if (ch == '"') { _pos--; ReadString(); }
                        else if (ch == open)  depth++;
                        else if (ch == close) depth--;
                    }
                    return;
                }
                while (_pos < _src.Length && _src[_pos] != ',' && _src[_pos] != '}' && _src[_pos] != ']') _pos++;
            }
        }
    }
}
