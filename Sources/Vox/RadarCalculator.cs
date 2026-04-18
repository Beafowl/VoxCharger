using System;
using System.Collections.Generic;

namespace VoxCharger
{
    // Radar axes are computed as a linear combination of chart features, with
    // coefficients fitted against ~8.3k official Konami charts. To re-fit, use
    // the `--calibrate-radar` CLI (see RadarCalibrator) and paste the printed
    // weight arrays below.
    public static class RadarCalculator
    {
        // Konami radar values are bytes, but in practice official charts top out around ~200.
        private const byte MaxRadar = 200;

        public static readonly string[] FeatureNames =
        {
            "intercept",
            "chips_per_measure",
            "holds_per_measure",
            "hold_duration_ratio",
            "laser_segments_per_measure",
            "slams_per_measure",
            "laser_active_ratio",
            "both_lasers_active_ratio",
            "button_during_laser_per_measure",
            "simultaneous_lr_per_measure",
            "peak_density_1m",
            "peak_density_2m",
            "peak_density_4m",
            "peak_density_8m",
            "bpm_change_count",
            "bpm_range_fraction",
            "stop_count_per_measure",
            "tilt_changes_per_measure",
            "level_difnum",
        };

        // Notes and Peak use DIRECT formulas instead of multivariate regression.
        //   Notes = NotesScale * (chips + holds + laser_segments + slams) / measures
        //   Peak  = PeakScale  * peak_density_4m
        // The regression for these two axes fit well in R² but produced
        // incoherent weights (negative holds_per_measure for Notes, and Peak
        // explained more by level_difnum than by peak_density features).
        // Direct formulas match the conceptual definitions from voltexes.com
        // and keep each axis tied to the one feature it's supposed to measure.
        // Fit via `VoxCharger.exe --calibrate-radar <game_data_dir>`; paste
        // the reported `[direct formula] scale = ...` values here.
        private const double NotesScale = 6.0;  // placeholder; run --calibrate-radar for a fitted value
        private const double PeakScale  = 6.0;  // placeholder; run --calibrate-radar for a fitted value

        // Remaining axes still use multivariate regression — their definitions
        // (speed changes, hand crossings, laser-while-button coverage) don't
        // reduce to a single feature cleanly. Fitted against music_db.xml
        // (n=8280). Order matches FeatureNames. R² reported per axis.
        private static readonly double[] TsumamiWeights = // R²=0.473, RMSE=16.62
        {
            19.885970, -9.333893, 9.272503, -44.446968, -10.165093, 18.128370, 61.677796, 89.507114,
            -0.413305, 11.819119, 0.542611, 0.549946, 1.108836, 1.255030, -0.027717, -14.531069,
          -445.694887, 6.782250, 2.472188
        };
        private static readonly double[] TrickyWeights = // R²=0.549, RMSE=21.17
        {
           -14.192832, -2.528549, 1.799152, 9.092520, 0.819348, 4.051176, -16.614304, 55.244232,
             0.173178, 2.703967, 0.441194, 1.321132, 0.146902, -0.087734, 0.146782, 79.080910,
           777.107443, -157.395441, 2.315149
        };
        private static readonly double[] HandTripWeights = // R²=0.560, RMSE=19.19
        {
           -13.254981, -4.756649, -4.009566, 7.285322, -11.289355, -15.545705, 8.424176, -48.998835,
            10.126539, 30.765193, 0.445748, 0.700719, -0.080343, 0.999042, -0.001430, -2.762574,
           137.240376, -11.645617, 1.206068
        };
        private static readonly double[] OneHandWeights = // R²=0.770, RMSE=19.15
        {
           -30.226156, -0.285671, 6.280906, 20.331417, -4.547709, -36.096944, -5.962500, -89.615086,
            12.989854, 14.079866, 0.390225, 0.746504, -0.686032, 0.449103, 0.004726, -2.381292,
            57.139071, -15.123112, 3.644141
        };

        public static VoxLevelRadar Calculate(EventCollection events, int level = 1)
        {
            var features = ExtractFeatures(events, level);
            if (features == null)
                return new VoxLevelRadar();

            return new VoxLevelRadar
            {
                Notes    = ClampByte(NotesScale * RawNotes(features)),
                Peak     = ClampByte(PeakScale  * RawPeak(features)),
                Lasers   = Predict(features, TsumamiWeights),
                Tricky   = Predict(features, TrickyWeights),
                HandTrip = Predict(features, HandTripWeights),
                OneHand  = Predict(features, OneHandWeights),
            };
        }

        // Direct Notes signal: all "note-like" objects per measure.
        //   chips_per_measure + holds_per_measure + laser_segments_per_measure + slams_per_measure
        public static double RawNotes(double[] features)
            => features[1] + features[2] + features[4] + features[5];

        // Direct Peak signal: the busiest 4-measure window's average objects per measure.
        public static double RawPeak(double[] features)
            => features[12];

        private static byte ClampByte(double v)
        {
            if (v <= 0) return 0;
            if (v >= MaxRadar) return MaxRadar;
            return (byte)v;
        }

        // Returns FeatureNames-aligned feature vector for the given chart, or null if the
        // chart has no usable events. Public so RadarCalibrator can share this logic.
        public static double[] ExtractFeatures(EventCollection events, int level)
        {
            if (events == null || events.Count == 0)
                return null;

            int maxMeasure = 1;
            foreach (var ev in events)
                if (ev.Time.Measure > maxMeasure) maxMeasure = ev.Time.Measure;
            if (maxMeasure <= 1)
                return null;

            double measures = maxMeasure;

            var buttons = new List<Event.Button>();
            var lasers  = new List<Event.Laser>();
            var bpms    = new List<Event.Bpm>();
            var stops   = new List<Event.Stop>();
            int tiltCount = 0;
            foreach (var ev in events)
            {
                if (ev is Event.Button b)        buttons.Add(b);
                else if (ev is Event.Laser l)    lasers.Add(l);
                else if (ev is Event.Bpm bp)     bpms.Add(bp);
                else if (ev is Event.Stop s)     stops.Add(s);
                else if (ev is Event.TiltMode)   tiltCount++;
            }

            int chips = 0, holds = 0;
            double holdTicks = 0;
            foreach (var b in buttons)
            {
                if (b.HoldLength > 0) { holds++; holdTicks += b.HoldLength; }
                else chips++;
            }
            double chartTicks = measures * 192.0;
            double holdRatio = chartTicks > 0 ? Math.Min(1.0, holdTicks / chartTicks) : 0;

            int totalTicks = maxMeasure * 192;
            var laserActiveL = BuildLaserActive(lasers, Event.LaserTrack.Left,  maxMeasure, out int lsegL);
            var laserActiveR = BuildLaserActive(lasers, Event.LaserTrack.Right, maxMeasure, out int lsegR);
            int laserSegments = lsegL + lsegR;
            int slamCount = CountSlams(lasers);

            int activeL = CountSet(laserActiveL);
            int activeR = CountSet(laserActiveR);
            int activeBoth = CountBoth(laserActiveL, laserActiveR);
            double laserActiveRatio = totalTicks > 0 ? (double)(activeL + activeR - activeBoth) / totalTicks : 0;
            double bothLaserRatio   = totalTicks > 0 ? (double)activeBoth / totalTicks : 0;

            int buttonsDuringLaser = 0;
            int simultaneousLr = 0;
            var buttonsByTick = new Dictionary<int, List<Event.Button>>();
            foreach (var b in buttons)
            {
                int tick = AbsTick(b.Time);
                if (tick < 0 || tick >= totalTicks) continue;
                if (laserActiveL[tick] || laserActiveR[tick]) buttonsDuringLaser++;

                if (!buttonsByTick.TryGetValue(tick, out var lst))
                    buttonsByTick[tick] = lst = new List<Event.Button>();
                lst.Add(b);
            }
            foreach (var kv in buttonsByTick)
            {
                bool left = false, right = false;
                foreach (var b in kv.Value)
                {
                    if (b.Track == Event.ButtonTrack.A || b.Track == Event.ButtonTrack.B || b.Track == Event.ButtonTrack.FxL) left  = true;
                    if (b.Track == Event.ButtonTrack.C || b.Track == Event.ButtonTrack.D || b.Track == Event.ButtonTrack.FxR) right = true;
                }
                if (left && right) simultaneousLr++;
            }

            var perMeasure = new int[maxMeasure + 2];
            foreach (var b in buttons)
            {
                int m = b.Time.Measure;
                if (m >= 1 && m <= maxMeasure) perMeasure[m]++;
            }
            foreach (var l in lasers)
            {
                if (l.Flag == Event.LaserFlag.Start)
                {
                    int m = l.Time.Measure;
                    if (m >= 1 && m <= maxMeasure) perMeasure[m]++;
                }
            }
            double peak1 = WindowMax(perMeasure, maxMeasure, 1);
            double peak2 = WindowMax(perMeasure, maxMeasure, 2);
            double peak4 = WindowMax(perMeasure, maxMeasure, 4);
            double peak8 = WindowMax(perMeasure, maxMeasure, 8);

            int bpmChanges = bpms.Count > 1 ? bpms.Count - 1 : 0;
            double bpmMin = double.MaxValue, bpmMax = double.MinValue;
            foreach (var bp in bpms)
            {
                if (bp.Value < bpmMin) bpmMin = bp.Value;
                if (bp.Value > bpmMax) bpmMax = bp.Value;
            }
            double bpmRangeFrac = (bpmMax > 0 && bpmMin > 0 && bpmMax > bpmMin)
                ? (bpmMax - bpmMin) / bpmMax : 0;

            return new double[]
            {
                1.0,
                chips / measures,
                holds / measures,
                holdRatio,
                laserSegments / measures,
                slamCount / measures,
                laserActiveRatio,
                bothLaserRatio,
                buttonsDuringLaser / measures,
                simultaneousLr / measures,
                peak1,
                peak2,
                peak4,
                peak8,
                bpmChanges,
                bpmRangeFrac,
                stops.Count / measures,
                tiltCount / measures,
                level,
            };
        }

        private static byte Predict(double[] features, double[] weights)
        {
            double sum = 0;
            for (int i = 0; i < weights.Length; i++) sum += weights[i] * features[i];
            if (sum <= 0) return 0;
            if (sum >= MaxRadar) return MaxRadar;
            return (byte)sum;
        }

        private static int AbsTick(Time t) => t.Measure * 192 + (t.Beat - 1) * 48 + t.Offset;

        private static bool[] BuildLaserActive(List<Event.Laser> lasers, Event.LaserTrack track, int maxMeasure, out int segments)
        {
            int totalTicks = maxMeasure * 192;
            var active = new bool[totalTicks];
            segments = 0;

            var filtered = new List<Event.Laser>();
            foreach (var l in lasers)
                if (l.Track == track) filtered.Add(l);
            filtered.Sort((a, b) => AbsTick(a.Time).CompareTo(AbsTick(b.Time)));

            int segStart = -1;
            foreach (var l in filtered)
            {
                int tick = AbsTick(l.Time);
                if (tick < 0 || tick >= totalTicks) continue;

                if (l.Flag == Event.LaserFlag.Start)
                {
                    segments++;
                    segStart = tick;
                }
                else if (l.Flag == Event.LaserFlag.End && segStart >= 0)
                {
                    for (int t = segStart; t <= tick && t < totalTicks; t++) active[t] = true;
                    segStart = -1;
                }
            }
            return active;
        }

        // Slam detection: multiple laser events at the same (track, absolute-tick).
        // Works for both KSH-parsed (IsLaserSlam-tagged) and VOX-parsed charts.
        private static int CountSlams(List<Event.Laser> lasers)
        {
            var byKey = new Dictionary<long, int>();
            foreach (var l in lasers)
            {
                long key = ((long)l.Track << 32) | (uint)AbsTick(l.Time);
                if (!byKey.ContainsKey(key)) byKey[key] = 0;
                byKey[key]++;
            }
            int slams = 0;
            foreach (var kv in byKey)
                if (kv.Value >= 2) slams++;
            return slams;
        }

        private static int CountSet(bool[] a)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i]) n++;
            return n;
        }

        private static int CountBoth(bool[] a, bool[] b)
        {
            int n = 0;
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++) if (a[i] && b[i]) n++;
            return n;
        }

        private static double WindowMax(int[] perMeasure, int maxMeasure, int window)
        {
            if (maxMeasure < window) return 0;
            int best = 0;
            for (int i = 1; i <= maxMeasure - window + 1; i++)
            {
                int sum = 0;
                for (int j = i; j < i + window; j++) sum += perMeasure[j];
                if (sum > best) best = sum;
            }
            return best / (double)window;
        }
    }
}
