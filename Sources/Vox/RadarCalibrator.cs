using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VoxCharger
{
    // One-off tool: fits RadarCalculator coefficients against official Konami
    // radar values. Walks music_db.xml, parses each per-difficulty .vox, extracts
    // a common feature vector (via RadarCalculator.ExtractFeatures) per chart,
    // and runs multivariate linear regression per radar axis.
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
            Console.WriteLine("=== Fitted coefficients (paste into RadarCalculator) ===");
            for (int axis = 0; axis < AxisNames.Length; axis++)
                FitAndReport(samples, axis);

            Console.WriteLine();
            Console.WriteLine("=== Direct-formula fits (one scalar each, for Notes and Peak) ===");
            FitDirectAndReport(samples, 0, "NotesScale", RadarCalculator.RawNotes);
            FitDirectAndReport(samples, 1, "PeakScale",  RadarCalculator.RawPeak);
        }

        // Single-variable ordinary least squares through the origin:
        //   target ≈ scale * rawFn(features)
        // Fits one coefficient and reports R² and RMSE so the direct-formula
        // fit can be compared head-to-head with the multivariate regression.
        private static void FitDirectAndReport(List<Sample> samples, int axisIndex, string constName, Func<double[], double> rawFn)
        {
            int n = samples.Count;
            double sumXY = 0, sumXX = 0, ySum = 0;
            foreach (var s in samples)
            {
                double x = rawFn(s.Features);
                double y = s.Targets[axisIndex];
                sumXY += x * y;
                sumXX += x * x;
                ySum  += y;
            }
            if (sumXX <= 0)
            {
                Console.WriteLine($"[{AxisNames[axisIndex]}] direct fit: no usable signal");
                return;
            }

            double scale = sumXY / sumXX;
            double yMean = ySum / n;
            double ssRes = 0, ssTot = 0;
            foreach (var s in samples)
            {
                double pred = scale * rawFn(s.Features);
                double err = s.Targets[axisIndex] - pred;
                ssRes += err * err;
                double d = s.Targets[axisIndex] - yMean;
                ssTot += d * d;
            }
            double rmse = Math.Sqrt(ssRes / n);
            double r2   = ssTot > 0 ? 1 - ssRes / ssTot : 0;

            Console.WriteLine();
            Console.WriteLine($"--- {AxisNames[axisIndex]} direct --- (n={n}, R\u00b2={r2:F3}, RMSE={rmse:F2})");
            Console.WriteLine($"  private const double {constName} = {scale.ToString("F6", CultureInfo.InvariantCulture)};");
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

        private static void FitAndReport(List<Sample> samples, int axisIndex)
        {
            int n = samples.Count;
            int k = RadarCalculator.FeatureNames.Length;

            double[,] xt_x = new double[k, k];
            double[]  xt_y = new double[k];
            double ySum = 0;
            foreach (var s in samples) ySum += s.Targets[axisIndex];
            double yMean = ySum / n;

            foreach (var s in samples)
            {
                double y = s.Targets[axisIndex];
                for (int i = 0; i < k; i++)
                {
                    double xi = s.Features[i];
                    xt_y[i] += xi * y;
                    for (int j = 0; j < k; j++)
                        xt_x[i, j] += xi * s.Features[j];
                }
            }

            double[] weights;
            try
            {
                weights = SolveLinearSystem(xt_x, xt_y);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{AxisNames[axisIndex]}] solve failed: {ex.Message}");
                return;
            }

            double ssRes = 0, ssTot = 0;
            foreach (var s in samples)
            {
                double pred = 0;
                for (int i = 0; i < k; i++) pred += weights[i] * s.Features[i];
                double err = s.Targets[axisIndex] - pred;
                ssRes += err * err;
                double d = s.Targets[axisIndex] - yMean;
                ssTot += d * d;
            }
            double rmse = Math.Sqrt(ssRes / n);
            double r2   = ssTot > 0 ? 1 - ssRes / ssTot : 0;

            Console.WriteLine();
            Console.WriteLine($"--- {AxisNames[axisIndex]} --- (n={n}, R\u00b2={r2:F3}, RMSE={rmse:F2})");
            for (int i = 0; i < k; i++)
                Console.WriteLine($"  {RadarCalculator.FeatureNames[i],-36} {weights[i],12:F6}");
        }

        // (XᵀX) β = Xᵀy  via Gaussian elimination with partial pivoting.
        private static double[] SolveLinearSystem(double[,] A, double[] b)
        {
            int n = b.Length;
            var M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, n] = b[i];
            }

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                double pivotVal = Math.Abs(M[col, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double v = Math.Abs(M[r, col]);
                    if (v > pivotVal) { pivot = r; pivotVal = v; }
                }
                if (pivotVal < 1e-12)
                    throw new InvalidOperationException($"singular matrix at column {col}");

                if (pivot != col)
                {
                    for (int j = col; j <= n; j++)
                    {
                        double tmp = M[col, j]; M[col, j] = M[pivot, j]; M[pivot, j] = tmp;
                    }
                }

                for (int r = col + 1; r < n; r++)
                {
                    double factor = M[r, col] / M[col, col];
                    if (factor == 0) continue;
                    for (int j = col; j <= n; j++) M[r, j] -= factor * M[col, j];
                }
            }

            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = M[i, n];
                for (int j = i + 1; j < n; j++) sum -= M[i, j] * x[j];
                x[i] = sum / M[i, i];
            }
            return x;
        }
    }
}
