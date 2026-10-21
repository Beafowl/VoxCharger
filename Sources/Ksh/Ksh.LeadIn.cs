using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxCharger
{
    // Lead-in / tail helpers for the Ksh class. Lives in its own file to
    // keep the giant Ksh.Parse method from getting noisier.
    //
    // Concept: a lot of community KSM charts begin with the first note
    // sitting at chart tick 0 — meaning notes are already at the judgement
    // line the moment the player presses Start, with no lead-in beat.
    // A handful also have a long music tail after the chart's last note
    // because the original KSM author picked a song that keeps going
    // after the gameplay ends.
    //
    // We address both by:
    //   * detecting the offending charts at convert time,
    //   * shifting all chart events forward by N whole measures (clean
    //     beat alignment) so the first note appears at least ~1 s into
    //     gameplay,
    //   * telling the audio pipeline how much extra silence to prepend
    //     and where to cut+fade the tail so audio stays in sync with
    //     the shifted chart.
    public partial class Ksh
    {
        // Default policy constants — kept on Ksh so every entry point (CLI
        // and GUI alike) gets the same behavior. Tweak here, not at the call
        // sites.
        public const int AutoLeadInMinMs    = 1000; // ≥1 s before the first note
        public const int AutoTailPadMs      = 1000; // 1 s of breathing room after the last event
        public const int AutoTailFadeOutMs  = 1500; // linear fade across the last 1.5 s

        // Inspect a freshly-parsed Ksh and:
        //   1) if the first gameplay event is too close to chart tick 0,
        //      shift every event forward by enough whole measures to give
        //      the player at least AutoLeadInMinMs of breathing room, and
        //      record the equivalent silence on `audioOptions.LeadInPadMs`
        //      so the audio pipeline pre-pends matching silence;
        //   2) compute where the chart actually ends, and program a
        //      tail truncate + fade on `audioOptions` so the music doesn't
        //      keep playing after the chart finishes.
        //
        // Returns the number of measures the chart events were shifted by.
        // Pass this back through `ParseOption.LeadInMeasures` when you call
        // `Ksh.Exporter.Export(...)` so the per-difficulty re-parses inside
        // the exporter apply the same shift and stay in sync with the main
        // Ksh.
        //
        // Errors are swallowed — a bad timing helper shouldn't abort an
        // otherwise-fine conversion. Worst case we just emit no shift / no
        // tail trim.
        public static int ApplyAutoLeadInTo(Ksh ksh, AudioImportOptions audioOptions)
        {
            if (ksh == null || audioOptions == null) return 0;
            int measuresShifted = 0;
            try
            {
                double firstEventMs = ksh.GetFirstGameplayEventMs();

                int leadInMs = 0;
                if (firstEventMs < AutoLeadInMinMs && !double.IsPositiveInfinity(firstEventMs))
                {
                    double measureMs = ksh.Events.MeasureLength(1) * MsPerTick(ksh.SafeInitialBpm);
                    int needMs = AutoLeadInMinMs - (int)firstEventMs;
                    measuresShifted = (int)System.Math.Ceiling(needMs / measureMs);
                    if (measuresShifted < 1) measuresShifted = 1;

                    leadInMs = ksh.PrependLeadInMeasures(measuresShifted);
                    audioOptions.LeadInPadMs = leadInMs;
                }

                double lastEventMs = ksh.GetLastEventMs();
                if (lastEventMs > 0)
                {
                    int truncateAtMs = (int)System.Math.Round(lastEventMs)
                                     + leadInMs
                                     + AutoTailPadMs
                                     + AutoTailFadeOutMs;
                    audioOptions.TruncateAtMs  = truncateAtMs;
                    audioOptions.TailFadeOutMs = AutoTailFadeOutMs;
                }
            }
            catch
            {
                // Helper bug shouldn't abort the import. Caller will get
                // measuresShifted == 0 and unaffected audioOptions if we
                // throw mid-way.
            }
            return measuresShifted;
        }

        // ms per real tick (1/48 quarter note) at the given BPM
        private static double MsPerTick(float bpm)
        {
            if (bpm <= 0) bpm = 120f;
            return 60000.0 / (48.0 * bpm);
        }

        // Initial BPM with safe fallback. KSH parser sets InitialBpm on the
        // first single-value `t=` it sees; if a file is malformed and never
        // sets one, default to 120.
        private float SafeInitialBpm => InitialBpm > 0 ? InitialBpm : 120f;

        private (int beat, int note) InitialSignatureTuple()
        {
            var sig = Events.GetTimeSignature(Time.Initial);
            if (sig == null) return (4, 4);
            return (sig.Beat, sig.Note);
        }

        // Time of the first GAMEPLAY event (button or laser) in ms. Excludes
        // BPM / signature / tilt / camera events because those typically
        // sit at (1,1,0) for chart setup and don't represent something the
        // player has to react to.
        public double GetFirstGameplayEventMs()
        {
            int firstTick = int.MaxValue;
            foreach (var ev in Events)
            {
                if (!IsGameplayEvent(ev)) continue;
                int tick = Events.ToTicks(ev.Time);
                if (tick < firstTick) firstTick = tick;
            }
            if (firstTick == int.MaxValue) return double.PositiveInfinity;

            return SecondsAtTick(firstTick) * 1000.0;
        }

        // Time of the LAST event of any kind in ms, walking BPM changes
        // properly so a chart that accelerates / decelerates lands on the
        // right total duration.
        public double GetLastEventMs()
        {
            int lastTick = 0;
            foreach (var ev in Events)
            {
                int tick = Events.ToTicks(ev.Time);
                if (ev is Event.Button button)
                    tick += Math.Max(0, button.HoldLength);
                else if (ev is Camera camera)
                    tick += Math.Max(0, camera.Duration);

                if (tick > lastTick) lastTick = tick;
            }
            return SecondsAtTick(lastTick) * 1000.0;
        }

        // Seconds from the chart start to a tick, integrating the BPM changes
        private double SecondsAtTick(int tick)
        {
            var bpms = Events.OfType<Event.Bpm>()
                .Select(b => (tick: Events.ToTicks(b.Time), bpm: b.Value))
                .Where(b => b.bpm > 0)
                .OrderBy(b => b.tick)
                .ToList();

            double seconds = 0;
            int    prevTick = 0;
            float  bpm      = SafeInitialBpm;
            foreach (var change in bpms)
            {
                if (change.tick >= tick)
                    break;

                if (change.tick > prevTick)
                    seconds += (change.tick - prevTick) * MsPerTick(bpm) / 1000.0;

                prevTick = Math.Max(prevTick, change.tick);
                bpm      = change.bpm;
            }

            seconds += (tick - prevTick) * MsPerTick(bpm) / 1000.0;
            return seconds;
        }

        // Buttons and lasers are the user-facing gameplay events. Slams are
        // a Laser subclass so OfType<Laser> covers them too.
        private static bool IsGameplayEvent(Event ev)
        {
            return ev is Event.Button || ev is Event.Laser || ev is Event.Slam;
        }

        // Shift every event in the chart forward by `measures` measures
        // (keeping all beat alignment intact). Inserts copies of the initial
        // BPM and TimeSignature events at (1,1,0) so the new lead-in plays
        // at the correct musical state.
        //
        // Returns the equivalent shift in milliseconds, computed under the
        // initial BPM/signature — the audio side needs the same offset
        // applied via LoudnessNormalizer.LeadInPadMs to stay in sync.
        public int PrependLeadInMeasures(int measures)
        {
            if (measures <= 0) return 0;

            // Capture the initial state before mutating.
            float initBpmValue = SafeInitialBpm;
            var initSigTuple = InitialSignatureTuple();
            int leadInTicks  = measures * Time.CellsPerMeasure(initSigTuple);
            var initialBpmEvent = Events.OfType<Event.Bpm>().OrderBy(e => Events.ToTicks(e.Time)).FirstOrDefault();
            var initialSigEvent = Events.OfType<Event.TimeSignature>().OrderBy(e => e.Time.Measure).FirstOrDefault();

            // Shift everything by `measures` measures. Time.Measure is just
            // an int, so this is a clean offset — no tick math needed.
            var shifted = new List<Event>(Events);
            Events.Clear();
            foreach (var ev in shifted)
            {
                ev.Time.Measure += measures;
                Events.Add(ev);
            }
            MeasureCount += measures;

            // Re-establish initial state at (1,1,0). Only add a stand-in BPM
            // event if there isn't already one we shifted (always true post-
            // shift); same for signature.
            if (initialBpmEvent != null)
                Events.Add(new Event.Bpm(Time.Initial, initBpmValue));
            if (initialSigEvent != null)
                Events.Add(new Event.TimeSignature(Time.Initial, initSigTuple.beat, initSigTuple.note));

            // Convert the shift to ms under the initial BPM — the audio
            // pipeline uses this to prepend the matching silence.
            return (int)Math.Round(leadInTicks * MsPerTick(initBpmValue));
        }
    }
}
