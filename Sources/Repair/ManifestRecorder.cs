using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace VoxCharger
{
    // Called from inside Ksh.Exporter.Action after every successful export.
    // Walks the just-written outputs + reachable sources, computes SHA256s,
    // updates the per-mix manifest. Treats every failure as best-effort —
    // a broken manifest must never break an import.
    internal static class ManifestRecorder
    {
        // Imports export several charts in parallel, so loading, updating and saving the
        // manifest is serialized. During a batch the manifest stays in memory and is written
        // every BatchSaveInterval charts and at the end, instead of rewriting the whole
        // (growing) file once per chart.
        private const int BatchSaveInterval = 50;
        private static readonly object _lock = new object();
        private static Manifest _batch;
        private static int _batchDepth;
        private static int _batchPending;

        public static void BeginBatch()
        {
            lock (_lock)
                _batchDepth++;
        }

        public static void EndBatch()
        {
            lock (_lock)
            {
                if (_batchDepth == 0 || --_batchDepth > 0)
                    return;

                if (_batch != null && _batchPending > 0)
                    TrySave(_batch);

                _batch        = null;
                _batchPending = 0;
            }
        }

        public static void RecordExport(VoxHeader header, Dictionary<Difficulty, ChartInfo> charts, AudioImportOptions opt)
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
                return;

            // Hashing reads every source and output file, keep it outside the lock
            var sourceFiles = HashSourceFiles(charts, out string sourceDir);
            var outputs     = HashOutputs(header, charts, opt);

            Update(manifest =>
            {
                var entry = manifest.GetOrCreate(header.Id);

                entry.Id                 = header.Id;
                entry.Ascii              = header.Ascii ?? string.Empty;
                entry.CodeName           = header.CodeName ?? string.Empty;
                entry.VoxchargerVersion  = Manifest.CurrentVoxchargerVersion;
                entry.ExportedAt         = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                entry.AudioFormat        = (opt?.Format ?? AudioFormat.Iidx).ToString();

                entry.SourceFiles = sourceFiles;
                entry.SourceDir   = sourceDir ?? string.Empty;
                entry.Outputs     = outputs;
            });
        }

        // Re-records a song's thumbnails after they were rebuilt or moved, leaving the other outputs alone
        public static void UpdateThumbnails(VoxHeader header)
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
                return;

            var thumbnails = new Dictionary<string, string>();
            foreach (var diff in header.Levels.Keys)
                AddRelative(thumbnails, AssetManager.MixPath, $"{AssetManager.GetThumbnailJacketPath(header, diff)}.png");

            Update(manifest =>
            {
                if (!manifest.TryGet(header.Id, out var entry))
                    return;

                foreach (string key in entry.Outputs.Keys.Where(k => k.StartsWith("graphics/", StringComparison.OrdinalIgnoreCase)).ToList())
                    entry.Outputs.Remove(key);
                foreach (var thumbnail in thumbnails)
                    entry.Outputs[thumbnail.Key] = thumbnail.Value;
            });
        }

        // Runs `update` on the manifest under the lock and saves it, immediately or at the batch interval
        private static void Update(Action<Manifest> update)
        {
            lock (_lock)
            {
                var manifest = _batchDepth > 0 ? (_batch = _batch ?? Manifest.Load(AssetManager.MixPath)) : Manifest.Load(AssetManager.MixPath);
                update(manifest);

                if (_batchDepth == 0)
                {
                    manifest.Save(AssetManager.MixPath);
                }
                else if (++_batchPending >= BatchSaveInterval)
                {
                    TrySave(manifest);
                    _batchPending = 0;
                }
            }
        }

        private static void TrySave(Manifest manifest)
        {
            try
            {
                manifest.Save(AssetManager.MixPath);
            }
            catch (Exception)
            {
                // Best effort, like every other manifest write
            }
        }

        private static Dictionary<string, string> HashSourceFiles(Dictionary<Difficulty, ChartInfo> charts, out string sourceDir)
        {
            sourceDir = null;
            var hashes = new Dictionary<string, string>();

            // Collect unique KSH source paths and their parent directory. We
            // assume all sibling difficulties live in the same folder, which
            // is how KSM organizes charts and how Ksh.Exporter.GetCharts finds
            // them.
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in charts.Values)
            {
                if (string.IsNullOrEmpty(info.FileName) || !File.Exists(info.FileName)) continue;
                if (!seenPaths.Add(info.FileName)) continue;

                if (sourceDir == null)
                    sourceDir = Path.GetDirectoryName(info.FileName);

                AddHash(hashes, info.FileName, Path.GetFileName(info.FileName));
            }

            if (sourceDir == null)
                return hashes;

            // Hash the original audio source and the jacket(s) referenced by
            // each KSH. We resolve them against sourceDir using each Source.MusicFileName /
            // JacketFileName, since the values pulled from the KSH are relative.
            var seenSidecars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in charts.Values)
            {
                if (info.Source == null) continue;

                if (!string.IsNullOrEmpty(info.Source.MusicFileName))
                {
                    string audio = Path.Combine(sourceDir, info.Source.MusicFileName);
                    if (File.Exists(audio) && seenSidecars.Add(audio))
                        AddHash(hashes, audio, Path.GetFileName(audio));
                }

                if (!string.IsNullOrEmpty(info.Source.JacketFileName))
                {
                    string jacket = Path.Combine(sourceDir, info.Source.JacketFileName);
                    if (File.Exists(jacket) && seenSidecars.Add(jacket))
                        AddHash(hashes, jacket, Path.GetFileName(jacket));
                }
            }

            return hashes;
        }

        private static Dictionary<string, string> HashOutputs(VoxHeader header, Dictionary<Difficulty, ChartInfo> charts, AudioImportOptions opt)
        {
            var hashes = new Dictionary<string, string>();
            string mixRoot = AssetManager.MixPath;
            var format     = opt?.Format ?? AudioFormat.Iidx;

            // VOX charts (per difficulty)
            foreach (var diff in header.Levels.Keys)
            {
                AddRelative(hashes, mixRoot, AssetManager.GetVoxPath(header, diff));
            }

            // Audio: per-difficulty if charts use distinct music files, else
            // a single shared file. Mirroring the export logic above.
            string sharedMusic = charts.Values.Count > 0 ? charts.Values.First().MusicFileName : null;
            bool   uniqueAudio = false;
            foreach (var c in charts.Values)
                if (c.MusicFileName != sharedMusic) { uniqueAudio = true; break; }

            if (uniqueAudio)
            {
                foreach (var diff in header.Levels.Keys)
                {
                    AddRelative(hashes, mixRoot, AssetManager.GetAudioPath(header, diff, format));
                }
                AddRelative(hashes, mixRoot, AssetManager.GetAudioPath(header, format, true)); // shared preview
            }
            else
            {
                AddRelative(hashes, mixRoot, AssetManager.GetAudioPath(header, format));
                AddRelative(hashes, mixRoot, AssetManager.GetAudioPath(header, format, true));
            }

            // Jackets (per difficulty: 4 sizes — base/_b/_s plus thumbnail)
            foreach (var diff in header.Levels.Keys)
            {
                string baseJk = AssetManager.GetJacketPath(header, diff);
                string thumb  = AssetManager.GetThumbnailJacketPath(header, diff);
                AddRelative(hashes, mixRoot, $"{baseJk}.png");
                AddRelative(hashes, mixRoot, $"{baseJk}_b.png");
                AddRelative(hashes, mixRoot, $"{baseJk}_s.png");
                AddRelative(hashes, mixRoot, $"{thumb}.png");
            }

            return hashes;
        }

        private static void AddHash(Dictionary<string, string> hashes, string fullPath, string key)
        {
            string h = ManifestHash.FileSha256(fullPath);
            if (h != null) hashes[key] = h;
        }

        private static void AddRelative(Dictionary<string, string> hashes, string root, string fullPath)
        {
            if (!File.Exists(fullPath)) return;
            string rel = MakeRelative(root, fullPath);
            string h   = ManifestHash.FileSha256(fullPath);
            if (h != null) hashes[rel] = h;
        }

        private static string MakeRelative(string root, string fullPath)
        {
            // Path.GetRelativePath isn't on .NET Framework. Implement a minimal
            // version that handles trailing slashes and case-insensitive prefix.
            string normRoot = root.Replace('/', '\\').TrimEnd('\\') + "\\";
            string normFull = fullPath.Replace('/', '\\');
            if (normFull.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                return normFull.Substring(normRoot.Length).Replace('\\', '/');
            return Path.GetFileName(fullPath);
        }
    }
}
