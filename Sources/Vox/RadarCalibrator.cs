using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VoxCharger
{
    // Calibration data for RadarCalculator. Walks music_db.xml, parses each
    // per-difficulty .vox, extracts the feature vector (RadarCalculator.ExtractFeatures)
    // per chart, writes it with the official radar values to a CSV for the offline fit,
    // and reports how the compiled weights perform against the official values.
    //
    // Usage (CLI):  VoxCharger.exe --calibrate-radar <game_data_dir> [--csv out.csv]
    //   <game_data_dir> points at the folder containing `others/music_db.xml`
    //   and `music/<id>_<ascii>/*.vox` (e.g. KFC-xxxxxx/contents/data).
    public static class RadarCalibrator
    {
        private static readonly string[] AxisNames = { "notes", "peak", "tsumami", "tricky", "hand-trip", "one-hand" };

        private class Sample
        {
            public int Id;
            public string Ascii;
            public Difficulty Diff;
            public double[] Features;
            public double[] Targets;
        }

        public static void Calibrate(string gameDataPath, string csvOutputPath = null)
        {
            string mdbPath   = Path.Combine(gameDataPath, "others", "music_db.xml");
            string musicRoot = Path.Combine(gameDataPath, "music");
            if (!File.Exists(mdbPath))
                throw new FileNotFoundException($"music_db.xml not found at {mdbPath}", mdbPath);
            if (!Directory.Exists(musicRoot))
                throw new DirectoryNotFoundException($"music directory not found at {musicRoot}");

            Console.WriteLine($"Loading {mdbPath}...");
            var mdb = new MusicDb();
            mdb.Load(mdbPath);
            Console.WriteLine($"  {mdb.Count} songs loaded");

            var samples = new List<Sample>();
            int scanned = 0, parsed = 0, noRadar = 0, noVox = 0, failed = 0;
            string firstMissingPath = null;

            foreach (var header in mdb)
            {
                foreach (var kv in header.Levels)
                {
                    scanned++;
                    if (kv.Value.Radar == null) { noRadar++; continue; }

                    string voxPath = LocateVox(musicRoot, header, kv.Key);
                    if (voxPath == null)
                    {
                        if (firstMissingPath == null)
                            firstMissingPath = $"{header.CodeName} / {kv.Key}";
                        noVox++;
                        continue;
                    }

                    try
                    {
                        var chart = new VoxChart();
                        chart.Parse(voxPath);

                        var features = RadarCalculator.ExtractFeatures(chart.Events, kv.Value.Level);
                        if (features == null) { failed++; continue; }

                        samples.Add(new Sample
                        {
                            Id       = header.Id,
                            Ascii    = header.Ascii,
                            Diff     = kv.Key,
                            Features = features,
                            Targets  = new double[]
                            {
                                kv.Value.Radar.Notes,
                                kv.Value.Radar.Peak,
                                kv.Value.Radar.Lasers,
                                kv.Value.Radar.Tricky,
                                kv.Value.Radar.HandTrip,
                                kv.Value.Radar.OneHand,
                            }
                        });
                        parsed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (failed <= 5)
                            Console.WriteLine($"  parse failed [{Path.GetFileName(voxPath)}]: {ex.GetType().Name}: {ex.Message}");
                    }

                    if (scanned % 500 == 0)
                        Console.WriteLine($"  {scanned} scanned, {parsed} usable, {failed} failed, {noRadar} no-radar, {noVox} no-vox");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Collection complete: {parsed} usable samples.");
            Console.WriteLine($"  {failed} parse failures, {noRadar} without radar data, {noVox} without vox file");
            if (firstMissingPath != null)
                Console.WriteLine($"  first missing vox: {firstMissingPath}");

            if (parsed < 50)
            {
                Console.Error.WriteLine($"not enough samples to fit (need >=50, got {parsed})");
                return;
            }

            if (csvOutputPath != null)
            {
                WriteCsv(csvOutputPath, samples);
                Console.WriteLine($"Features written to {csvOutputPath}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Shipped RadarCalculator weights vs official values ===");
            Console.WriteLine("    (held-out = id % 5 == 0, the fifth the offline fit never saw)");
            for (int axis = 0; axis < AxisNames.Length; axis++)
            {
                Report(samples, axis, "all");
                Report(samples.Where(s => s.Id % 5 == 0).ToList(), axis, "held-out");
            }
        }

        // R2 / RMSE of the weights currently compiled into RadarCalculator against the
        // official radar values. This is the regression test for the radar: re-run after
        // every change to the feature extraction or the weights.
        private static void Report(List<Sample> samples, int axis, string label)
        {
            int n = samples.Count;
            if (n == 0) return;

            double yMean = samples.Average(s => s.Targets[axis]);
            double ssRes = 0, ssTot = 0;
            foreach (var s in samples)
            {
                var radar = RadarCalculator.Calculate(s.Features);
                double pred;
                switch (axis)
                {
                    case 0:  pred = radar.Notes;    break;
                    case 1:  pred = radar.Peak;     break;
                    case 2:  pred = radar.Lasers;   break;
                    case 3:  pred = radar.Tricky;   break;
                    case 4:  pred = radar.HandTrip; break;
                    default: pred = radar.OneHand;  break;
                }

                double err = s.Targets[axis] - pred;
                ssRes += err * err;
                double d = s.Targets[axis] - yMean;
                ssTot += d * d;
            }

            double rmse = Math.Sqrt(ssRes / n);
            double r2   = ssTot > 0 ? 1 - ssRes / ssTot : 0;
            Console.WriteLine($"  {AxisNames[axis],-10} {label,-9} n={n,5}  R2={r2:F3}  RMSE={rmse:F2}");
        }

        private static string LocateVox(string musicRoot, VoxHeader header, Difficulty diff)
        {
            string folder = Path.Combine(musicRoot, header.CodeName);
            if (!Directory.Exists(folder))
                return null;

            string[] candidates;
            switch (diff)
            {
                case Difficulty.Novice:   candidates = new[] { "1n" }; break;
                case Difficulty.Advanced: candidates = new[] { "2a" }; break;
                case Difficulty.Exhaust:  candidates = new[] { "3e" }; break;
                default:                  candidates = new[] { "5m", "4i" }; break;
            }

            foreach (string code in candidates)
            {
                string path = Path.Combine(folder, $"{header.CodeName}_{code}.vox");
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private static void WriteCsv(string path, List<Sample> samples)
        {
            var sb = new StringBuilder();
            sb.Append("id,ascii,diff");
            for (int a = 0; a < AxisNames.Length; a++) sb.Append($",{AxisNames[a]}_actual");
            for (int f = 0; f < RadarCalculator.FeatureNames.Length; f++) sb.Append($",{RadarCalculator.FeatureNames[f]}");
            sb.AppendLine();

            foreach (var s in samples)
            {
                sb.Append($"{s.Id},{s.Ascii},{s.Diff}");
                for (int a = 0; a < s.Targets.Length; a++)  sb.Append($",{s.Targets[a].ToString(CultureInfo.InvariantCulture)}");
                for (int f = 0; f < s.Features.Length; f++) sb.Append($",{s.Features[f].ToString("0.######", CultureInfo.InvariantCulture)}");
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
