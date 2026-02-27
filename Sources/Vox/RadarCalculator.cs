using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxCharger
{
    public static class RadarCalculator
    {
        // Radar values are 0-255 (byte), but in practice official charts use ~0-200 range
        private const byte MaxRadar = 200;

        public static VoxLevelRadar Calculate(EventCollection events)
        {
            if (events == null || events.Count == 0)
                return new VoxLevelRadar();

            var buttons = new List<Event.Button>();
            var lasers  = new List<Event.Laser>();
            var bpms    = new List<Event.Bpm>();
            var stops   = new List<Event.Stop>();
            var cameras = new List<Camera>();
            var tilts   = new List<Event.TiltMode>();

            foreach (var ev in events)
            {
                if (ev is Event.Button btn)
                    buttons.Add(btn);
                else if (ev is Event.Laser lsr)
                    lasers.Add(lsr);
                else if (ev is Event.Bpm bpm)
                    bpms.Add(bpm);
                else if (ev is Event.Stop stop)
                    stops.Add(stop);
                else if (ev is Camera cam)
                    cameras.Add(cam);
                else if (ev is Event.TiltMode tilt)
                    tilts.Add(tilt);
            }

            int maxMeasure = 1;
            foreach (var ev in events)
            {
                if (ev.Time.Measure > maxMeasure)
                    maxMeasure = ev.Time.Measure;
            }

            if (maxMeasure <= 1)
                return new VoxLevelRadar();

            return new VoxLevelRadar
            {
                Notes    = CalculateNotes(buttons, lasers, maxMeasure),
                Peak     = CalculatePeak(buttons, lasers, maxMeasure),
                Lasers   = CalculateLasers(lasers, buttons, maxMeasure),
                Tricky   = CalculateTricky(bpms, stops, cameras, tilts, maxMeasure),
                HandTrip = CalculateHandTrip(buttons, maxMeasure),
                OneHand  = CalculateOneHand(buttons, lasers, maxMeasure),
            };
        }

        private static byte CalculateNotes(List<Event.Button> buttons, List<Event.Laser> lasers, int maxMeasure)
        {
            // Note density: total note events per measure
            int chipCount = 0;
            int holdCount = 0;
            foreach (var btn in buttons)
            {
                if (btn.HoldLength > 0)
                    holdCount++;
                else
                    chipCount++;
            }

            // Count laser segments (start events only to avoid double-counting)
            int laserSegments = 0;
            foreach (var lsr in lasers)
            {
                if (lsr.Flag == Event.LaserFlag.Start)
                    laserSegments++;
            }

            float totalNotes = chipCount + holdCount * 1.5f + laserSegments * 0.5f;
            float notesPerMeasure = totalNotes / maxMeasure;

            // Scale: ~4 notes/measure = 0, ~40+ notes/measure = 200
            float normalized = (notesPerMeasure - 4f) / 36f;
            return ClampToByte(normalized);
        }

        private static byte CalculatePeak(List<Event.Button> buttons, List<Event.Laser> lasers, int maxMeasure)
        {
            // Peak density: find the densest 4-measure window
            var measureDensity = new Dictionary<int, int>();

            foreach (var btn in buttons)
            {
                int m = btn.Time.Measure;
                if (!measureDensity.ContainsKey(m))
                    measureDensity[m] = 0;
                measureDensity[m]++;
            }

            foreach (var lsr in lasers)
            {
                if (lsr.Flag != Event.LaserFlag.Start)
                    continue;

                int m = lsr.Time.Measure;
                if (!measureDensity.ContainsKey(m))
                    measureDensity[m] = 0;
                measureDensity[m]++;
            }

            // Sliding window of 4 measures
            float maxDensity = 0;
            for (int i = 1; i <= maxMeasure - 3; i++)
            {
                int sum = 0;
                for (int j = i; j < i + 4; j++)
                {
                    if (measureDensity.ContainsKey(j))
                        sum += measureDensity[j];
                }

                float avg = sum / 4f;
                if (avg > maxDensity)
                    maxDensity = avg;
            }

            // Scale: ~8 notes/measure peak = 0, ~60+ notes/measure = 200
            float normalized = (maxDensity - 8f) / 52f;
            return ClampToByte(normalized);
        }

        private static byte CalculateLasers(List<Event.Laser> lasers, List<Event.Button> buttons, int maxMeasure)
        {
            // Laser proportion: how much of the chart is laser-oriented
            int laserEvents = lasers.Count;
            int buttonEvents = buttons.Count;
            int totalEvents = laserEvents + buttonEvents;

            if (totalEvents == 0)
                return 0;

            float laserRatio = (float)laserEvents / totalEvents;

            // Also consider laser density
            float laserPerMeasure = (float)laserEvents / maxMeasure;

            // Combine ratio and density
            float combined = laserRatio * 0.5f + Math.Min(laserPerMeasure / 20f, 1f) * 0.5f;
            float normalized = combined / 0.6f; // Charts rarely exceed 60% laser content

            return ClampToByte(normalized);
        }

        private static byte CalculateTricky(List<Event.Bpm> bpms, List<Event.Stop> stops,
                                             List<Camera> cameras, List<Event.TiltMode> tilts, int maxMeasure)
        {
            // Tricky: BPM changes, stops, camera manipulation
            float score = 0f;

            // BPM changes (more = trickier)
            int bpmChanges = bpms.Count > 1 ? bpms.Count - 1 : 0;
            score += Math.Min(bpmChanges * 8f, 80f);

            // Stops (each adds significant tricky factor)
            score += Math.Min(stops.Count * 15f, 60f);

            // Camera events per measure
            float cameraPerMeasure = maxMeasure > 0 ? (float)cameras.Count / maxMeasure : 0;
            score += Math.Min(cameraPerMeasure * 10f, 40f);

            // Tilt mode changes
            score += Math.Min(tilts.Count * 3f, 20f);

            float normalized = score / MaxRadar;
            return ClampToByte(normalized);
        }

        private static byte CalculateHandTrip(List<Event.Button> buttons, int maxMeasure)
        {
            // Hand-trip: notes requiring crossing hands or using the opposite side
            // FX holds with BT notes on the other side, or simultaneous opposite-side presses
            var eventsByMeasureBeat = new Dictionary<string, List<Event.Button>>();
            foreach (var btn in buttons)
            {
                string key = $"{btn.Time.Measure},{btn.Time.Beat},{btn.Time.Offset}";
                if (!eventsByMeasureBeat.ContainsKey(key))
                    eventsByMeasureBeat[key] = new List<Event.Button>();
                eventsByMeasureBeat[key].Add(btn);
            }

            int handTripCount = 0;
            foreach (var pair in eventsByMeasureBeat)
            {
                var simultaneous = pair.Value;
                if (simultaneous.Count < 2)
                    continue;

                bool hasLeftSide = false;  // BT-A, BT-B, FX-L
                bool hasRightSide = false; // BT-C, BT-D, FX-R

                foreach (var btn in simultaneous)
                {
                    if (btn.Track == Event.ButtonTrack.A || btn.Track == Event.ButtonTrack.B || btn.Track == Event.ButtonTrack.FxL)
                        hasLeftSide = true;
                    if (btn.Track == Event.ButtonTrack.C || btn.Track == Event.ButtonTrack.D || btn.Track == Event.ButtonTrack.FxR)
                        hasRightSide = true;
                }

                // Both sides at once = hand-trip
                if (hasLeftSide && hasRightSide)
                    handTripCount++;
            }

            float handTripPerMeasure = maxMeasure > 0 ? (float)handTripCount / maxMeasure : 0;

            // Scale: ~0.5 hand-trips/measure = 0, ~8+ = 200
            float normalized = (handTripPerMeasure - 0.5f) / 7.5f;
            return ClampToByte(normalized);
        }

        private static byte CalculateOneHand(List<Event.Button> buttons, List<Event.Laser> lasers, int maxMeasure)
        {
            // One-hand: button notes while lasers are active (one hand on knob, one on buttons)
            // Find measures where lasers are active
            var laserActiveMeasures = new HashSet<int>();
            foreach (var lsr in lasers)
                laserActiveMeasures.Add(lsr.Time.Measure);

            int oneHandCount = 0;
            foreach (var btn in buttons)
            {
                if (laserActiveMeasures.Contains(btn.Time.Measure))
                    oneHandCount++;
            }

            float oneHandPerMeasure = maxMeasure > 0 ? (float)oneHandCount / maxMeasure : 0;

            // Scale: ~1 one-hand/measure = 0, ~15+ = 200
            float normalized = (oneHandPerMeasure - 1f) / 14f;
            return ClampToByte(normalized);
        }

        private static byte ClampToByte(float normalized)
        {
            if (normalized <= 0f)
                return 0;
            if (normalized >= 1f)
                return MaxRadar;
            return (byte)(normalized * MaxRadar);
        }
    }
}
