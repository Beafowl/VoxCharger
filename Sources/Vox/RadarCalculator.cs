using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoxCharger
{
    // Radar axes are linear combinations of chart features, fitted against the official
    // radar values of the stock charts (~8.6k charts). `--calibrate-radar` extracts the
    // feature CSV (see RadarCalibrator) and reports how the weights below perform; the
    // fit itself is done offline on that CSV (held-out split: id % 5 == 0) and the
    // resulting weights are pasted into the *Weights tables.
    //
    // Features are computed from a normalized view of the chart (see Normalize) so that
    // KSH-parsed and VOX-parsed charts yield identical values: the VOX parser wraps slam
    // end points in Event.Slam, the KSH parser keeps them as two Laser events. Time is
    // measured in ticks of 1/48 quarter note and in seconds (via the BPM events), both
    // independent of the time signature.
    public static class RadarCalculator
    {
        private const int MaxRadar = 255;
        private const int TicksPerMeasure = 192; // one whole note, a 4/4 measure
        private const int TicksPerBeat    = 48;

        // Count-type features, each emitted per measure (_pm, whole-note units) and per second (_ps)
        private static readonly string[] RateFeatures =
        {
            "chips", "holds", "objects",
            "laser_segments", "laser_points", "slams", "laser_move", "laser_dir_changes", "laser_side_switches",
            "chords2", "chords3", "jack16", "jack8", "jack_runs", "offgrid", "triplets",
            "buttons_during_laser", "one_hand", "hand_trip", "one_hand_chords", "one_hand_holds", "both_knob_buttons",
            "bpm_changes", "stops", "cam_rot", "cam_rad", "cam_tilt", "tilt_modes", "lane_clears",
        };

        private static readonly string[] PlainFeatures =
        {
            "total_measures", "span_measures", "total_sec", "span_sec", "span_ratio", "avg_bpm",
            "hold_ratio", "laser_active_ratio", "laser_both_ratio", "laser_l_ratio", "laser_r_ratio", "stop_ratio",
            "offgrid_frac", "one_hand_frac", "hand_trip_frac", "bpm_range_frac",
            "cam_rot_mag_pm", "cam_rad_mag_pm", "cam_tilt_mag_pm",
            "peak_obj_1m", "peak_obj_2m", "peak_obj_4m", "peak_obj_8m",
            "peak_chip_1m", "peak_chip_2m", "peak_chip_4m",
            "peak_obj_1s", "peak_obj_2s", "peak_obj_4s", "peak_chip_1s", "peak_chip_2s",
            "level",
        };

        public static readonly string[] FeatureNames = BuildFeatureNames();
        private static readonly Dictionary<string, int> FeatureIndex =
            FeatureNames.Select((name, index) => new { name, index }).ToDictionary(x => x.name, x => x.index);

        private static string[] BuildFeatureNames()
        {
            var names = new List<string>();
            foreach (string rate in RateFeatures)
            {
                names.Add(rate + "_pm");
                names.Add(rate + "_ps");
            }
            names.AddRange(PlainFeatures);
            return names.ToArray();
        }

        // Weights per axis: "intercept" plus feature name -> coefficient, optionally with a
        // transform prefix ("sqrt:", "log1p:"). Fitted with scripts/radar_fit.py on the CSV of
        //   VoxCharger.exe --calibrate-radar <game>/data --csv features.csv
        //   python radar_fit.py features.csv weights.txt --camera-only-tricky
        // (forward stepwise selection by 5-fold CV, camera features only for TRICKY, no level).
        // Held-out R² / RMSE against the official values, KFC-2026071400, 8648 charts:
        //   notes 0.999 / 1.0   peak 0.924 / 9.7   tsumami 0.684 / 13.2
        //   tricky 0.704 / 18.5 hand-trip 0.720 / 14.8   one-hand 0.860 / 15.0
        // `--calibrate-radar` without --csv re-checks the compiled weights against the game.
        private static readonly Dictionary<string, double> NotesWeights = new Dictionary<string, double>
        {
            { "intercept", -1.226433 },
            { "chips_ps", 16.055857 },
            { "log1p:hold_ratio", 19.445583 },
        };

        private static readonly Dictionary<string, double> PeakWeights = new Dictionary<string, double>
        {
            { "intercept", -28.671986 },
            { "sqrt:peak_obj_4s", 16.266137 },
            { "chips_ps", 6.768679 },
            { "sqrt:peak_chip_1s", 5.911350 },
            { "log1p:hold_ratio", 32.391205 },
            { "chords2_pm", -8.033407 },
            { "peak_chip_1m", 0.776802 },
        };

        private static readonly Dictionary<string, double> TsumamiWeights = new Dictionary<string, double>
        {
            { "intercept", 22.666837 },
            { "laser_both_ratio", 25.105073 },
            { "sqrt:peak_obj_1m", 3.981288 },
            { "sqrt:chips_pm", -26.593027 },
            { "sqrt:slams_ps", -21.005707 },
            { "laser_dir_changes_ps", 9.607794 },
            { "log1p:peak_obj_4m", 25.957157 },
            { "laser_active_ratio", 211.656879 },
            { "sqrt:laser_active_ratio", -107.664741 },
            { "buttons_during_laser_pm", -4.056659 },
            { "log1p:chords2_pm", 12.708087 },
            { "hold_ratio", -16.547050 },
            { "sqrt:jack8_pm", 13.402293 },
            { "sqrt:chords3_pm", 15.422227 },
            { "jack8_ps", -6.797805 },
        };

        private static readonly Dictionary<string, double> TrickyWeights = new Dictionary<string, double>
        {
            { "intercept", -2.628276 },
            { "sqrt:cam_rad_mag_pm", 135.159464 },
            { "bpm_range_frac", 876.409488 },
            { "peak_chip_2s", 2.386968 },
            { "log1p:bpm_range_frac", -1283.027539 },
            { "cam_rad_pm", -78.195360 },
            { "log1p:jack16_ps", 59.871598 },
            { "peak_chip_2m", -1.221299 },
            { "sqrt:bpm_range_frac", 148.275717 },
            { "log1p:cam_rad_ps", 81.712882 },
        };

        private static readonly Dictionary<string, double> HandTripWeights = new Dictionary<string, double>
        {
            { "intercept", -3.551402 },
            { "chords2_ps", 26.604305 },
            { "hand_trip_frac", 573.282597 },
            { "sqrt:one_hand_ps", -10.100559 },
            { "sqrt:chords3_ps", 30.896650 },
            { "sqrt:hand_trip_frac", -82.677912 },
            { "log1p:jack_runs_ps", 25.037492 },
            { "holds_ps", 6.639840 },
            { "sqrt:chords2_pm", -14.902717 },
            { "peak_obj_1m", 0.520413 },
        };

        private static readonly Dictionary<string, double> OneHandWeights = new Dictionary<string, double>
        {
            { "intercept", -28.521229 },
            { "objects_ps", 5.402829 },
            { "holds_pm", 14.964991 },
            { "log1p:chords2_ps", 26.044027 },
            { "one_hand_frac", -81.731321 },
            { "sqrt:one_hand_frac", 248.526902 },
            { "sqrt:slams_pm", -11.459208 },
            { "sqrt:hand_trip_pm", -27.172891 },
            { "buttons_during_laser_ps", 34.504063 },
            { "sqrt:peak_obj_2s", 8.469794 },
            { "sqrt:buttons_during_laser_ps", -253.339179 },
            { "log1p:buttons_during_laser_ps", 193.913066 },
        };

        public static VoxLevelRadar Calculate(EventCollection events, int level = 1)
        {
            var features = ExtractFeatures(events, level);
            return features == null ? new VoxLevelRadar() : Calculate(features);
        }

        // Radar for a chart about to be written: computed from the chart as it reads back from
        // the serialized VOX, so imported charts are measured exactly like the stock charts the
        // weights were fitted on (the VOX parser normalizes a few things the KSH events don't:
        // duplicate laser points, camera value precision, stop markers).
        public static VoxLevelRadar Calculate(VoxChart chart, int level = 1)
        {
            string path = Path.GetTempFileName();
            try
            {
                chart.Serialize(path);
                var parsed = new VoxChart();
                parsed.Parse(path);
                return Calculate(parsed.Events, level);
            }
            catch (Exception)
            {
                return Calculate(chart.Events, level);
            }
            finally
            {
                try { File.Delete(path); } catch { /* temp file, best effort */ }
            }
        }

        public static VoxLevelRadar Calculate(double[] features)
        {
            return new VoxLevelRadar
            {
                Notes    = Predict(features, NotesWeights),
                Peak     = Predict(features, PeakWeights),
                Lasers   = Predict(features, TsumamiWeights),
                Tricky   = Predict(features, TrickyWeights),
                HandTrip = Predict(features, HandTripWeights),
                OneHand  = Predict(features, OneHandWeights),
            };
        }

        private static byte Predict(double[] features, Dictionary<string, double> weights)
        {
            double sum = 0;
            foreach (var kv in weights)
                sum += kv.Key == "intercept" ? kv.Value : kv.Value * Term(features, kv.Key);

            if (sum <= 0) return 0;
            if (sum >= MaxRadar) return MaxRadar;
            return (byte)Math.Round(sum);
        }

        // A weight key is a feature name, optionally prefixed with a transform ("sqrt:chips_ps")
        private static double Term(double[] features, string key)
        {
            int colon = key.IndexOf(':');
            if (colon < 0)
                return features[FeatureIndex[key]];

            double value = features[FeatureIndex[key.Substring(colon + 1)]];
            switch (key.Substring(0, colon))
            {
                case "sqrt":  return Math.Sqrt(Math.Max(0, value));
                case "log1p": return Math.Log(1 + Math.Max(0, value));
                default:      throw new ArgumentException($"Unknown radar feature transform in '{key}'");
            }
        }

        #region --- Normalized chart view ---

        private struct Note
        {
            public int Tick;
            public int Hold;     // ticks, 0 = chip
            public Event.ButtonTrack Track;
        }

        private struct LaserPoint
        {
            public Event.LaserTrack Track;
            public int Tick;
            public int Offset;   // 0..127
            public Event.LaserFlag Flag;
            public int Seq;      // order of appearance, keeps slam start before slam end
        }

        // Tick positions and seconds along the chart, honouring time signatures and BPM changes.
        private class Timeline
        {
            private readonly List<(int measure, int beat, int note)> _signatures;
            private readonly List<int> _starts = new List<int> { 0, 0 }; // _starts[m] = tick where measure m starts
            private List<(int tick, double sec, double secPerTick)> _bpm = new List<(int, double, double)>();

            public Timeline(IEnumerable<Event.TimeSignature> signatures)
            {
                _signatures = signatures
                    .Select(s => (measure: Math.Max(1, s.Time.Measure), beat: Math.Max(1, s.Beat), note: Math.Max(1, s.Note)))
                    .OrderBy(s => s.measure)
                    .ToList();
            }

            private (int beat, int note) SignatureOf(int measure)
            {
                (int beat, int note) sig = (4, 4);
                foreach (var s in _signatures)
                {
                    if (s.measure > measure) break;
                    sig = (s.beat, s.note);
                }
                return sig;
            }

            public int Start(int measure)
            {
                while (_starts.Count <= measure)
                {
                    int last = _starts.Count - 1;
                    var sig  = SignatureOf(last);
                    _starts.Add(_starts[last] + (int)Math.Round(sig.beat * (double)TicksPerMeasure / sig.note));
                }
                return _starts[measure];
            }

            public int TickOf(Time t)
            {
                int measure = Math.Max(1, t.Measure);
                var sig     = SignatureOf(measure);
                return Start(measure) + (int)Math.Round(((t.Beat - 1) * TicksPerBeat + t.Offset) * 4.0 / sig.note);
            }

            public void SetBpms(IEnumerable<(int tick, float bpm)> bpms)
            {
                var list = bpms.Where(b => b.bpm > 0).OrderBy(b => b.tick).ToList();
                if (list.Count == 0) list.Add((0, 120f));
                if (list[0].tick > 0) list.Insert(0, (0, list[0].bpm));

                _bpm = new List<(int, double, double)>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (i + 1 < list.Count && list[i + 1].tick == list[i].tick)
                        continue; // last value at the same tick wins

                    double sec = 0;
                    if (_bpm.Count > 0)
                    {
                        var prev = _bpm[_bpm.Count - 1];
                        sec = prev.sec + (list[i].tick - prev.tick) * prev.secPerTick;
                    }
                    _bpm.Add((list[i].tick, sec, 60.0 / (list[i].bpm * TicksPerBeat)));
                }
            }

            public double SecondsAt(int tick)
            {
                var seg = _bpm[0];
                for (int i = 1; i < _bpm.Count && _bpm[i].tick <= tick; i++)
                    seg = _bpm[i];
                return seg.sec + (tick - seg.tick) * seg.secPerTick;
            }
        }

        private static bool IsLeftSide(Event.ButtonTrack track)
        {
            return track == Event.ButtonTrack.A || track == Event.ButtonTrack.B || track == Event.ButtonTrack.FxL;
        }

        #endregion

        // Returns the FeatureNames-aligned feature vector, or null if the chart has no gameplay.
        public static double[] ExtractFeatures(EventCollection events, int level)
        {
            if (events == null || events.Count == 0)
                return null;

            var signatures = new List<Event.TimeSignature>();
            var buttons    = new List<Event.Button>();
            var lasers     = new List<Event.Laser>();
            var slams      = new List<Event.Slam>();
            var bpms       = new List<Event.Bpm>();
            var stops      = new List<Event.Stop>();
            var cameras    = new List<Camera>();
            int tiltModes  = 0;
            int maxMeasure = 1;

            foreach (var ev in events)
            {
                if (ev.Time != null && ev.Time.Measure > maxMeasure)
                    maxMeasure = ev.Time.Measure;

                if (ev is Event.TimeSignature sig)   signatures.Add(sig);
                else if (ev is Event.Button b)       buttons.Add(b);
                else if (ev is Event.Laser l)        lasers.Add(l);
                else if (ev is Event.Slam s)         slams.Add(s);
                else if (ev is Event.Bpm bpm)        bpms.Add(bpm);
                else if (ev is Event.Stop stop)      stops.Add(stop);
                else if (ev is Event.TiltMode)       tiltModes++;
                else if (ev is Camera cam)           cameras.Add(cam);
            }

            // VOX parser: the slam start stays a Laser event, the end only lives inside the Slam
            var known = new HashSet<Event.Laser>(lasers);
            foreach (var s in slams)
            {
                if (s.Start != null && known.Add(s.Start)) lasers.Add(s.Start);
                if (s.End   != null && known.Add(s.End))   lasers.Add(s.End);
            }

            var tl = new Timeline(signatures);

            var notes = new List<Note>(buttons.Count);
            foreach (var b in buttons)
                notes.Add(new Note { Tick = tl.TickOf(b.Time), Hold = Math.Max(0, b.HoldLength), Track = b.Track });

            var points = new List<LaserPoint>(lasers.Count);
            for (int i = 0; i < lasers.Count; i++)
            {
                var l = lasers[i];
                points.Add(new LaserPoint { Track = l.Track, Tick = tl.TickOf(l.Time), Offset = l.Offset, Flag = l.Flag, Seq = i });
            }
            points.Sort((a, b) => a.Track != b.Track ? a.Track.CompareTo(b.Track) : a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Seq.CompareTo(b.Seq));

            if (notes.Count == 0 && points.Count == 0)
                return null;

            int firstTick = int.MaxValue, lastTick = 0;
            foreach (var n in notes)  { firstTick = Math.Min(firstTick, n.Tick); lastTick = Math.Max(lastTick, n.Tick + n.Hold); }
            foreach (var p in points) { firstTick = Math.Min(firstTick, p.Tick); lastTick = Math.Max(lastTick, p.Tick); }

            while (tl.Start(maxMeasure + 1) < lastTick)
                maxMeasure++; // holds running past the last measure

            int totalTicks = tl.Start(maxMeasure + 1);
            tl.SetBpms(bpms.Select(b => (tl.TickOf(b.Time), b.Value)));

            double spanTicks    = Math.Max(TicksPerBeat, lastTick - firstTick);
            double spanMeasures = spanTicks / TicksPerMeasure;
            double totalMeasures = totalTicks / (double)TicksPerMeasure;
            double spanSec      = Math.Max(1.0, tl.SecondsAt(lastTick) - tl.SecondsAt(firstTick));
            double totalSec     = Math.Max(1.0, tl.SecondsAt(totalTicks));

            // --- buttons -----------------------------------------------------------------
            int chips = 0, holds = 0;
            double holdTicks = 0;
            int offgrid = 0, triplets = 0;
            var byTrack = new Dictionary<Event.ButtonTrack, List<int>>();
            foreach (var n in notes)
            {
                if (n.Hold > 0) { holds++; holdTicks += n.Hold; }
                else chips++;

                if (n.Tick % 12 != 0) offgrid++;
                if (n.Tick % 16 == 0 && n.Tick % 12 != 0) triplets++;

                if (!byTrack.TryGetValue(n.Track, out var ticks))
                    byTrack[n.Track] = ticks = new List<int>();
                ticks.Add(n.Tick);
            }

            int jack16 = 0, jack8 = 0, jackRuns = 0;
            foreach (var ticks in byTrack.Values)
            {
                ticks.Sort();
                int run = 1;
                for (int i = 1; i < ticks.Count; i++)
                {
                    int gap = ticks[i] - ticks[i - 1];
                    if (gap <= 0) continue;
                    if (gap <= 12) jack16++;
                    if (gap <= 24) { jack8++; run++; }
                    else { if (run >= 3) jackRuns++; run = 1; }
                }
                if (run >= 3) jackRuns++;
            }

            // --- lasers ------------------------------------------------------------------
            var activeL = new bool[totalTicks + 1];
            var activeR = new bool[totalTicks + 1];
            int segments = 0, slamCount = 0, dirChanges = 0;
            double move = 0;
            var segmentStarts = new List<(int tick, Event.LaserTrack track)>();
            var objectTicks   = new List<int>();

            for (int i = 0; i < points.Count;)
            {
                var track  = points[i].Track;
                var active = track == Event.LaserTrack.Left ? activeL : activeR;
                int segStart = -1, lastTickL = 0, lastOffset = 0, lastDir = 0;

                void Close(int endTick)
                {
                    segments++;
                    segmentStarts.Add((segStart, track));
                    objectTicks.Add(segStart);
                    for (int t = Math.Max(0, segStart); t <= endTick && t < active.Length; t++)
                        active[t] = true;
                    segStart = -1;
                }

                for (; i < points.Count && points[i].Track == track; i++)
                {
                    var p = points[i];

                    // Identical consecutive points (same tick and offset) are artifacts of the KSH
                    // slam alignment; the VOX parser drops them, so they carry no information here either
                    if (segStart >= 0 && p.Tick == lastTickL && p.Offset == lastOffset)
                    {
                        if (p.Flag == Event.LaserFlag.End) Close(p.Tick);
                        continue;
                    }

                    if (segStart < 0 || p.Flag == Event.LaserFlag.Start)
                    {
                        if (segStart >= 0) Close(lastTickL);
                        segStart = p.Tick; lastTickL = p.Tick; lastOffset = p.Offset; lastDir = 0;
                        if (p.Flag == Event.LaserFlag.End) Close(p.Tick);
                        continue;
                    }

                    int delta = p.Offset - lastOffset;
                    if (delta != 0)
                    {
                        if (p.Tick == lastTickL) { slamCount++; objectTicks.Add(p.Tick); }
                        move += Math.Abs(delta) / 127.0;
                        int dir = Math.Sign(delta);
                        if (lastDir != 0 && dir != lastDir) dirChanges++;
                        lastDir = dir;
                    }
                    lastOffset = p.Offset; lastTickL = p.Tick;

                    if (p.Flag == Event.LaserFlag.End)
                        Close(p.Tick);
                }
                if (segStart >= 0) Close(lastTickL);
            }

            int activeLCount = 0, activeRCount = 0, bothCount = 0;
            for (int t = 0; t < totalTicks; t++)
            {
                if (activeL[t]) activeLCount++;
                if (activeR[t]) activeRCount++;
                if (activeL[t] && activeR[t]) bothCount++;
            }

            int sideSwitches = 0;
            segmentStarts.Sort((a, b) => a.tick.CompareTo(b.tick));
            for (int i = 1; i < segmentStarts.Count; i++)
                if (segmentStarts[i].track != segmentStarts[i - 1].track) sideSwitches++;

            // --- hands -------------------------------------------------------------------
            int chords2 = 0, chords3 = 0, buttonsDuringLaser = 0, oneHand = 0, handTrip = 0;
            int oneHandChords = 0, oneHandHolds = 0, bothKnob = 0;
            foreach (var group in notes.GroupBy(n => n.Tick))
            {
                int tick  = group.Key;
                bool left  = tick >= 0 && tick < activeL.Length && activeL[tick];
                bool right = tick >= 0 && tick < activeR.Length && activeR[tick];
                int count  = group.Count();

                if (count >= 2) chords2++;
                if (count >= 3) chords3++;
                if (left || right) buttonsDuringLaser += count;

                if (left ^ right)
                {
                    oneHand += count;
                    if (count >= 2) oneHandChords++;
                    foreach (var n in group)
                    {
                        if (n.Hold > 0) oneHandHolds++;
                        // The hand on the knob covers that side's buttons, the free hand has to cross over
                        if (left == IsLeftSide(n.Track)) handTrip++;
                    }
                }
                else if (left && right)
                    bothKnob += count;
            }

            // --- tempo -------------------------------------------------------------------
            var bpmTicks = bpms.Select(b => (tick: tl.TickOf(b.Time), b)).OrderBy(x => x.tick).ToList();
            int bpmChanges = 0;
            double bpmMin = double.MaxValue, bpmMax = 0;
            for (int i = 0; i < bpmTicks.Count; i++)
            {
                float v = bpmTicks[i].b.Value;
                if (v <= 0) continue;
                bpmMin = Math.Min(bpmMin, v); bpmMax = Math.Max(bpmMax, v);
                if (i > 0 && Math.Abs(v - bpmTicks[i - 1].b.Value) > 0.001) bpmChanges++;
            }
            double bpmRangeFrac = bpmMax > 0 && bpmMin < double.MaxValue ? (bpmMax - bpmMin) / bpmMax : 0;

            // Stops: Event.Stop on the VOX side, Bpm.IsStop markers (until the next BPM event) on the KSH side
            int stopCount = 0;
            double stopTicks = 0;
            foreach (var s in stops) { stopCount++; stopTicks += Math.Max(0, s.Duration); }
            for (int i = 0; i < bpmTicks.Count; i++)
            {
                if (!bpmTicks[i].b.IsStop) continue;
                stopCount++;
                if (i + 1 < bpmTicks.Count) stopTicks += bpmTicks[i + 1].tick - bpmTicks[i].tick;
            }

            // --- camera ------------------------------------------------------------------
            int camRot = 0, camRad = 0, camTilt = 0, laneClears = 0;
            double camRotMag = 0, camRadMag = 0, camTiltMag = 0;
            foreach (var cam in cameras)
            {
                // VOX stores camera values with 2 decimals, anything below that is rounding noise
                double delta = Math.Abs(cam.End - cam.Start);
                switch (cam.Work)
                {
                    case Camera.WorkType.Rotation:  if (delta >= 0.005) { camRot++;  camRotMag  += delta; } break;
                    case Camera.WorkType.Radian:    if (delta >= 0.005) { camRad++;  camRadMag  += delta; } break;
                    case Camera.WorkType.Tilt:      if (delta >= 0.005) { camTilt++; camTiltMag += delta; } break;
                    case Camera.WorkType.LaneClear: laneClears++; break;
                }
            }

            // --- density peaks -----------------------------------------------------------
            foreach (var n in notes) objectTicks.Add(n.Tick);
            var chipTicks = notes.Where(n => n.Hold == 0).Select(n => n.Tick).ToList();

            int bins = totalTicks / TicksPerMeasure + 2;
            var objBins  = new int[bins];
            var chipBins = new int[bins];
            foreach (int t in objectTicks) if (t >= 0) objBins[Math.Min(bins - 1, t / TicksPerMeasure)]++;
            foreach (int t in chipTicks)   if (t >= 0) chipBins[Math.Min(bins - 1, t / TicksPerMeasure)]++;

            var objSec  = objectTicks.Select(t => tl.SecondsAt(t)).OrderBy(x => x).ToList();
            var chipSec = chipTicks.Select(t => tl.SecondsAt(t)).OrderBy(x => x).ToList();

            // --- assemble ----------------------------------------------------------------
            int objects = chips + holds + segments + slamCount;
            int buttonsTotal = Math.Max(1, chips + holds);
            var f = new Dictionary<string, double>();

            void Rate(string name, double count)
            {
                f[name + "_pm"] = count / spanMeasures;
                f[name + "_ps"] = count / spanSec;
            }

            Rate("chips", chips);
            Rate("holds", holds);
            Rate("objects", objects);
            Rate("laser_segments", segments);
            Rate("laser_points", points.Count);
            Rate("slams", slamCount);
            Rate("laser_move", move);
            Rate("laser_dir_changes", dirChanges);
            Rate("laser_side_switches", sideSwitches);
            Rate("chords2", chords2);
            Rate("chords3", chords3);
            Rate("jack16", jack16);
            Rate("jack8", jack8);
            Rate("jack_runs", jackRuns);
            Rate("offgrid", offgrid);
            Rate("triplets", triplets);
            Rate("buttons_during_laser", buttonsDuringLaser);
            Rate("one_hand", oneHand);
            Rate("hand_trip", handTrip);
            Rate("one_hand_chords", oneHandChords);
            Rate("one_hand_holds", oneHandHolds);
            Rate("both_knob_buttons", bothKnob);
            Rate("bpm_changes", bpmChanges);
            Rate("stops", stopCount);
            Rate("cam_rot", camRot);
            Rate("cam_rad", camRad);
            Rate("cam_tilt", camTilt);
            Rate("tilt_modes", tiltModes);
            Rate("lane_clears", laneClears);

            f["total_measures"]     = totalMeasures;
            f["span_measures"]      = spanMeasures;
            f["total_sec"]          = totalSec;
            f["span_sec"]           = spanSec;
            f["span_ratio"]         = Math.Min(1.0, spanTicks / Math.Max(1.0, totalTicks));
            f["avg_bpm"]            = (spanTicks / TicksPerBeat) / (spanSec / 60.0);
            f["hold_ratio"]         = Math.Min(1.0, holdTicks / spanTicks);
            f["laser_active_ratio"] = Math.Min(1.0, (activeLCount + activeRCount - bothCount) / spanTicks);
            f["laser_both_ratio"]   = Math.Min(1.0, bothCount / spanTicks);
            f["laser_l_ratio"]      = Math.Min(1.0, activeLCount / spanTicks);
            f["laser_r_ratio"]      = Math.Min(1.0, activeRCount / spanTicks);
            f["stop_ratio"]         = Math.Min(1.0, stopTicks / spanTicks);
            f["offgrid_frac"]       = offgrid / (double)buttonsTotal;
            f["one_hand_frac"]      = oneHand / (double)buttonsTotal;
            f["hand_trip_frac"]     = handTrip / (double)buttonsTotal;
            f["bpm_range_frac"]     = bpmRangeFrac;
            f["cam_rot_mag_pm"]     = camRotMag / spanMeasures;
            f["cam_rad_mag_pm"]     = camRadMag / spanMeasures;
            f["cam_tilt_mag_pm"]    = camTiltMag / spanMeasures;
            f["peak_obj_1m"]        = WindowMax(objBins, 1);
            f["peak_obj_2m"]        = WindowMax(objBins, 2);
            f["peak_obj_4m"]        = WindowMax(objBins, 4);
            f["peak_obj_8m"]        = WindowMax(objBins, 8);
            f["peak_chip_1m"]       = WindowMax(chipBins, 1);
            f["peak_chip_2m"]       = WindowMax(chipBins, 2);
            f["peak_chip_4m"]       = WindowMax(chipBins, 4);
            f["peak_obj_1s"]        = WindowMax(objSec, 1.0);
            f["peak_obj_2s"]        = WindowMax(objSec, 2.0) / 2.0;
            f["peak_obj_4s"]        = WindowMax(objSec, 4.0) / 4.0;
            f["peak_chip_1s"]       = WindowMax(chipSec, 1.0);
            f["peak_chip_2s"]       = WindowMax(chipSec, 2.0) / 2.0;
            f["level"]              = level;

            var result = new double[FeatureNames.Length];
            for (int i = 0; i < FeatureNames.Length; i++)
                result[i] = f[FeatureNames[i]];
            return result;
        }

        // Busiest run of `window` consecutive bins, averaged per bin
        private static double WindowMax(int[] bins, int window)
        {
            if (bins.Length < window) return 0;
            int sum = 0, best = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                sum += bins[i];
                if (i >= window) sum -= bins[i - window];
                if (sum > best) best = sum;
            }
            return best / (double)window;
        }

        // Most events inside any `window` seconds
        private static double WindowMax(List<double> sortedSeconds, double window)
        {
            int best = 0, j = 0;
            for (int i = 0; i < sortedSeconds.Count; i++)
            {
                while (sortedSeconds[i] - sortedSeconds[j] > window) j++;
                best = Math.Max(best, i - j + 1);
            }
            return best;
        }
    }
}
