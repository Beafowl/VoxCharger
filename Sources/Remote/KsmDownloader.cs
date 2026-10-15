using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;

namespace VoxCharger
{
    public class KsmDownloader : IDisposable
    {
        private readonly WebClient _http;

        public KsmDownloader()
        {
            _http = new WebClient();
            _http.Headers[HttpRequestHeader.UserAgent] = "VoxCharger";
        }

        public string DownloadAndExtract(string ksmUrl)
        {
            return DownloadAndExtractInternal(NormalizeDownloadUrl(ksmUrl));
        }

        // Used when the URL is already a direct asset link (e.g. Asphyxia's
        // nauticaExportList stores ksm.dev CDN URLs that point straight at
        // the chart zip — appending /download to those would 404).
        public string DownloadAndExtractDirect(string zipUrl)
        {
            return DownloadAndExtractInternal(zipUrl);
        }

        private string DownloadAndExtractInternal(string url)
        {
            byte[] zipData = _http.DownloadData(EncodeUrlPath(url));
            string tempDir = Path.Combine(Path.GetTempPath(), "VoxCharger_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "chart.zip");
            File.WriteAllBytes(zipPath, zipData);

            ExtractWithDetectedEncoding(zipPath, tempDir);
            File.Delete(zipPath);

            return tempDir;
        }

        // Zips from ksm.dev often store non-ASCII entry names without the UTF-8 flag, in
        // whatever codepage the uploader's OS used (Shift-JIS, EUC-JP, GBK, ...).
        // ZipFile.ExtractToDirectory decodes those with the system codepage, which garbles
        // the names so the `m=` music file in the .ksh is never found.
        // Read the names through IBM437 (maps every byte 1:1, so the raw bytes are recoverable),
        // then pick the encoding whose decoded names match the most files the .ksh charts
        // reference. Several of these codepages decode each other's bytes without errors,
        // so "decodes cleanly" alone can't tell them apart.
        private static readonly Encoding RawNameEncoding = Encoding.GetEncoding(437);
        private static readonly Encoding StrictRawNameEncoding =
            Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private static readonly Encoding[] NameEncodings = new[] { "utf-8", "shift_jis", "euc-jp", "gbk", "big5", "ks_c_5601-1987" }
            .Select(name => Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback))
            .ToArray();

        // Same rule as Ksh.Parse: UTF-8 when the file has a BOM, Shift-JIS otherwise
        private static readonly Encoding KshEncoding = Encoding.GetEncoding("Shift_JIS");

        private static void ExtractWithDetectedEncoding(string zipPath, string targetDir)
        {
            string root = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, RawNameEncoding))
            {
                var entries = archive.Entries.ToList();
                var names   = DecodeNames(entries);

                for (int i = 0; i < entries.Count; i++)
                {
                    string path = Path.GetFullPath(Path.Combine(root, names[i]));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Zip entry escapes the extraction folder: {names[i]}");

                    if (names[i].EndsWith("/") || names[i].EndsWith("\\"))
                    {
                        Directory.CreateDirectory(path);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entries[i].ExtractToFile(path, true);
                }
            }
        }

        private static List<string> DecodeNames(List<ZipArchiveEntry> entries)
        {
            var rawNames = entries.Select(e => e.FullName).ToList();

            // Entries flagged as UTF-8 are already decoded correctly and contain characters
            // outside IBM437, leave those alone and only re-decode the rest
            var raw = rawNames.Select(n => IsRawName(n) ? RawNameEncoding.GetBytes(n) : null).ToList();
            if (raw.All(b => b == null || b.All(c => c < 0x80)))
                return rawNames;

            var referenced = GetReferencedFileNames(entries);

            List<string> best = null;
            int bestScore = -1;
            foreach (var encoding in NameEncodings)
            {
                var decoded = new List<string>(rawNames.Count);
                try
                {
                    for (int i = 0; i < rawNames.Count; i++)
                        decoded.Add(raw[i] == null ? rawNames[i] : encoding.GetString(raw[i]));
                }
                catch (DecoderFallbackException)
                {
                    continue; // Not this encoding
                }

                // Earlier encodings win ties, so with nothing to match the first clean decode is used
                int score = decoded.Count(n => referenced.Contains(FileNameOf(n)));
                if (score > bestScore)
                {
                    best      = decoded;
                    bestScore = score;
                }
            }

            return best ?? rawNames;
        }

        // Music and jacket file names the .ksh charts in the archive point at
        private static HashSet<string> GetReferencedFileNames(List<ZipArchiveEntry> entries)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Where(e => e.FullName.EndsWith(".ksh", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using (var reader = new StreamReader(entry.Open(), KshEncoding, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null && line != "--")
                        {
                            if (line.StartsWith("m=") || line.StartsWith("jacket="))
                            {
                                foreach (var value in line.Substring(line.IndexOf('=') + 1).Split(';'))
                                {
                                    if (!string.IsNullOrWhiteSpace(value))
                                        names.Add(FileNameOf(value.Trim()));
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Unreadable chart, the importer reports it later
                }
            }

            return names;
        }

        // Path.GetFileName throws on characters that are invalid in paths, which garbled names can contain
        private static string FileNameOf(string path)
        {
            return path.Substring(path.LastIndexOfAny(new[] { '/', '\\' }) + 1);
        }

        private static bool IsRawName(string name)
        {
            try
            {
                StrictRawNameEncoding.GetBytes(name);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        // ksm.dev (and the asphyxia export it produces) stores URLs with raw,
        // human-readable filenames containing characters that aren't safe in
        // a URL path: `#` (fragment delimiter — silently truncates the URL),
        // spaces, `$`, `♥`, multibyte chars like `µ`/`♔`, etc.
        // Re-encode the filename segment (the part after the last `/`)
        // before sending it to the CDN. Earlier segments and the scheme are
        // already valid URI components so we leave them untouched.
        private static string EncodeUrlPath(string url)
        {
            int slash = url.LastIndexOf('/');
            if (slash < 0 || slash == url.Length - 1)
                return url;
            string head = url.Substring(0, slash + 1);
            string name = url.Substring(slash + 1);

            // If the filename already contains percent-escapes, assume the
            // caller already encoded it and pass through unchanged. Avoids
            // double-encoding (%20 → %2520).
            if (name.IndexOf('%') >= 0)
                return url;

            return head + Uri.EscapeDataString(name);
        }

        public static string FindKshFile(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*.ksh", SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        public static string FindKshDirectory(string directory)
        {
            string kshFile = FindKshFile(directory);
            return kshFile != null ? Path.GetDirectoryName(kshFile) : null;
        }

        public static void Cleanup(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        private static string NormalizeDownloadUrl(string url)
        {
            url = url.TrimEnd('/');
            if (!url.EndsWith("/download"))
                url += "/download";
            return url;
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
