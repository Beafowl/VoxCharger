using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VoxCharger
{
    public partial class Ksh
    {
        private static readonly Encoding DefaultEncoding = Encoding.GetEncoding("Shift_JIS");
        private const string VolPositions = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmno";

        private const int DefaultBackgroundId = 88;
        private const int DefaultGenre = 16;

        public string Title          { get; set; }
        public string Artist         { get; set; }
        public string Effector       { get; set; }
        public string Illustrator    { get; set; }
        public string JacketFileName { get; set; }
        public string MusicFileName  { get; set; }
        public int Volume            { get; set; }
        public Difficulty Difficulty { get; set; }
        public int Level             { get; set; }
        public int MusicOffset       { get; set; }
        public int PreviewOffset     { get; set; }
        public float BpmMin          { get; set; }
        public float BpmMax          { get; set; }
        // First single-value t= the parser sees in the header. Used to convert
        // the KSH 'o=' music-offset (milliseconds) into VOX ticks when we
        // realign positions — the conversion depends on BPM, and the chart's
        // initial BPM is the right reference for a fixed intro offset.
        public float InitialBpm      { get; private set; }
        public string Background     { get; set; }
        public int MeasureCount      { get; private set; }
        public List<string> FxLog    { get; set; }

        public EventCollection Events { get; private set; } = new EventCollection();

        public class ParseOption
        {
            // After parsing, thin laser density beyond the caps below. Off by default: the
            // game's own charts carry up to 128 laser positions per measure, so the caps
            // remove real points (and slams). Kept for charts whose dense laser clusters
            // desynchronise the player.
            public bool SanitizeLasers { get; set; } = false;

            // Max total laser events (Start + Tick + End) per track per
            // measure. 32 is in line with the densest official charts.
            public int MaxLaserEventsPerMeasure { get; set; } = 32;

            // Max slam events per track per beat. Slams are the heaviest
            // events for the player — 8/beat is already ridiculous.
            public int MaxLaserSlamsPerBeat { get; set; } = 8;

            public class SoundFxOptions
            {
                public bool Chip  { get; set; } = true;
                public bool Long  { get; set; } = true;
                public bool Laser { get; set; } = true;
            }

            public class CameraOptions
            {
                public bool SlamImpact { get; set; } = true;
                public Dictionary<Camera.WorkType, bool> EnabledWorks { get; set; } = new Dictionary<Camera.WorkType, bool>();

                public bool IsEnabled(Camera.WorkType work)
                {
                    // Non-configured work should be enabled by default
                    return !EnabledWorks.ContainsKey(work) || EnabledWorks[work];
                }
            }

            public class TrackOptions
            {
                public Dictionary<Event.ButtonTrack, bool> EnabledButtonTracks { get; set; } = new Dictionary<Event.ButtonTrack, bool>();
                public Dictionary<Event.LaserTrack, bool>  EnabledLaserTracks { get; set; }  = new Dictionary<Event.LaserTrack, bool>();

                public bool IsButtonTrackEnabled(Event.ButtonTrack track)
                {
                    return !EnabledButtonTracks.ContainsKey(track) || EnabledButtonTracks[track];
                }

                public bool IsLaserTrackEnabled(Event.LaserTrack track)
                {
                    return !EnabledLaserTracks.ContainsKey(track) || EnabledLaserTracks[track];
                }
            }

            public bool RealignOffset     { get; set; } = false;

            // After parsing finishes, shift every event forward by this many
            // whole measures and re-establish the initial BPM / signature at
            // (1,1,0). 0 = no shift. Used by Program.ApplyLeadInAndTail to
            // give players a moment of silence before the first note on
            // charts whose KSH places gameplay events at chart tick 0. Whole-
            // measure shifts keep all notes on their original beat grid.
            public int LeadInMeasures     { get; set; } = 0;

            public SoundFxOptions SoundFx { get; set; } = new SoundFxOptions();
            public CameraOptions  Camera  { get; set; } = new CameraOptions();
            public TrackOptions Track     { get; set; } = new TrackOptions();

            // Shallow copy, so per-chart values like LeadInMeasures can be set without
            // touching options shared between threads (sub options are only read)
            public ParseOption Clone()
            {
                return (ParseOption)MemberwiseClone();
            }
        }

        public Ksh()
        {
        }

        public VoxHeader ToHeader()
        {
            return new VoxHeader()
            {
                Title            = Title,
                TitleYomigana    = "ダミー",
                Artist           = Artist,
                ArtistYomigana   = "ダミー",
                BpmMin           = BpmMin,
                BpmMax           = BpmMax,
                Volume           = Volume > 0 ? (short)Volume : (short)80,
                DistributionDate = DateTime.Now,
                BackgroundId     = DefaultBackgroundId,
                GenreId          = DefaultGenre,
            };
        }

        public VoxLevelHeader ToLevelHeader()
        {
            return new VoxLevelHeader
            {
                Difficulty  = Difficulty,
                Illustrator = Illustrator,
                Effector    = Effector,
                Level       = Level,
                Radar       = RadarCalculator.Calculate(Events, Level)
            };
        }

        // Note lines look like "0000|00|--" (BT|FX|lasers, optionally followed by spin data).
        // Anything else that happens to start with a digit (stray "1" lines exist in the wild)
        // is ignored, the line parser reads fixed columns up to index 9.
        private static bool IsNoteLine(string line)
        {
            return line.Length >= 10 && char.IsDigit(line[0]) && line[4] == '|' && line[7] == '|';
        }

        private static readonly string[] AudioExtensions = { ".ogg", ".mp3", ".wav", ".flac", ".m4a" };

        // Packs that went through some download services have their files renamed
        // (Google Drive turns ' and % into _), while the .ksh still references the
        // original name. When the referenced file doesn't exist, use the file in the
        // chart's folder whose name matches ignoring punctuation and case, and for music
        // fall back to the folder's only audio file. Otherwise the name is kept as-is.
        private static string ResolveReferencedFile(string kshFile, string name, bool isMusic)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(kshFile));
                if (File.Exists(Path.Combine(dir, name)))
                    return name;

                var files  = Directory.GetFiles(dir);
                string key = RelaxedFileName(Path.GetFileName(name));
                var match  = files.Where(f => RelaxedFileName(Path.GetFileName(f)) == key).ToList();
                if (match.Count == 1)
                    return Path.GetFileName(match[0]);

                if (isMusic)
                {
                    var audio = files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
                    if (audio.Count == 1)
                        return Path.GetFileName(audio[0]);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // Names with characters invalid in paths can't be looked up, keep the original
            }

            return name;
        }

        private static string RelaxedFileName(string name)
        {
            return new string(name.Select(c => char.IsLetterOrDigit(c) || c == '.' ? char.ToLowerInvariant(c) : '_').ToArray());
        }

        public void Parse(string fileName, ParseOption opt = null)
        {
            // I pulled all nighters for few days, all for this piece of trash codes :)
            // Reminder: there's lot of "offset" that can be ambiguous between many contexts everywhere
            var lines = File.ReadAllLines(fileName, DefaultEncoding);

            if (opt == null)
                opt = new ParseOption();

            var time       = Time.Initial;
            var signature  = new Event.TimeSignature(time, 4, 4);
            var filter     = Event.LaserFilter.Peak;
            var cameras    = new Dictionary<Camera.WorkType, Camera>();
            var hitFx      = new Dictionary<Event.ButtonTrack, KshSoundEffect>();
            var holdFx     = new Dictionary<Event.ButtonTrack, Effect>();
            var longNotes  = new Dictionary<Event.ButtonTrack, Event.Button>();
            var lasers     = new Dictionary<Event.LaserTrack, List<Event.Laser>>();
            var fxTable    = new List<Effect>();                       // FXBUTTON EFFECT INFO pairs in id order
            var stopEnds   = new HashSet<Event.Bpm>();                 // BPM rows that end a stop
            var pendingStops = new List<(Event.Bpm start, int ticks)>(); // stop ends are placed after parsing
            var explicitBpm = new HashSet<Event.Bpm>();                // BPM rows set by a t= line
            var tiltSeries = new List<Camera.Tilt>();                  // rows of the open manual tilt series
            Camera.Tilt zeroHold = null;                               // open tilt=zero hold
            bool laneHidden = false;

            // laserrange_l/r: pending for the next section, and the range of the running section
            var pendingRange = new Dictionary<Event.LaserTrack, int> { { Event.LaserTrack.Left, 1 }, { Event.LaserTrack.Right, 1 } };
            var sectionRange = new Dictionary<Event.LaserTrack, int> { { Event.LaserTrack.Left, 1 }, { Event.LaserTrack.Right, 1 } };

            int offset    = 0;
            int measure   = 0;

            int measureCount = lines.Count(l => l.Trim() == "--");
            int noteCount = 0;

            // Pre-scan for user-defined FX declarations (#define_fx <name> <params>)
            var fxDefinitions = new Dictionary<string, KshDefinition>();
            foreach (var ln in lines)
            {
                var trimmed = ln.Trim();
                if (!trimmed.StartsWith("#define_fx "))
                    continue;

                // Format: #define_fx <name> <type=...;param=...;...>
                var parts = trimmed.Substring("#define_fx ".Length);
                int sep = parts.IndexOf(' ');
                if (sep <= 0)
                    continue;

                string name = parts.Substring(0, sep).Trim();
                string def  = parts.Substring(sep + 1).Trim();
                fxDefinitions[name] = new KshDefinition(def);
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#"))
                    continue;


                // First measure is 1,1,0 but initial counter start from 0 until actual measure started
                if (measure > 0)
                {
                    /** Time 
                     * Behold! time conversion between ksh format to vox
                     * 
                     * Vox Time Format:
                     * Timestamp value has beat to be matched with current signature
                     * However, the offset is base of 48, this might be lead into confusion in non standard signature
                     * 
                     * For example:
                     * In 8 / 4 signature the maximum timestamp that means:
                     * - Maximum timestamp is 999 / 8 / 48 for each measure, beat and cells respectively
                     * - Value of beat adapt to the signature beat
                     * - May only contain 4 notes per beat in normal behavior since signature note is 4
                     * 
                     * And since offset need to be base of 48 (not 192) this value need to be converted
                     * for example the 3rd note in the beat should be 36 and not 24
                     * 
                     * Ksh Time Format:
                     * This too, depends on beat but the number of actual event definitions can be much greater than the beat itself
                     * for example, measure that has 8/4 signature may contains 441 events, though much of them are just padding
                     * this padding required since the event didn't define timestamp in each line (easier to debug with larger file size tradeoff)
                     */

                    float position = (measure * 192f) + ((offset / (float)noteCount) * 192f);
                    // Apply KSH 'o=' music offset. It's in milliseconds; convert
                    // to VOX ticks using the chart's initial BPM so every event
                    // gets shifted by the same constant. Positive o= means the
                    // audio starts later than chart tick 0 in KSM — in VOX
                    // (where chart and audio share tick 0) that translates to
                    // the chart events being placed EARLIER. The old code
                    // subtracted raw milliseconds from ticks, which is a unit
                    // mismatch; it was also gated behind RealignOffset which
                    // defaulted to false, so in practice no offset was applied
                    // at all — charts like 'Roar of Chronos' (o=1400 @ 175 BPM
                    // -> 196 ticks, one whole note) ended up offset by a
                    // whole musical note from where they should be.
                    // NOTE: MusicOffset is applied to the *audio* during encode
                    // (see LoudnessNormalizer.Normalize / AudioImportOptions.
                    // MusicOffsetMs), not to chart positions here. That keeps
                    // every event exactly on the KSH tick grid; the audio is
                    // padded or trimmed to absorb the ms offset.

                    // Magic happens here!
                    time = Time.FromOffset(position, signature);
                    if (time.Measure < 0)
                        continue; // Might happen when try to realign music offset

                    if (time.Beat > signature.Beat)
                        Debug.WriteLine($"[KSH] {i + 1:D4}: Measure has more than {signature.Beat} beats");
                }

                if (line.Contains("="))
                {
                    // Might just pull it to first bar
                    if (time.Measure == 0)
                        time = Time.Initial;

                    var data = line.Split('=');
                    if (data.Length < 2)
                    {
                        Debug.WriteLine($"[KSH] {i+1:D4}: Invalid attribute");
                        continue;
                    }

                    string prop  = data[0].Trim().ToLower();
                    string value = data[1].Trim();
                    var param    = value.Split(';');
                    if (string.IsNullOrEmpty(value))
                        Debug.WriteLine($"[KSH] {i + 1:D4}: {prop} value is empty (reset value)");

                    /** WARNING !!!
                     * Cancer code ahead, proceed with caution!
                     * Well.. the rest of codes aren't any better either lol
                     */
                    int maxDuration = (measureCount - measure) * 192;
                    switch (prop) // At first i just want try to use switch-case pattern, turns out became cancerous (indent and) code lol
                    {
                        #region --- Metadata ---
                        case "title":       Title          = value; break;
                        case "artist":      Artist         = value; break;
                        case "effect":      Effector       = value; break;
                        case "illustrator": Illustrator    = value; break;
                        case "jacket":      JacketFileName = ResolveReferencedFile(fileName, value, false); break;
                        // m=main.ogg;main_f.ogg lists the music plus an effected variant, the first one is the music
                        case "m":           MusicFileName  = ResolveReferencedFile(fileName, param[0].Trim(), true); break;
                        case "po"    when int.TryParse(value, out int po):  PreviewOffset = po;                  break;
                        case "mvol"  when int.TryParse(value, out int vol): Volume        = vol;                 break;
                        case "level" when int.TryParse(value, out int lv):  Level         = lv;                  break;
                        case "difficulty" when value == "light":            Difficulty    = Difficulty.Novice;   break;
                        case "difficulty" when value == "challenge":        Difficulty    = Difficulty.Advanced; break;
                        case "difficulty" when value == "extended":         Difficulty    = Difficulty.Exhaust;  break;
                        case "difficulty" when value == "infinite":         Difficulty    = Difficulty.Infinite; break;
                        case "bg"    when string.IsNullOrEmpty(Background):
                        case "layer" when string.IsNullOrEmpty(Background):
                            Background = value;
                            break;
                        #endregion
                        #region --- Event Info ---
                        case "o" when int.TryParse(value, out int o): 
                            MusicOffset = o;

                            // Should be fine if there's no bpm change
                            if (MusicOffset % 48 != 0 && opt.RealignOffset)
                                Debug.WriteLine($"[KSH] Music Offset is not base of 48 signature (Offset: {MusicOffset})");

                            break;
                        case "t":
                            foreach (string ts in value.Split('-'))
                            {
                                if (!float.TryParse(ts, out float t))
                                    break;

                                if (BpmMin == 0f || t < BpmMin)
                                    BpmMin = t;

                                if (BpmMax == 0f || t > BpmMax)
                                    BpmMax = t;

                                if (!value.Contains("-"))
                                {
                                    // BPM changes keep their exact position, stock charts place them on any cell
                                    var existing = Events[time].OfType<Event.Bpm>().LastOrDefault();
                                    if (existing != null)
                                    {
                                        existing.Value = t;
                                        explicitBpm.Add(existing);
                                    }
                                    else if (Events.GetBpm(time)?.Value != t)
                                    {
                                        var change = new Event.Bpm(time, t);
                                        Events.Add(change);
                                        explicitBpm.Add(change);
                                    }

                                    // Capture the first explicit single-value BPM as the initial BPM
                                    // for offset-to-ticks conversion.
                                    if (InitialBpm == 0f)
                                        InitialBpm = t;
                                }
                            }

                            // BPM change with unaligned music offset, proceed with caution!
                            if (BpmMin != BpmMax && MusicOffset % 48 != 0 && opt.RealignOffset)
                                Debug.WriteLine($"[KSH] BPM change with unaligned music offset");

                            break;
                        case "stop" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float duration):
                        {
                            // One KSH stop unit is 1/192 whole note, i.e. one cell. The pause starts on the
                            // exact line position and the game keeps its note timing (scroll stop only).
                            int stopTicks = (int)Math.Round(duration);
                            if (stopTicks <= 0)
                                break;

                            var start = Events[time].OfType<Event.Bpm>().LastOrDefault();
                            if (start == null)
                            {
                                var last = Events.GetBpm(time);
                                if (last == null)
                                    break;

                                start = new Event.Bpm(time, last.Value);
                                Events.Add(start);
                            }
                            start.IsStop = true;
                            pendingStops.Add((start, stopTicks));
                            break;
                        }
                        case "beat": 
                            var sig = value.Split('/');
                            if (sig.Length != 2 || !int.TryParse(sig[0], out int b) || !int.TryParse(sig[1], out int n))
                                break;

                            // Signature changes sit on a measure start; placed after the measure's first note
                            // line it applies to the next measure (KSH spec). The last one in a measure wins.
                            var sigTime = new Time(offset > 0 ? time.Measure + 1 : time.Measure, 1, 0);
                            foreach (var previous in Events[sigTime].OfType<Event.TimeSignature>().ToList())
                                Events.Remove(previous);

                            signature = new Event.TimeSignature(sigTime, b, n);
                            Events.Add(signature);

                            break;
                        case "fx-l":
                        case "fx-r":
                            if (!opt.SoundFx.Long)
                                break;

                            var htrack  = prop == "fx-l" ? Event.ButtonTrack.FxL : Event.ButtonTrack.FxR;
                            holdFx[htrack] = new Effect();

                            var fx = Effect.FromKsh(value);

                            // Resolve user-defined FX by name lookup
                            string[] fxParts = value.Trim().Split(';');
                            string fxName = fxParts[0].Trim();
                            if (fx == null && fxDefinitions.TryGetValue(fxName, out var fxDef))
                            {
                                // Apply inline parameter override (e.g., "ret1;16" → waveLength=1/16)
                                string paramName = null;
                                string origValue = null;
                                if (fxParts.Length > 1 && !string.IsNullOrEmpty(fxParts[1].Trim()))
                                {
                                    string inlineParam = fxParts[1].Trim();
                                    fxDef.GetString("type", out string fxType);
                                    paramName = GetInlineParamName(fxType);
                                    if (paramName != null)
                                    {
                                        // Save original value to restore after parsing
                                        fxDef.GetString(paramName, out origValue);

                                        // For waveLength/period params, convert to fraction format (16 → 1/16)
                                        if (paramName == "waveLength" || paramName == "period")
                                            fxDef.SetValue(paramName, $"1/{inlineParam}");
                                        else
                                            fxDef.SetValue(paramName, inlineParam);
                                    }
                                }

                                fx = Effect.FromKsh(fxDef);

                                // Restore original definition value so subsequent uses aren't affected
                                if (paramName != null)
                                {
                                    if (origValue != null)
                                        fxDef.SetValue(paramName, origValue);
                                    else
                                        fxDef.RemoveValue(paramName);
                                }
                            }

                            if (fx != null)
                            {
                                // The effect gets its table id when a hold uses it, so identical
                                // definitions share one FXBUTTON EFFECT INFO pair
                                holdFx[htrack] = fx;
                                FxLog?.Add($"{prop}={value} → Type={fx.Type}({(int)fx.Type}) Output=[{fx}]");
                            }
                            else
                            {
                                FxLog?.Add($"{prop}={value} → FAILED (null)");
                            }

                            break;
                        case "fx-l_se":
                        case "fx-r_se":
                            if (!opt.SoundFx.Chip)
                                break;

                            var fxTrack = prop == "fx-l_se" ? Event.ButtonTrack.FxL : Event.ButtonTrack.FxR;
                            hitFx[fxTrack] = new KshSoundEffect();
                            
                            hitFx[fxTrack].Effect = Event.ChipFxFromKsh(param[0]);

                            if (param.Length >= 2 && int.TryParse(param[1], out int hitCount))
                                hitFx[fxTrack].HitCount = hitCount + 1;
                            else
                                hitFx[fxTrack].HitCount = 1;

                            break;
                        case "filtertype":
                            if (!opt.SoundFx.Laser)
                            {
                                filter = Event.LaserFilter.None;
                                break;
                            }

                            switch (value.Trim())
                            {
                                case "peak": filter = Event.LaserFilter.Peak; break;
                                case "hpf1": filter = Event.LaserFilter.HighPass; break;
                                case "lpf1": filter = Event.LaserFilter.LowPass; break;
                                case var f when f.Contains("bitc"):
                                    filter = Event.LaserFilter.BitCrusher;
                                    break;
                                default:
                                    filter = (Event.LaserFilter)6;
                                    break;
                            }

                            break;
                        case "laserrange_l" when int.TryParse(value.Replace("x", ""), out int r): pendingRange[Event.LaserTrack.Left]  = r >= 2 ? 2 : 1; break;
                        case "laserrange_r" when int.TryParse(value.Replace("x", ""), out int r): pendingRange[Event.LaserTrack.Right] = r >= 2 ? 2 : 1; break;
                        #endregion
                        #region --- Camera ---
                        case "tilt" when !float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float _):
                        {
                            if (!opt.Camera.IsEnabled(Camera.WorkType.Tilt))
                                break;

                            // A keyword ends a manual tilt series or a zero hold
                            EndManualTilt(time, cameras, tiltSeries);
                            CloseZeroHold(time, ref zeroHold);

                            switch (value.Trim().ToLowerInvariant())
                            {
                                case "zero":
                                    // No tilt at all: hold the lane level until the next tilt setting
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Normal));
                                    zeroHold = new Camera.Tilt(time, 0, 0f, 0f) { NodeType = Camera.Tilt.NodeSingle };
                                    Events.Add(zeroHold);
                                    break;
                                case "normal":
                                case "keep_normal":
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Normal));
                                    break;
                                case "bigger":
                                case "biggest":
                                case "big":
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Large));
                                    break;
                                case "keep_bigger":
                                case "keep_biggest":
                                case "keep":
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Incremental));
                                    break;
                            }

                            break;
                        }
                        case "lane_toggle" when int.TryParse(value.Trim(), out int laneTicks):
                        {
                            if (!opt.Camera.IsEnabled(Camera.WorkType.LaneClear))
                                break;

                            // Alternates between hiding and showing the lane (stock charts: LaneY 0 -> 1024 hides)
                            laneHidden = !laneHidden;
                            Events.Add(new Camera.LaneClear(time, Math.Max(0, laneTicks), laneHidden ? 0f : 1024f, laneHidden ? 1024f : 0f));
                            break;
                        }
                        case "tilt":        // Tilt
                        case "zoom_top":    // CAM_RotX
                        case "zoom_bottom": // CAM_Radi
                        {
                            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float cameraVal))
                                break;

                            var work = Camera.WorkType.None;
                            switch (prop)
                            {
                                case "zoom_top":
                                    // CAM_RotX: soft-clip via tanh so values in the [-100, 100]
                                    // KSM range stay near-linear, while extremes (e.g. 150+)
                                    // saturate at ±1.0 instead of producing lane angles that
                                    // Konami's own charts never use.
                                    work = Camera.WorkType.Rotation;
                                    cameraVal = (float)Math.Tanh(cameraVal / 100.0f);
                                    break;
                                case "zoom_bottom":
                                    work = Camera.WorkType.Radian;
                                    cameraVal = (float)Math.Tanh(cameraVal / -100.0f);
                                    break;
                                case "tilt":
                                    work = Camera.WorkType.Tilt;
                                    cameraVal /= -1.0f; // the game's tilt is positive to the left
                                    break;
                            }

                            if (!opt.Camera.IsEnabled(work))
                                break;

                            int currentTick = Events.ToTicks(time);
                            cameras.TryGetValue(work, out var lastCamera);

                            if (work == Camera.WorkType.Tilt)
                            {
                                CloseZeroHold(time, ref zeroHold);
                                if (lastCamera == null)
                                {
                                    // First value of a manual series: from level to the value
                                    var first = new Camera.Tilt(time, 0, 0f, cameraVal) { NodeType = Camera.Tilt.NodeStart };
                                    Events.Add(first);
                                    tiltSeries.Add(first);
                                }
                            }
                            else if (lastCamera == null)
                            {
                                // KSM applies the first value from the chart start
                                var init = Camera.Create(work, Time.Initial, 0, 0.00f, cameraVal);
                                var pair = Camera.Create(work, Time.Initial, currentTick, cameraVal, cameraVal);
                                Events.Add(init, pair);
                            }

                            if (lastCamera != null)
                            {
                                // The previous keyframe ramps linearly to this one (durations in real ticks)
                                lastCamera.End      = cameraVal;
                                lastCamera.Duration = currentTick - Events.ToTicks(lastCamera.Time);
                            }

                            var camera = Camera.Create(work, time, 0, cameraVal, cameraVal);
                            if (camera is Camera.Tilt tiltRow)
                                tiltSeries.Add(tiltRow);

                            cameras[work] = camera;
                            Events.Add(camera);
                            break;
                        }
                        #endregion
                    }
                }
                else if (line == "--")
                {
                    // Reset time counter
                    measure  += 1;
                    offset    = 0;
                    noteCount = 0;

                    // A laserrange option only applies to a section starting in the same measure
                    pendingRange[Event.LaserTrack.Left]  = 1;
                    pendingRange[Event.LaserTrack.Right] = 1;

                    float position = measure * 192f;
                    // MusicOffset is applied to the *audio* during encode
                    // (LoudnessNormalizer.Normalize via AudioImportOptions.
                    // MusicOffsetMs), not to chart positions. Applying it
                    // here too would double-shift every event after measure 1
                    // and also introduce sub-beat residues.
                    time = Time.FromOffset(position, signature);
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string ln = lines[j].Trim();
                        if (!string.IsNullOrEmpty(ln))
                        {
                            if (IsNoteLine(ln))
                                noteCount++;
                            else if (ln == "--")
                                break;
                        }
                    }
                }
                else if (IsNoteLine(line))
                {
                    // Increment offset when current line is event
                    offset++;

                    #region --- BT & FX ---
                    for (int channel = 0; channel < 7; channel++)
                    {
                        Event.ButtonTrack track;
                        switch(channel)
                        {
                            case 0: track = Event.ButtonTrack.A; break;
                            case 1: track = Event.ButtonTrack.B; break;
                            case 2: track = Event.ButtonTrack.C; break;
                            case 3: track = Event.ButtonTrack.D; break;
                            case 5: track = Event.ButtonTrack.FxL; break;
                            case 6: track = Event.ButtonTrack.FxR; break;
                            default: continue;
                        }

                        if (!opt.Track.IsButtonTrackEnabled(track))
                            continue;

                        bool   isFx     = track == Event.ButtonTrack.FxL || track == Event.ButtonTrack.FxR;
                        char   cell     = line[channel];
                        string legacyFx = null;
                        int    flag;
                        if (char.IsDigit(cell))
                            flag = cell - '0';
                        else if (isFx && LegacyFxEffects.TryGetValue(cell, out legacyFx))
                            flag = 1; // KSH 1.x long FX note, the letter is its effect
                        else
                            continue;

                        // BT: 1 chip, 2 hold. FX: 2 chip, anything else hold
                        bool isChip = ((isFx && flag == 2) || (!isFx && flag == 1)) && flag != 0;
                        bool isHold = ((isFx && flag != 2) || (!isFx && flag == 2)) && flag != 0;

                        if (isChip)
                        {
                            var hit = Event.ChipFx.None;
                            if (isFx)
                            {
                                if (!hitFx.ContainsKey(track))
                                    hitFx[track] = new KshSoundEffect();

                                var fx = hitFx[track];
                                hit    = fx.HitCount > fx.Used ? fx.Effect : Event.ChipFx.None;

                                fx.Used++;
                            }

                            // A chip right after a hold ends the hold here
                            if (longNotes.TryGetValue(track, out var endedByChip))
                            {
                                endedByChip.HoldLength = Events.ToTicks(time) - Events.ToTicks(endedByChip.Time);
                                Events.Add(endedByChip);
                                longNotes.Remove(track);
                            }

                            Events.Add(new Event.Button(time, track, 0, null, hit));
                        }
                        else if (isHold)
                        {
                            if (!longNotes.ContainsKey(track))
                            {
                                Effect fx = null;
                                if (isFx)
                                {
                                    if (legacyFx != null)
                                        fx = Effect.FromKsh(legacyFx);
                                    else
                                        holdFx.TryGetValue(track, out fx);

                                    fx = RegisterEffect(fxTable, fx);
                                }

                                longNotes[track] = new Event.Button(time, track, 0, fx, Event.ChipFx.None);
                            }
                        }
                        else if (longNotes.TryGetValue(track, out var ended))
                        {
                            // Empty cell: the hold ended on this line
                            ended.HoldLength = Events.ToTicks(time) - Events.ToTicks(ended.Time);
                            Events.Add(ended);
                            longNotes.Remove(track);
                        }
                    }
                    #endregion

                    #region  --- Laser ---
                    // Lane spin after the laser characters: @( @) full spin, @< @> half spin (a swing in the
                    // game), S< S> swing; the number is the length in 1/192 whole notes. The roll length is
                    // written in quarter notes, twice the KSH length (the rule hand-made conversions follow).
                    var spinImpact = Event.SlamImpact.None;
                    int spinLength = 0;
                    if (line.Length > 12 && opt.Camera.SlamImpact)
                    {
                        char kind = line[10];
                        char dir  = line[11];
                        if ((kind == '@' || kind == 'S') && (dir == '(' || dir == ')' || dir == '<' || dir == '>'))
                        {
                            string digits = new string(line.Substring(12).TakeWhile(char.IsDigit).ToArray());
                            int.TryParse(digits, out int kshLength);
                            spinImpact = kind == '@' && (dir == '(' || dir == ')') ? Event.SlamImpact.Measure : Event.SlamImpact.Swing;
                            spinLength = Math.Max(1, (int)Math.Round((kshLength > 0 ? kshLength : 192) / 24.0));
                        }
                    }

                    foreach (var track in new[] { Event.LaserTrack.Left, Event.LaserTrack.Right })
                    {
                        if (!opt.Track.IsLaserTrackEnabled(track))
                            continue;

                        int  channel = track == Event.LaserTrack.Left ? 8 : 9;
                        char flag    = line[channel];

                        if (flag == ':')
                            continue; // continues the current segment

                        if (flag == '-')
                        {
                            if (lasers.TryGetValue(track, out var nodes))
                            {
                                lasers.Remove(track);
                                FinishLaserSection(track, nodes);
                            }

                            continue;
                        }

                        int position = VolPositions.IndexOf(flag);
                        if (position < 0)
                            continue;

                        bool starts = !lasers.ContainsKey(track);
                        if (starts)
                        {
                            lasers[track]       = new List<Event.Laser>();
                            sectionRange[track] = pendingRange[track]; // laserrange applies to this section only
                            pendingRange[track] = 1;
                        }

                        // 51 KSH steps onto the 0..127 VOX range
                        var laser = new Event.Laser(time, track, (int)Math.Round(position / 50.0 * 127.0), starts ? Event.LaserFlag.Start : Event.LaserFlag.Tick, spinImpact, filter, sectionRange[track]);
                        laser.RollLength = spinImpact != Event.SlamImpact.None ? spinLength : 0;
                        lasers[track].Add(laser);
                    }
                    #endregion
                }
            }

            // The header's "--" opens the first measure and every later one closes a measure;
            // a trailing block without note lines (comments, #define_fx) is not a measure
            MeasureCount = offset == 0 ? Math.Max(0, measure - 1) : measure;
            int chartEndTick = Events.MeasureStartTick(MeasureCount + 1);

            // Holds and lasers still open at the end of the chart end with it
            foreach (var openHold in longNotes.Values)
            {
                openHold.HoldLength = Math.Max(0, chartEndTick - Events.ToTicks(openHold.Time));
                Events.Add(openHold);
            }
            longNotes.Clear();

            foreach (var section in lasers.ToList())
                FinishLaserSection(section.Key, section.Value);
            lasers.Clear();

            // A manual tilt still active at the end holds through it
            if (tiltSeries.Count > 0)
            {
                tiltSeries[0].NodeType = tiltSeries.Count == 1 ? Camera.Tilt.NodeSingle : Camera.Tilt.NodeStart;
                if (tiltSeries.Count > 1)
                    tiltSeries[tiltSeries.Count - 1].NodeType = Camera.Tilt.NodeEnd;
                tiltSeries.Clear();
            }
            CloseZeroHold(Events.FromTicks(chartEndTick), ref zeroHold);

            // Stop ends are placed now that every signature change is known (a stop may run into
            // measures with another signature)
            foreach (var (start, ticks) in pendingStops)
            {
                var target = Events.FromTicks(Events.ToTicks(start.Time) + ticks);
                var end    = Events[target].OfType<Event.Bpm>().LastOrDefault();
                if (end != null && end.IsStop)
                    continue; // runs straight into the next stop

                if (end == null)
                {
                    end = new Event.Bpm(target, Events.GetBpm(target)?.Value ?? start.Value);
                    Events.Add(end);
                }

                end.IsStop = false;
                stopEnds.Add(end);
            }

            // A stop ends with whatever BPM is in effect there, unless a t= sits on that line
            foreach (var end in stopEnds)
            {
                if (explicitBpm.Contains(end))
                    continue;

                int endTick = Events.ToTicks(end.Time);
                var active  = Events.OfType<Event.Bpm>()
                    .Where(b => (explicitBpm.Contains(b) || !stopEnds.Contains(b)) && Events.ToTicks(b.Time) <= endTick)
                    .OrderBy(b => Events.ToTicks(b.Time))
                    .LastOrDefault();
                if (active != null)
                    end.Value = active.Value;
            }

            // The game pauses from a "4-" row until the next row without the dash, so a BPM change
            // inside a stop has to keep the flag or it would end the stop early
            bool inStop = false;
            foreach (var bpm in Events.OfType<Event.Bpm>().OrderBy(b => Events.ToTicks(b.Time)).ToList())
            {
                if (stopEnds.Contains(bpm))
                    inStop = false;
                else if (bpm.IsStop)
                    inStop = true;
                else if (inStop)
                    bpm.IsStop = true;
            }

            // Some KSH charts append massive trailing padding after the last
            // real gameplay event — Internet Yamero's MXM ends with a
            // 'beat=128/1' measure that adds ~166 seconds of silence, pushing
            // the VOX EndPosition well past the audio duration. In-game the
            // song can't auto-end because the chart still thinks it's
            // running; the player is stuck on a silent track.
            //
            // Heuristic: only trim when a late-in-the-chart signature change
            // introduces an extreme ratio. 'Late' = within the last 5 measures
            // (padding hacks always sit at the tail). 'Extreme' = >= 16 whole
            // notes per measure (4/4=1, 3/8=0.375, 50/4=12.5, 128/1=128 — so
            // a 128/1 trips, a normal 50/4 measure doesn't). A legit 16/1 or
            // larger in the middle of a chart isn't touched.
            int padStartMeasure = int.MaxValue;
            foreach (var ev in Events)
            {
                if (ev is Event.TimeSignature sig && sig.Note > 0 && sig.Time.Measure > MeasureCount - 5)
                {
                    double wholesPerMeasure = (double)sig.Beat / sig.Note;
                    if (wholesPerMeasure >= 16 && sig.Time.Measure < padStartMeasure)
                        padStartMeasure = sig.Time.Measure;
                }
            }
            if (padStartMeasure != int.MaxValue && padStartMeasure <= MeasureCount)
            {
                Console.WriteLine($"[ksh] Trimming trailing padding measure(s) starting at {padStartMeasure}: MeasureCount {MeasureCount} -> {padStartMeasure - 1}");
                MeasureCount = padStartMeasure - 1;
            }

            if (opt.SanitizeLasers)
                SanitizeLasers(opt.MaxLaserEventsPerMeasure, opt.MaxLaserSlamsPerBeat);

            // Auto-applied lead-in: gives players a moment of silence before
            // the first note on charts whose first event sits at tick 0.
            // Caller (Program.ApplyLeadInAndTail) sets the measure count;
            // we apply it here so every chart that goes through Parse —
            // including the per-difficulty re-parses inside Ksh.Exporter —
            // gets the same shift, keeping all difficulties in sync.
            if (opt.LeadInMeasures > 0)
                PrependLeadInMeasures(opt.LeadInMeasures);
        }

        // Thin excessive laser density in-place on the parsed event list. Two
        // caps, applied per laser track (L and R):
        //
        //   Cap A - max slams per beat. Any slam events beyond the cap in the
        //           same (measure, beat) are removed, preferring Tick-flag
        //           events over Start/End so the segment structure survives.
        //
        //   Cap B - max total laser events (Start + Tick + End) per measure.
        //           After Cap A, any measure still over the cap is thinned by
        //           uniform sampling; Start and End flags are always kept so
        //           we never orphan a laser segment.
        //
        // The caller logs how many events were removed. Permanent on-disk data
        // is untouched — we only modify the in-memory Events list.
        private void SanitizeLasers(int maxEventsPerMeasure, int maxSlamsPerBeat)
        {
            var byTrack = new Dictionary<Event.LaserTrack, List<Event.Laser>>();
            foreach (var ev in Events)
            {
                if (ev is Event.Laser l)
                {
                    if (!byTrack.ContainsKey(l.Track))
                        byTrack[l.Track] = new List<Event.Laser>();
                    byTrack[l.Track].Add(l);
                }
            }

            var toRemove = new HashSet<Event.Laser>();

            // We cap by "laser objects" — a logical event at one (measure,
            // beat, offset). A slam is 2 events (Start + End) at the SAME
            // tick; a normal tick is 1 event. Counting raw events would let
            // a slam-dense measure like 45 slams (= 90 events) sail under
            // any per-event cap because every event is flagged Start or
            // End. Counting "time positions" collapses slams to 1 unit each,
            // which matches the player's processing load.
            foreach (var kv in byTrack)
            {
                var byTick = kv.Value
                    .GroupBy(l => (l.Time.Measure, l.Time.Beat, l.Time.Offset))
                    .ToList();

                // Cap A: slam clusters per beat. A slam-pair ticks group has
                // 2 events (Start + End) at the same offset. We count groups
                // that look like slams (>=2 events at same tick) per beat.
                var slamGroupsByBeat = byTick
                    .Where(g => g.Count() >= 2 || g.Any(l => l.IsLaserSlam))
                    .GroupBy(g => (g.Key.Measure, g.Key.Beat))
                    .Where(bg => bg.Count() > maxSlamsPerBeat);

                foreach (var bg in slamGroupsByBeat)
                {
                    var orderedGroups = bg.OrderBy(g => g.Key.Offset).ToList();
                    // Keep the first maxSlamsPerBeat groups, drop the rest.
                    for (int i = maxSlamsPerBeat; i < orderedGroups.Count; i++)
                    {
                        foreach (var e in orderedGroups[i])
                            toRemove.Add(e);
                    }
                }

                // Cap B: total laser *objects* (time positions) per measure.
                // After Cap A, re-bucket survivors and enforce the cap.
                var survivorGroups = kv.Value
                    .Where(l => !toRemove.Contains(l))
                    .GroupBy(l => (l.Time.Measure, l.Time.Beat, l.Time.Offset))
                    .ToList();

                var perMeasureGroups = survivorGroups
                    .GroupBy(g => g.Key.Measure)
                    .Where(mg => mg.Count() > maxEventsPerMeasure);

                foreach (var mg in perMeasureGroups)
                {
                    var orderedGroups = mg
                        .OrderBy(g => g.Key.Beat)
                        .ThenBy(g => g.Key.Offset)
                        .ToList();
                    int total = orderedGroups.Count;
                    int keep  = maxEventsPerMeasure;

                    // Uniform-sample group indices to keep.
                    var keepIdx = new HashSet<int>();
                    for (int i = 0; i < keep; i++)
                    {
                        int idx = (int)((double)i * total / keep);
                        if (idx >= total) idx = total - 1;
                        keepIdx.Add(idx);
                    }

                    for (int i = 0; i < total; i++)
                    {
                        if (keepIdx.Contains(i)) continue;
                        foreach (var e in orderedGroups[i])
                            toRemove.Add(e);
                    }
                }
            }

            if (toRemove.Count > 0)
            {
                Console.WriteLine($"[sanitize-lasers] thinned {toRemove.Count} laser event(s) (caps: {maxEventsPerMeasure} objects/measure, {maxSlamsPerBeat} slams/beat)");
                foreach (var e in toRemove)
                    Events.Remove(e);
            }
        }

        private static string GetInlineParamName(string fxType)
        {
            switch (fxType)
            {
                case "Retrigger":
                case "Echo":
                case "Gate":
                case "Wobble":     return "waveLength";
                case "SideChain":  return "period";
                case "TapeStop":   return "speed";
                case "Flanger":    return "period";
                case "BitCrusher": return "reduction";
                case "PitchShift": return "pitch";
                case "Phaser":     return "period";
                default:           return null;
            }
        }

        // KSH 1.x wrote the effect of a long FX note as a letter in the FX lane
        private static readonly Dictionary<char, string> LegacyFxEffects = new Dictionary<char, string>
        {
            { 'S', "Retrigger;8" },  { 'V', "Retrigger;12" }, { 'T', "Retrigger;16" }, { 'W', "Retrigger;24" },
            { 'U', "Retrigger;32" }, { 'G', "Gate;4" },       { 'H', "Gate;8" },        { 'K', "Gate;12" },
            { 'I', "Gate;16" },      { 'L', "Gate;24" },      { 'J', "Gate;32" },       { 'F', "Flanger" },
            { 'P', "PitchShift" },   { 'B', "BitCrusher" },   { 'Q', "Phaser" },        { 'X', "Wobble;12" },
            { 'A', "TapeStop" },     { 'D', "SideChain" },
        };

        // Stock charts define at most this many FXBUTTON EFFECT INFO pairs
        public const int MaxFxEffectPairs = 12;

        // Gives an effect its place in the FXBUTTON EFFECT INFO table (ids start at 2). Identical
        // definitions share one entry, so a chart that redefines fx-l=Retrigger;8 fifty times gets one pair.
        private static Effect RegisterEffect(List<Effect> table, Effect fx)
        {
            if (fx == null || fx.Type == FxType.None)
                return null;

            string key = fx.ToString();
            foreach (var existing in table)
            {
                if (existing.ToString() == key)
                    return existing;
            }

            fx.Id = table.Count + 2;
            table.Add(fx);
            if (table.Count == MaxFxEffectPairs + 1)
                Console.WriteLine($"[ksh] more than {MaxFxEffectPairs} distinct FX effects, the game's own charts never use more");

            return fx;
        }

        // KSM: two laser points at most 1/32 whole note apart with different positions form a slam
        private const int SlamMaxTicks = 6;

        // Turns the points of one KSH laser section into VOX laser events. A slam becomes two points on
        // the start tick; in a chain of slams the shared point stays on its own tick as the next start;
        // a spin token is kept only on a point that starts a slam.
        private void FinishLaserSection(Event.LaserTrack track, List<Event.Laser> nodes)
        {
            if (nodes.Count < 2)
                return; // KSM needs two points for a section

            // Three equal positions in a row are one straight line, the middle point adds nothing
            for (int n = nodes.Count - 2; n >= 1; n--)
            {
                if (nodes[n].Offset == nodes[n - 1].Offset && nodes[n].Offset == nodes[n + 1].Offset && nodes[n].Impact == Event.SlamImpact.None)
                    nodes.RemoveAt(n);
            }

            var result = new List<Event.Laser>();
            for (int n = 0; n < nodes.Count; n++)
            {
                var point = nodes[n];
                result.Add(point);

                var next = n + 1 < nodes.Count ? nodes[n + 1] : null;
                int gap  = next != null ? Events.ToTicks(next.Time) - Events.ToTicks(point.Time) : int.MaxValue;
                if (next != null && gap <= SlamMaxTicks && next.Offset != point.Offset)
                {
                    point.IsLaserSlam = next.IsLaserSlam = true;

                    var  after   = n + 2 < nodes.Count ? nodes[n + 2] : null;
                    bool chained = after != null && Events.ToTicks(after.Time) - Events.ToTicks(next.Time) <= SlamMaxTicks && after.Offset != next.Offset;
                    if (chained)
                    {
                        // `next` ends this slam and starts the following one: end this slam on the start
                        // tick and leave `next` where it is for the next slam
                        result.Add(new Event.Laser(point.Time, track, next.Offset, Event.LaserFlag.Tick, Event.SlamImpact.None, next.Filter, next.Range));
                    }
                    else
                    {
                        next.Time = point.Time.Clone();
                    }
                }
                else
                {
                    // A spin needs a slam on its line
                    point.Impact     = Event.SlamImpact.None;
                    point.RollLength = 0;
                }
            }

            result[0].Flag = Event.LaserFlag.Start;
            for (int n = 1; n < result.Count; n++)
                result[n].Flag = Event.LaserFlag.Tick;
            result[result.Count - 1].Flag = Event.LaserFlag.End;

            Events.Add(result.ToArray());
        }

        // Ends an open manual tilt series at `time`: the lane snaps back to level, holds it for a beat
        // and the rows are marked as a series so the game resumes the laser-driven tilt afterwards.
        private void EndManualTilt(Time time, Dictionary<Camera.WorkType, Camera> cameras, List<Camera.Tilt> series)
        {
            cameras.TryGetValue(Camera.WorkType.Tilt, out var last);
            cameras[Camera.WorkType.Tilt] = null;

            if (series.Count == 0)
                return;

            var reset   = new Camera.Tilt(time, 0, last?.End ?? 0f, 0f);
            var padding = new Camera.Tilt(time, 48, 0f, 0f) { NodeType = Camera.Tilt.NodeEnd };
            Events.Add(reset, padding);

            series[0].NodeType = Camera.Tilt.NodeStart;
            series.Clear();
        }

        // Closes a tilt=zero hold at `time`
        private void CloseZeroHold(Time time, ref Camera.Tilt zeroHold)
        {
            if (zeroHold == null)
                return;

            zeroHold.Duration = Math.Max(0, Events.ToTicks(time) - Events.ToTicks(zeroHold.Time));
            zeroHold = null;
        }

        public void Import(VoxChart chart)
        {
            Events = chart.Events;

            // Extract BPM range from events
            foreach (var ev in Events)
            {
                if (ev is Event.Bpm bpm)
                {
                    if (BpmMin == 0f || bpm.Value < BpmMin)
                        BpmMin = bpm.Value;
                    if (BpmMax == 0f || bpm.Value > BpmMax)
                        BpmMax = bpm.Value;
                }
            }

            // Calculate MeasureCount from EndPosition
            if (chart.EndPosition != null)
                MeasureCount = chart.EndPosition.Measure - 1;
            else
                MeasureCount = Events.Max(ev => ev.Time.Measure);
        }

        public void Serialize(string fileName)
        {
            var sb = new StringBuilder();

            // Header metadata
            sb.AppendLine($"title={Title ?? ""}");
            sb.AppendLine($"artist={Artist ?? ""}");
            sb.AppendLine($"effect={Effector ?? ""}");
            sb.AppendLine($"illustrator={Illustrator ?? ""}");
            sb.AppendLine($"jacket={JacketFileName ?? "nowprinting1.png"}");
            sb.AppendLine($"difficulty={DifficultyToKsh(Difficulty)}");
            sb.AppendLine($"level={Level}");

            if (BpmMin == BpmMax || BpmMin == 0)
                sb.AppendLine($"t={BpmMax}");
            else
                sb.AppendLine($"t={BpmMin}-{BpmMax}");

            var firstSig = Events.GetTimeSignature(1);
            sb.AppendLine($"beat={firstSig.Beat}/{firstSig.Note}");
            sb.AppendLine($"m={MusicFileName ?? ""}");
            sb.AppendLine($"mvol={Volume}");
            sb.AppendLine($"o={MusicOffset}");
            sb.AppendLine($"bg=desert");
            sb.AppendLine($"po={PreviewOffset}");
            sb.AppendLine("ver=167");
            sb.AppendLine("--");

            // Pre-build laser segments for each track
            var laserSegments = BuildLaserSegments();

            // Track active button holds: maps track to end absolute offset
            var activeHolds = new Dictionary<Event.ButtonTrack, int>();

            // Track current state for change detection
            Effect currentFxL = null;
            Effect currentFxR = null;
            var currentChipFxL = Event.ChipFx.None;
            var currentChipFxR = Event.ChipFx.None;
            var currentFilter = Event.LaserFilter.Peak;
            int currentRangeL = 1;
            int currentRangeR = 1;

            for (int measure = 1; measure <= MeasureCount; measure++)
            {
                var sig = Events.GetTimeSignature(measure);
                int ticksPerMeasure = 192;

                // Calculate resolution for this measure
                int resolution = CalculateResolution(measure, sig);

                int ticksPerRow = ticksPerMeasure / resolution;

                for (int row = 0; row < resolution; row++)
                {
                    int absOffset = measure * ticksPerMeasure + row * ticksPerRow;
                    var time = Time.FromOffset(absOffset, sig);

                    // Emit time signature change
                    foreach (var ev in Events[time])
                    {
                        if (ev is Event.TimeSignature ts && ts.Time.Measure == measure && ts.Time.Beat == 1 && ts.Time.Offset == 0 && row == 0)
                        {
                            // Only emit if different from first signature (already in header)
                            if (measure > 1)
                                sb.AppendLine($"beat={ts.Beat}/{ts.Note}");
                        }
                    }

                    // Emit BPM changes and stop events
                    foreach (var ev in Events[time])
                    {
                        if (ev is Event.Bpm bpm)
                        {
                            if (bpm.IsStop)
                            {
                                // Find the matching non-stop BPM to calculate duration
                                var stopEv = Events.OfType<Event.Stop>().FirstOrDefault(s => s.Time == time);
                                if (stopEv != null)
                                    sb.AppendLine($"stop={stopEv.Duration}");
                            }
                            else
                            {
                                // Only emit if it's a BPM change (not the very first BPM at measure 1)
                                if (measure > 1 || time.Beat > 1 || time.Offset > 0)
                                    sb.AppendLine($"t={bpm.Value}");
                            }
                        }
                    }

                    // Emit tilt mode changes
                    foreach (var ev in Events[time])
                    {
                        if (ev is Event.TiltMode tilt)
                        {
                            switch (tilt.Mode)
                            {
                                case Event.TiltType.Normal:      sb.AppendLine("tilt=normal");       break;
                                case Event.TiltType.Large:       sb.AppendLine("tilt=bigger");       break;
                                case Event.TiltType.Incremental: sb.AppendLine("tilt=keep_bigger");  break;
                            }
                        }
                    }

                    // Emit camera events
                    foreach (var ev in Events[time])
                    {
                        if (ev is Camera camera)
                        {
                            switch (camera.Work)
                            {
                                case Camera.WorkType.Rotation:
                                    sb.AppendLine($"zoom_top={(int)(camera.End * 100f)}");
                                    break;
                                case Camera.WorkType.Radian:
                                    sb.AppendLine($"zoom_bottom={(int)(camera.End * -100f)}");
                                    break;
                                case Camera.WorkType.Tilt:
                                    sb.AppendLine($"tilt={(int)(camera.End * -1f)}");
                                    break;
                                case Camera.WorkType.LaneClear:
                                    sb.AppendLine($"lane_toggle={(int)camera.End}");
                                    break;
                            }
                        }
                    }

                    // Collect button events at this time
                    var buttonsAtTime = Events[time].OfType<Event.Button>().ToList();

                    // Emit FX hold effect definitions (fx-l=, fx-r=) before the hold starts
                    foreach (var bt in buttonsAtTime)
                    {
                        if (bt.IsFx && bt.HoldLength > 0 && bt.HoldFx != null)
                        {
                            string fxLine = bt.HoldFx.ToKsh();
                            if (bt.Track == Event.ButtonTrack.FxL && bt.HoldFx != currentFxL)
                            {
                                currentFxL = bt.HoldFx;
                                if (!string.IsNullOrEmpty(fxLine))
                                    sb.AppendLine($"fx-l={fxLine}");
                            }
                            else if (bt.Track == Event.ButtonTrack.FxR && bt.HoldFx != currentFxR)
                            {
                                currentFxR = bt.HoldFx;
                                if (!string.IsNullOrEmpty(fxLine))
                                    sb.AppendLine($"fx-r={fxLine}");
                            }
                        }
                    }

                    // Emit chip FX SE definitions (fx-l_se=, fx-r_se=) before the chip
                    foreach (var bt in buttonsAtTime)
                    {
                        if (bt.IsFx && bt.HoldLength == 0 && bt.HitFx != Event.ChipFx.None)
                        {
                            if (bt.Track == Event.ButtonTrack.FxL && bt.HitFx != currentChipFxL)
                            {
                                currentChipFxL = bt.HitFx;
                                sb.AppendLine($"fx-l_se={Event.ChipFxToKsh(bt.HitFx)}");
                            }
                            else if (bt.Track == Event.ButtonTrack.FxR && bt.HitFx != currentChipFxR)
                            {
                                currentChipFxR = bt.HitFx;
                                sb.AppendLine($"fx-r_se={Event.ChipFxToKsh(bt.HitFx)}");
                            }
                        }
                    }

                    // Emit laser filter and range changes
                    var lasersAtTime = Events[time].OfType<Event.Laser>().ToList();
                    foreach (var laser in lasersAtTime)
                    {
                        if (laser.Filter != currentFilter && laser.Filter != Event.LaserFilter.None)
                        {
                            currentFilter = laser.Filter;
                            switch (currentFilter)
                            {
                                case Event.LaserFilter.Peak:       sb.AppendLine("filtertype=peak"); break;
                                case Event.LaserFilter.LowPass:    sb.AppendLine("filtertype=lpf1"); break;
                                case Event.LaserFilter.HighPass:   sb.AppendLine("filtertype=hpf1"); break;
                                case Event.LaserFilter.BitCrusher: sb.AppendLine("filtertype=bitc"); break;
                            }
                        }

                        if (laser.Track == Event.LaserTrack.Left && laser.Range != currentRangeL && laser.Range > 1)
                        {
                            currentRangeL = laser.Range;
                            sb.AppendLine($"laserrange_l={currentRangeL}x");
                        }
                        else if (laser.Track == Event.LaserTrack.Right && laser.Range != currentRangeR && laser.Range > 1)
                        {
                            currentRangeR = laser.Range;
                            sb.AppendLine($"laserrange_r={currentRangeR}x");
                        }
                    }

                    // Build note line: ABCD|LR|lr
                    char[] bt4 = { '0', '0', '0', '0' }; // BT-A, BT-B, BT-C, BT-D
                    char[] fx2 = { '0', '0' };             // FX-L, FX-R
                    char[] vol = { '-', '-' };             // VOL-L, VOL-R

                    // Process buttons
                    foreach (var track in new[] { Event.ButtonTrack.A, Event.ButtonTrack.B, Event.ButtonTrack.C, Event.ButtonTrack.D, Event.ButtonTrack.FxL, Event.ButtonTrack.FxR })
                    {
                        bool isFx = track == Event.ButtonTrack.FxL || track == Event.ButtonTrack.FxR;
                        int charIdx;
                        switch (track)
                        {
                            case Event.ButtonTrack.A:   charIdx = 0; break;
                            case Event.ButtonTrack.B:   charIdx = 1; break;
                            case Event.ButtonTrack.C:   charIdx = 2; break;
                            case Event.ButtonTrack.D:   charIdx = 3; break;
                            case Event.ButtonTrack.FxL: charIdx = 0; break;
                            case Event.ButtonTrack.FxR: charIdx = 1; break;
                            default: continue;
                        }

                        // Check for new events at this time
                        var btn = buttonsAtTime.FirstOrDefault(b => b.Track == track);
                        if (btn != null)
                        {
                            if (btn.HoldLength == 0)
                            {
                                // Chip note
                                if (isFx)
                                    fx2[charIdx] = '2';
                                else
                                    bt4[charIdx] = '1';
                            }
                            else
                            {
                                // Hold start
                                if (isFx)
                                    fx2[charIdx] = '1';
                                else
                                    bt4[charIdx] = '2';

                                // Hold lengths are real ticks, the grid here is normalized
                                var holdEnd = Events.FromTicks(Events.ToTicks(btn.Time) + btn.HoldLength);
                                activeHolds[track] = holdEnd.GetAbsoluteOffset(Events.GetTimeSignature(holdEnd));
                            }
                        }
                        else if (activeHolds.ContainsKey(track))
                        {
                            // Inside active hold
                            if (absOffset < activeHolds[track])
                            {
                                if (isFx)
                                    fx2[charIdx] = '1';
                                else
                                    bt4[charIdx] = '2';
                            }
                            else
                            {
                                // Hold ended
                                activeHolds.Remove(track);
                            }
                        }
                    }

                    // Process lasers
                    vol[0] = GetLaserChar(laserSegments, Event.LaserTrack.Left, absOffset);
                    vol[1] = GetLaserChar(laserSegments, Event.LaserTrack.Right, absOffset);

                    sb.AppendLine($"{bt4[0]}{bt4[1]}{bt4[2]}{bt4[3]}|{fx2[0]}{fx2[1]}|{vol[0]}{vol[1]}");
                }

                sb.AppendLine("--");
            }

            File.WriteAllText(fileName, sb.ToString(), DefaultEncoding);
        }

        private int CalculateResolution(int measure, (int beat, int note) sig)
        {
            int ticksPerMeasure = 192;
            int measureStart = measure * ticksPerMeasure;

            // Collect all absolute offsets of events in this measure
            var offsets = new HashSet<int>();
            foreach (var ev in Events[measure])
            {
                int absOff = ev.Time.GetAbsoluteOffset(sig) - measureStart;
                if (absOff >= 0 && absOff < ticksPerMeasure)
                    offsets.Add(absOff);
            }

            if (offsets.Count == 0)
                return sig.beat * sig.note; // Default: e.g. 16 for 4/4

            // Find GCD of all offsets and ticksPerMeasure
            int gcd = ticksPerMeasure;
            foreach (int off in offsets)
            {
                if (off > 0)
                    gcd = Gcd(gcd, off);
            }

            int resolution = ticksPerMeasure / gcd;

            // Cap at reasonable maximum, minimum at signature default
            int minRes = sig.beat * sig.note;
            if (resolution < minRes)
                resolution = minRes;

            return resolution;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        private class LaserPoint
        {
            public int AbsOffset { get; set; }
            public int LaserOffset { get; set; }
        }

        private Dictionary<Event.LaserTrack, List<List<LaserPoint>>> BuildLaserSegments()
        {
            var segments = new Dictionary<Event.LaserTrack, List<List<LaserPoint>>>
            {
                { Event.LaserTrack.Left,  new List<List<LaserPoint>>() },
                { Event.LaserTrack.Right, new List<List<LaserPoint>>() }
            };

            foreach (var track in new[] { Event.LaserTrack.Left, Event.LaserTrack.Right })
            {
                List<LaserPoint> current = null;

                foreach (var ev in Events)
                {
                    if (ev is Event.Slam slam && slam.Track == track)
                    {
                        var sig = Events.GetTimeSignature(slam.Time);
                        int absOff = slam.Time.GetAbsoluteOffset(sig);

                        if (current == null)
                        {
                            current = new List<LaserPoint>();
                            current.Add(new LaserPoint { AbsOffset = absOff, LaserOffset = slam.Start.Offset });
                        }

                        // Add slam end point at same offset (will be rendered as same-row slam)
                        current.Add(new LaserPoint { AbsOffset = absOff, LaserOffset = slam.End.Offset });
                    }
                    else if (ev is Event.Laser laser && laser.Track == track)
                    {
                        var sig = Events.GetTimeSignature(laser.Time);
                        int absOff = laser.Time.GetAbsoluteOffset(sig);

                        if (laser.Flag == Event.LaserFlag.Start)
                        {
                            current = new List<LaserPoint>();
                            current.Add(new LaserPoint { AbsOffset = absOff, LaserOffset = laser.Offset });
                        }
                        else if (laser.Flag == Event.LaserFlag.Tick && current != null)
                        {
                            current.Add(new LaserPoint { AbsOffset = absOff, LaserOffset = laser.Offset });
                        }
                        else if (laser.Flag == Event.LaserFlag.End)
                        {
                            if (current != null)
                            {
                                current.Add(new LaserPoint { AbsOffset = absOff, LaserOffset = laser.Offset });
                                segments[track].Add(current);
                                current = null;
                            }
                        }
                    }
                }

                // Handle segment that never got an End event
                if (current != null && current.Count > 0)
                    segments[track].Add(current);
            }

            return segments;
        }

        private static char GetLaserChar(Dictionary<Event.LaserTrack, List<List<LaserPoint>>> segments, Event.LaserTrack track, int absOffset)
        {
            foreach (var segment in segments[track])
            {
                if (segment.Count == 0)
                    continue;

                int segStart = segment[0].AbsOffset;
                int segEnd   = segment[segment.Count - 1].AbsOffset;

                if (absOffset < segStart)
                    continue;
                if (absOffset > segEnd)
                    continue;

                // Check if there's a point at this exact offset
                foreach (var point in segment)
                {
                    if (point.AbsOffset == absOffset)
                        return OffsetToKshChar(point.LaserOffset);
                }

                // Inside segment but no position change - continuation
                return ':';
            }

            return '-';
        }

        private static char OffsetToKshChar(int offset)
        {
            int index = (int)Math.Round(offset / 127.0 * 50.0);
            index = Math.Max(0, Math.Min(50, index));
            return VolPositions[index];
        }

        private static string DifficultyToKsh(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Novice:   return "light";
                case Difficulty.Advanced: return "challenge";
                case Difficulty.Exhaust:  return "extended";
                case Difficulty.Infinite: return "infinite";
                default:                  return "infinite";
            }
        }
    }
}
