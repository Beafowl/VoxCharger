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
                    var sig = ksh.Events.GetTimeSignature(Time.Initial);
                    int sigBeat = sig != null ? sig.Beat : 4;
                    float bpm = ksh.InitialBpm > 0 ? ksh.InitialBpm : 120f;
                    double measureMs = sigBeat * 60000.0 / bpm;
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

        // ms per chart-tick at the given BPM and time signature.
        //   192 ticks span one measure
        //   one measure = signature.beat * (60 / bpm) seconds
        // so one tick = signature.beat * 60000 / (192 * bpm) milliseconds.
        // Both BPM and signature matter — a 3/4 measure at the same BPM is
        // shorter than a 4/4 measure, so each of its 192 ticks is shorter
        // too. Keep this as a pure helper so callers don't have to remember
        // which factors apply.
        private static double MsPerTick(float bpm, int sigBeat)
        {
            if (bpm <= 0) bpm = 120f;
            if (sigBeat <= 0) sigBeat = 4;
            return sigBeat * 60000.0 / (192.0 * bpm);
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
                var sig = Events.GetTimeSignature(ev.Time);
                int tick = ev.Time.GetAbsoluteOffset((sig.Beat, sig.Note));
                if (tick < firstTick) firstTick = tick;
            }
            if (firstTick == int.MaxValue) return double.PositiveInfinity;

            // Use initial BPM/signature — the first note is virtually always
            // before any BPM change, so this is exact in practice.
            var initSig = InitialSignatureTuple();
            return firstTick * MsPerTick(SafeInitialBpm, initSig.beat);
        }

        // Time of the LAST event of any kind in ms, walking BPM / signature
        // changes properly so a chart that accelerates / decelerates lands
        // on the right total duration.
        public double GetLastEventMs()
        {
            // Snapshot each event's absolute tick under the signature
            // active at its position, then sort.
            var sortedByTick = Events
                .Select(e =>
                {
                    var sig = Events.GetTimeSignature(e.Time);
                    int tick = e.Time.GetAbsoluteOffset((sig.Beat, sig.Note));
                    return (ev: e, tick);
                })
                .OrderBy(x => x.tick)
                .ToList();
            if (sortedByTick.Count == 0) return 0;

            double elapsedMs = 0;
            int prevTick = 0;
            float currentBpm = SafeInitialBpm;
            var currentSig = InitialSignatureTuple();

            foreach (var (ev, tick) in sortedByTick)
            {
                int delta = tick - prevTick;
                if (delta > 0) elapsedMs += delta * MsPerTick(currentBpm, currentSig.beat);
                if (ev is Event.Bpm bpm) currentBpm = bpm.Value;
                else if (ev is Event.TimeSignature sig) currentSig = (sig.Beat, sig.Note);
                prevTick = tick;
            }
            return elapsedMs;
        }

        // Buttons and lasers are the user-facing gameplay events. Slams are
        // a Laser subclass so OfType<Laser> covers them too.
        private static bool IsGameplayEvent(Event ev)
        {
            return ev is Event.Button || ev is Event.Laser || ev is Event.Slam;
        }

        // Shift every event in the chart forward by `measures` measures
        // (192 ticks per measure regardless of signature, keeping all beat
        // alignment intact). Inserts copies of the initial BPM and
        // TimeSignature events at (1,1,0) so the new lead-in plays at the
        // correct musical state.
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
            var initialBpmEvent = Events.OfType<Event.Bpm>().OrderBy(e =>
                e.Time.GetAbsoluteOffset((Events.GetTimeSignature(e.Time).Beat, Events.GetTimeSignature(e.Time).Note))
            ).FirstOrDefault();
            var initialSigEvent = Events.OfType<Event.TimeSignature>().OrderBy(e =>
                e.Time.GetAbsoluteOffset((e.Beat, e.Note))
            ).FirstOrDefault();

            // Shift everything by `measures` measures. Time.Measure is just
            // an int, so this is a clean offset — no tick math needed.
            foreach (var ev in Events)
            {
                ev.Time.Measure += measures;
            }
            MeasureCount += measures;

            // Re-establish initial state at (1,1,0). Only add a stand-in BPM
            // event if there isn't already one we shifted (always true post-
            // shift); same for signature.
            if (initialBpmEvent != null)
                Events.Add(new Event.Bpm(Time.Initial, initBpmValue));
            if (initialSigEvent != null)
                Events.Add(new Event.TimeSignature(Time.Initial, initSigTuple.beat, initSigTuple.note));

            // Convert the shift to ms under the initial BPM/sig — the audio
            // pipeline uses this to prepend the matching silence.
            int shiftedTicks = measures * 192;
            return (int)Math.Round(shiftedTicks * MsPerTick(initBpmValue, initSigTuple.beat));
        }
    }
}
