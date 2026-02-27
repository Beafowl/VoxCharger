using System;
using System.Collections.Generic;
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
        public string Background     { get; set; }
        public int MeasureCount      { get; private set; }
        public List<string> FxLog    { get; set; }

        public EventCollection Events { get; private set; } = new EventCollection();

        public class ParseOption
        {
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
            public SoundFxOptions SoundFx { get; set; } = new SoundFxOptions();
            public CameraOptions  Camera  { get; set; } = new CameraOptions();
            public TrackOptions Track     { get; set; } = new TrackOptions();
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
                Radar       = RadarCalculator.Calculate(Events)
            };
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
            int rangeLeft  = 0;
            int rangeRight = 0;
            var cameras    = new Dictionary<Camera.WorkType, Camera>();
            var hitFx      = new Dictionary<Event.ButtonTrack, KshSoundEffect>();
            var holdFx     = new Dictionary<Event.ButtonTrack, Effect>();
            var longNotes  = new Dictionary<Event.ButtonTrack, Event.Button>();
            var lasers     = new Dictionary<Event.LaserTrack, List<Event.Laser>>();

            int offset    = 0;
            int measure   = 0;

            int measureCount = lines.Count(l => l.Trim() == "--");
            int fxCount   = 1;
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
                    if (opt.RealignOffset)
                        position -= MusicOffset; // Attempt to align

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
                        case "jacket":      JacketFileName = value; break;
                        case "m":           MusicFileName  = value; break;               
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

                                if (!value.Contains("-") && Events.GetBpm(new Time(time.Measure, time.Beat, 0))?.Value != t)
                                {
                                    var bpm = new Event.Bpm(new Time(time.Measure, time.Beat, 0), t); // beat still acceptable
                                    Events.Add(bpm);
                                }
                            }

                            // BPM change with unaligned music offset, proceed with caution!
                            if (BpmMin != BpmMax && MusicOffset % 48 != 0 && opt.RealignOffset)
                                Debug.WriteLine($"[KSH] BPM change with unaligned music offset");

                            break;
                        case "stop" when float.TryParse(value, out float duration):
                            Event.Bpm start = null;
                            time = new Time(time.Measure, time.Beat, 0);
                            foreach (var ev in Events[time])
                            {
                                if (ev is Event.Bpm x)
                                {
                                    start = x;
                                    start.IsStop = true;
                                    break;
                                }
                            }

                            if (start == null)
                            {
                                var last = Events.GetBpm(time);
                                if (last == null)
                                    break;

                                start = new Event.Bpm(time, last.Value);
                                start.IsStop = true;
                                Events.Add(start);
                            }

                            Event.Bpm end = null;
                            var target    = Time.FromOffset((int)duration, signature);
                            foreach (var ev in Events[target])
                            {
                                if (ev is Event.Bpm x)
                                {
                                    end = x;
                                    end.IsStop = false;
                                    break;
                                }
                            }

                            if (end == null)
                            {
                                var last = Events.GetBpm(target);
                                if (last == null)
                                    break;

                                end = new Event.Bpm(target, last.Value);
                                end.IsStop = false;
                                Events.Add(end);
                            }

                            // Stop with unaligned music offset can break other stuffs too
                            if (MusicOffset % 48 != 0 && opt.RealignOffset)
                                Debug.WriteLine($"[KSH] Stop event unaligned music offset");

                            break;
                        case "beat": 
                            var sig = value.Split('/');
                            if (sig.Length != 2 || !int.TryParse(sig[0], out int b) || !int.TryParse(sig[1], out int n))
                                break;

                            signature = new Event.TimeSignature(new Time(time.Measure, 1, 0), b, n); // beat unacceptable, only at start measure
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
                                fx.Id = ++fxCount;
                                holdFx[htrack] = fx;
                                FxLog?.Add($"{prop}={value} → ID={fx.Id} Type={fx.Type}({(int)fx.Type}) Output=[{fx}]");
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
                            
                            if (Enum.TryParse(param[0], true, out Event.ChipFx hit))
                                hitFx[fxTrack].Effect = hit;

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
                        case "laserrange_l" when int.TryParse(value.Replace("x", ""), out int r): rangeLeft  = r; break;
                        case "laserrange_r" when int.TryParse(value.Replace("x", ""), out int r): rangeRight = r; break;
                        #endregion
                        #region --- Camera ---
                        case "tilt" when !float.TryParse(value.Trim(), out float _):
                            if (!opt.Camera.IsEnabled(Camera.WorkType.Tilt))
                                break;

                            // We need manually correct the tilt because TiltMode in-game is not reliable
                            cameras.TryGetValue(Camera.WorkType.Tilt, out var lastTilt);

                            // Clear the camera cache, leave the previous tilt duration to 0
                            cameras[Camera.WorkType.Tilt] = null;

                            switch (value.Trim()) // tested with limited scenario
                            {
                                case "zero":
                                case "normal":
                                    // Tilt mode normal seems ignored by the game
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Normal));

                                    // Instead, we reset the previously applied tilt manually
                                    Events.Add(new Camera.Tilt(time, 0, lastTilt?.End ?? 0.00f, 0.00f));

                                    // Add padding to our reset tilt camera
                                    var tiltPadding = new Camera.Tilt(time, 48, 0.00f, 0.00f);
                                    Events.Add(tiltPadding);

                                    break;
                                case "bigger":
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Large));
                                    break;
                                case "keep_bigger":
                                    Events.Add(new Event.TiltMode(time, Event.TiltType.Incremental));
                                    break;
                            }

                            break;
                        case "tilt":        // Tilt
                        case "zoom_top":    // CAM_RotX
                        case "zoom_bottom": // CAM_Radi
                        case "lane_toggle": // LaneY
                            if (!float.TryParse(value, out float cameraVal))
                                break;

                            var work = Camera.WorkType.None;
                            switch (prop)
                            {
                                case "zoom_top":
                                    work = Camera.WorkType.Rotation;
                                    cameraVal /= 100.0f;
                                    break;
                                case "zoom_bottom":
                                    work = Camera.WorkType.Radian;
                                    cameraVal /= -100.0f;
                                    break;
                                case "tilt":
                                    work = Camera.WorkType.Tilt;
                                    cameraVal /= -1.0f; // untested
                                    break;
                                case "lane_toggle":
                                    work = Camera.WorkType.LaneClear; // untested too
                                    break;
                            }

                            if (!opt.Camera.IsEnabled(work))
                                break;

                            Camera camera = null;
                            cameras.TryGetValue(work, out var lastCamera);
                            switch (work)
                            {
                                case Camera.WorkType.Rotation:
                                case Camera.WorkType.Radian:
                                case Camera.WorkType.Tilt:
                                    camera = Camera.Create(work, time, 0, cameraVal, cameraVal);
                                    break;
                                case Camera.WorkType.LaneClear:
                                    camera = new Camera.LaneClear(time, (int)cameraVal, 0.00f, 1024.00f);
                                    break;
                            }

                            if (camera == null)
                                break;

                            int currentOffset = time.GetAbsoluteOffset(Events.GetTimeSignature(time));
                            if (lastCamera != null)
                            {
                                int lastOffset = lastCamera.Time.GetAbsoluteOffset(Events.GetTimeSignature(lastCamera.Time));
                                int diff       = Math.Abs(currentOffset - lastOffset);

                                lastCamera.End      = cameraVal;
                                lastCamera.Duration = diff;
                            }
                            else
                            {
                                // Tilt might be reset via TiltMode event
                                var timePair   = work != Camera.WorkType.Tilt ? Time.Initial  : time;
                                int offsetPair = work != Camera.WorkType.Tilt ? currentOffset : Math.Abs(currentOffset - timePair.GetAbsoluteOffset(Events.GetTimeSignature(timePair)));

                                var init = Camera.Create(work, timePair, 0, 0.00f, cameraVal);
                                var pair = Camera.Create(work, timePair, offsetPair, cameraVal, cameraVal);

                                Events.Add(init, pair);
                            }

                            cameras[work] = camera;
                            Events.Add(camera);
                            break;
                        #endregion
                    }
                }
                else if (line == "--")
                {
                    // Reset time counter
                    measure  += 1;
                    offset    = 0;
                    noteCount = 0;

                    // Reset laser range, unless there's active laser
                    if (!lasers.ContainsKey(Event.LaserTrack.Left))
                        rangeLeft  = 1;

                    if (!lasers.ContainsKey(Event.LaserTrack.Right))
                        rangeRight = 1;

                    float position = measure * 192f;
                    if (measure > 1 && opt.RealignOffset)
                        position -= MusicOffset;

                    time = Time.FromOffset(position, signature);
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string ln = lines[j].Trim();
                        if (!string.IsNullOrEmpty(ln))
                        {
                            if (char.IsDigit(ln[0]))
                                noteCount++;
                            else if (ln == "--")
                                break;
                        }
                    }
                }
                else if (char.IsDigit(line[0]))
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

                        // FxL and FxR accept any char for long note
                        if (!int.TryParse(line[channel].ToString(), out int flag) && (track != Event.ButtonTrack.FxL && track != Event.ButtonTrack.FxR))
                            continue;

                        // Who's right in the mind to split fx and bt event logic just because the flag is opposite? lol
                        bool isFx   = track == Event.ButtonTrack.FxL || track == Event.ButtonTrack.FxR;
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

                            // Overlapping long notes
                            if (longNotes.ContainsKey(track))
                            {
                                var ev        = longNotes[track];
                                var timeSig   = Events.GetTimeSignature(ev.Time.Measure);
                                ev.HoldLength = time.Difference(ev.Time, timeSig);

                                Events.Add(ev);
                            }

                            longNotes.Remove(track);
                            Events.Add(new Event.Button(time, track, 0, null, hit));
                        }
                        else if (isHold)
                        {
                            if (!longNotes.ContainsKey(track))
                            {
                                Effect fx = null;
                                if (isFx)
                                {
                                    if (!holdFx.ContainsKey(track))
                                        holdFx[track] = null;
                                    
                                    if (holdFx[track] != null && holdFx[track].Type != FxType.None)
                                        fx = holdFx[track];
                                }

                                longNotes[track] = new Event.Button(time, track, 0, fx, Event.ChipFx.None);
                            }
                            else
                            {
                                var ev = longNotes[track];
                                ev.HoldLength = Math.Abs(time.Difference(ev.Time, signature));
                            }
                        }
                        else
                        {
                            // Empty event (Event flag: 0)
                            if (longNotes.ContainsKey(track))
                            {
                                var ev        = longNotes[track];
                                var timeSig   = Events.GetTimeSignature(ev.Time.Measure);
                                ev.HoldLength = time.Difference(ev.Time, timeSig);

                                Events.Add(longNotes[track]);
                            }

                            longNotes.Remove(track);
                        }
                    }
                    #endregion

                    #region  --- Laser ---
                    foreach (var track in new[] { Event.LaserTrack.Left, Event.LaserTrack.Right })
                    {
                        if (!opt.Track.IsLaserTrackEnabled(track))
                            continue;

                        int channel = track == Event.LaserTrack.Left ? 8 : 9;
                        int range   = track == Event.LaserTrack.Left ? rangeLeft : rangeRight;
                        char flag   = line[channel];
                        var impact  = Event.SlamImpact.None;

                        // Ignore tick
                        if (flag == ':')
                            continue;

                        if (line.Length >= 13 && opt.Camera.SlamImpact)
                        {
                            // Ignore direction, use slam offset instead
                            char f = line[10];
                            if (f == '@')
                            {
                                // Map the impact types
                                char spin = line[11];
                                if (spin == '(' || spin == ')')
                                    impact = Event.SlamImpact.Measure;
                                else if (spin == '<' || spin == '>')
                                    impact = Event.SlamImpact.HalfMeasure;

                                // Try to realign with impact length, seriously, why would impact has length in kshoot?
                                if (int.TryParse(line.Substring(12), out int length))
                                {
                                    // This mapping may incorrect.. and stupid
                                    if ((impact == Event.SlamImpact.Measure || impact == Event.SlamImpact.HalfMeasure) && length < 96)
                                        impact = Event.SlamImpact.Swing;
                                    else if (impact == Event.SlamImpact.Measure && length <= 144)
                                        impact = Event.SlamImpact.ThreeBeat;
                                }
                            }
                            else if (f == 'S')
                                impact = Event.SlamImpact.Swing;
                        }

                        var laser = new Event.Laser(
                            time,
                            track,
                            0,
                            Event.LaserFlag.Start,
                            impact,
                            filter,
                            range
                        );

                        laser.IsLaserSlam = noteCount >= 32;
                        if (flag != '-')
                        {
                            // Determine laser offset
                            for (int x = 0; x < VolPositions.Length; x++)
                            {
                                if (VolPositions[x] == flag)
                                    laser.Offset = (int)((x / 51f) * 127f);
                            }

                            if (lasers.ContainsKey(track))
                                laser.Flag = Event.LaserFlag.Tick;
                            else
                                lasers[track] = new List<Event.Laser>();

                            lasers[track].Add(laser);
                        }
                        else if (lasers.ContainsKey(track))
                        {
                            var nodes = lasers[track];
                            lasers.Remove(track);

                            // There suppose to be 2 point of laser, no?
                            if (nodes.Count < 2)
                                continue;

                            /** Slam Laser
                             * Every lasers inside 1/32 that 6 cells apart or less are slam in kshoot
                             * However, Vox may produce bug when slam defined more than 2 laser events within certain amount of distance
                             * It seems the threshold determined based on current active bpm, the exact formula is still unclear
                             * Therefore, eliminate the duplicate / unnecessary ticks
                             * 
                             * For example, this code will transform following events:
                             * 
                             *    001,01,00    25    1    0    0    2    0
                             *    001,01,00    95    0    0    0    2    0 // -> Unnecessary event, since the next event is the same offset
                             *    001,01,06    95    0    0    0    2    0 // -> This cause bug because same offset and located within distance threshold
                             *    002,01,00    95    0    0    0    2    0
                             *    002,01,06    50    2    0    0    2    0
                             *    
                             * To this:
                             * 
                             *    001,01,00    25    1    0    0    2    0
                             *    001,01,06    95    0    0    0    2    0 // -> Keep 001,01,06 and throw one of the duplicate
                             *    002,01,00    95    0    0    0    2    0
                             *    002,01,00    50    2    0    0    2    0
                             * 
                             */

                            int counter = 0;
                            Event.Laser last = null;
                            foreach (var node in nodes.ToArray())
                            {
                                if (last == null)
                                {
                                    last = node;
                                    continue;
                                }

                                if (node.Offset == last.Offset)
                                    counter++;
                                else
                                    counter = 0;

                                if (counter >= 2)
                                    nodes.Remove(last);

                                last = node;
                            }

                            // TODO: As inefficient as it gets lol, merge into previous loop if possible
                            for (int n = 0; n < nodes.Count; n++)
                            {
                                if (n + 1 >= nodes.Count)
                                    break;

                                var start   = nodes[n];
                                var end     = nodes[n + 1];
                                var timeSig = Events.GetTimeSignature(start.Time);

                                // Due to terrible ksh format for slam that appear in 1/32 or shorter 
                                // the output of conversion may have 6 cells gap apart each other
                                // TODO: Use offset instead of Time.Difference, cuz it might start and finish in different measure
                                if (start.IsLaserSlam && end.IsLaserSlam && end.Time.Difference(start.Time, timeSig) <= 6)
                                {
                                    // Pull one of the offset
                                    // In some rare cases, not pulling the offset will ended up normal laser instead of slam, especially in slow bpm
                                    int min  = Math.Min(start.Time.Offset, end.Time.Offset);
                                    start.Time.Offset = end.Time.Offset = min;

                                    n += 1;
                                }
                            }

                            // Don't forget to set the flag
                            nodes[0].Flag = Event.LaserFlag.Start;
                            last.Flag     = Event.LaserFlag.End;

                            // TODO: Reset laser range when it's no longer active in current measure(?)
                            Events.Add(nodes.ToArray());
                        }
                    }
                    #endregion
                }
            }

            MeasureCount = measure;
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
                                sb.AppendLine($"fx-l_se={bt.HitFx}");
                            }
                            else if (bt.Track == Event.ButtonTrack.FxR && bt.HitFx != currentChipFxR)
                            {
                                currentChipFxR = bt.HitFx;
                                sb.AppendLine($"fx-r_se={bt.HitFx}");
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

                                activeHolds[track] = absOffset + btn.HoldLength;
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
