using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoxCharger
{
    public enum ChartStatus
    {
        // All output files match their recorded hash and the source still
        // matches what was hashed at export time.
        Ok,
        // One or more output files are missing or have a different hash than
        // the manifest recorded at export time. Sync corruption typically
        // shows up here. Repair is fully automatic when source is reachable.
        OutputDrift,
        // Outputs are intact but the source files (KSH/audio/jacket) have
        // changed since this chart was last imported. Re-export to pick up
        // the new content.
        SourceDrift,
        // The source directory or specific source files no longer exist on
        // disk. Repair cannot be done without manual intervention; the user
        // needs to re-point or re-import.
        SourceMissing,
        // The chart is in music_db but has no manifest entry — it was imported
        // by an older VoxCharger build before the manifest existed. Verify-only;
        // repair would need an explicit source to be supplied.
        NotInManifest,
    }

    public class ChartReport
    {
        public int Id;
        public string CodeName;
        public string Title;
        public ChartStatus Status;
        public string Detail;          // human-readable specifics
        public ManifestEntry Entry;    // null if NotInManifest
        public VoxHeader Header;       // from music_db; null if missing
    }

    public static class MixRepairer
    {
        // Compute a status report for every chart in the currently-loaded mix.
        // Pure inspection — no files modified.
        public static List<ChartReport> Verify()
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
                throw new InvalidOperationException("No mix loaded");

            var manifest = Manifest.Load(AssetManager.MixPath);
            var reports  = new List<ChartReport>();

            foreach (var header in AssetManager.Headers)
            {
                manifest.TryGet(header.Id, out var entry);
                reports.Add(BuildReport(header, entry));
            }
            return reports;
        }

        private static ChartReport BuildReport(VoxHeader header, ManifestEntry entry)
        {
            var report = new ChartReport
            {
                Id       = header.Id,
                CodeName = header.CodeName,
                Title    = header.Title,
                Header   = header,
                Entry    = entry,
            };

            if (entry == null)
            {
                report.Status = ChartStatus.NotInManifest;
                report.Detail = "no manifest entry — imported before manifest support";
                return report;
            }

            // Output drift: any file in the manifest that's missing or has a different hash.
            var outputDriftDetails = new List<string>();
            foreach (var kv in entry.Outputs)
            {
                string fullPath = Path.Combine(AssetManager.MixPath, kv.Key);
                string actual   = ManifestHash.FileSha256(fullPath);
                if (actual == null)         outputDriftDetails.Add($"missing: {kv.Key}");
                else if (actual != kv.Value) outputDriftDetails.Add($"drift: {kv.Key}");
            }

            if (outputDriftDetails.Count > 0)
            {
                report.Status = ChartStatus.OutputDrift;
                report.Detail = string.Join("; ", outputDriftDetails.Take(3))
                              + (outputDriftDetails.Count > 3 ? $" (+{outputDriftDetails.Count - 3} more)" : "");
                return report;
            }

            // Source drift: compare each recorded source-file hash against current source.
            if (string.IsNullOrEmpty(entry.SourceDir) || !Directory.Exists(entry.SourceDir))
            {
                report.Status = ChartStatus.Ok; // outputs match; source unreachable but isn't drift
                report.Detail = "outputs ok; source directory unreachable";
                return report;
            }

            var sourceDriftDetails = new List<string>();
            foreach (var kv in entry.SourceFiles)
            {
                string fullPath = Path.Combine(entry.SourceDir, kv.Key);
                string actual   = ManifestHash.FileSha256(fullPath);
                if (actual == null)          sourceDriftDetails.Add($"missing source: {kv.Key}");
                else if (actual != kv.Value) sourceDriftDetails.Add($"changed source: {kv.Key}");
            }

            if (sourceDriftDetails.Count > 0)
            {
                bool anyMissing = sourceDriftDetails.Any(s => s.StartsWith("missing source"));
                report.Status = anyMissing ? ChartStatus.SourceMissing : ChartStatus.SourceDrift;
                report.Detail = string.Join("; ", sourceDriftDetails.Take(3))
                              + (sourceDriftDetails.Count > 3 ? $" (+{sourceDriftDetails.Count - 3} more)" : "");
                return report;
            }

            report.Status = ChartStatus.Ok;
            report.Detail = "all hashes match";
            return report;
        }

        // Re-export a single chart from its recorded source, overwriting drifted
        // outputs and refreshing the manifest entry. Throws if the manifest
        // entry has no reachable source.
        public static void Repair(ChartReport report)
        {
            if (report.Entry == null)
                throw new InvalidOperationException($"chart {report.CodeName} has no manifest entry — open it manually via Add Chart instead");
            if (string.IsNullOrEmpty(report.Entry.SourceDir) || !Directory.Exists(report.Entry.SourceDir))
                throw new InvalidOperationException($"source directory missing: {report.Entry.SourceDir}");

            string sourceDir = report.Entry.SourceDir;

            // Find the primary KSH (any .ksh in the directory). GetCharts will
            // group sibling charts by Title automatically.
            string[] kshFiles = Directory.GetFiles(sourceDir, "*.ksh");
            if (kshFiles.Length == 0)
                throw new InvalidOperationException($"no .ksh files in {sourceDir}");

            var primaryKsh = new Ksh();
            primaryKsh.Parse(kshFiles[0]);

            var charts = Ksh.Exporter.GetCharts(sourceDir, primaryKsh.Title);
            if (charts.Count == 0)
                throw new InvalidOperationException($"GetCharts found no matching charts in {sourceDir}");

            // Re-attach charts to the existing music_db header so we keep the
            // user's tweaks (volume, level, distribution date, jacket, etc.).
            // The exporter rebuilds Levels[].Chart from the parsed KSH.
            var header = report.Header;
            header.Levels = new Dictionary<Difficulty, VoxLevelHeader>();

            var importOptions = new AudioImportOptions
            {
                Format            = ParseAudioFormat(report.Entry.AudioFormat),
                NormalizeLoudness = true,
            };

            var exporter = new Ksh.Exporter(primaryKsh);
            exporter.Export(header, charts, null, importOptions);
            exporter.Action?.Invoke();

            // Re-export updates the manifest via Exporter's hook, but the
            // music_db Save is the caller's responsibility (mirroring the
            // bulk-import flow which saves once at the end).
        }

        private static AudioFormat ParseAudioFormat(string s)
        {
            if (string.Equals(s, "S3V", StringComparison.OrdinalIgnoreCase)) return AudioFormat.S3V;
            return AudioFormat.Iidx;
        }
    }
}
