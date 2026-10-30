using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace VoxCharger
{
    public enum AudioFormat
    {
        Iidx,
        S3V
    }

    public class AudioImportOptions
    {
        public static readonly AudioImportOptions Default = new AudioImportOptions();

        public AudioFormat Format { get; set; } = AudioFormat.Iidx;
        public bool IsPreview     { get; set; } = false;
        public int PreviewOffset  { get; set; } = 0;

        // EBU R128 loudness normalization. When enabled, the source audio is
        // two-pass loudnorm-ed to TargetLufs before being encoded, so every
        // chart lands at a consistent in-game level regardless of the source
        // master. Pair with a fixed VoxHeader.Volume (see TargetVolume).
        public bool NormalizeLoudness { get; set; } = false;
        // -9 LUFS roughly matches Konami's stock SDVX masters. The old -14 LUFS
        // target (Spotify/YouTube's broadcast-safe level) left custom charts
        // audibly quieter than base songs. TargetTruePeak stays at -1.5 dBTP
        // so the higher integrated loudness doesn't cause sample clipping.
        public double TargetLufs      { get; set; } = -9.0;
        public double TargetTruePeak  { get; set; } = -1.5;
        public short TargetVolume     { get; set; } = 91;

        // KSH 'o=' music-offset in milliseconds, applied to the audio file
        // rather than to chart tick positions. Positive = pad audio front
        // with silence (music starts later relative to chart tick 0).
        // Negative = trim audio front (music already N ms in at chart start).
        // Applying it to audio keeps every chart event aligned exactly on
        // its KSH tick position — which means notes stay on the beat grid
        // visually instead of being shifted off by the ms->ticks remainder.
        public int MusicOffsetMs      { get; set; } = 0;

        // Extra silence prepended to the audio after MusicOffset processing,
        // independent of `o=`. Use together with Ksh.PrependLeadInMeasures so
        // both audio and chart get the same shift — gives players a moment
        // before the first note instead of dropping them straight into the
        // judgement line on charts whose first event sits at tick 0.
        public int LeadInPadMs        { get; set; } = 0;

        // Hard cut audio at this absolute timestamp (ms in the FINAL audio,
        // i.e. after MusicOffset trimming and LeadInPad). 0 = no truncate.
        // Used to stop the music from continuing after the chart finishes
        // when the source KSM clip is longer than the gameplay segment.
        public int TruncateAtMs       { get; set; } = 0;

        // Linear fade-out duration ending at TruncateAtMs (ms). 0 = no fade.
        // The fade starts at (TruncateAtMs - TailFadeOutMs) so the audio is
        // already silent by the cut point.
        public int TailFadeOutMs      { get; set; } = 0;

        public static AudioImportOptions WithFormat(AudioFormat format)
        {
            return new AudioImportOptions
            {
                Format        = format,
                IsPreview     = false,
                PreviewOffset = 0
            };
        }

        public AudioImportOptions AsPreview()
        {
            return new AudioImportOptions
            {
                Format            = Format,
                IsPreview         = true,
                PreviewOffset     = PreviewOffset,
                NormalizeLoudness = NormalizeLoudness,
                TargetLufs        = TargetLufs,
                TargetTruePeak    = TargetTruePeak,
                TargetVolume      = TargetVolume,
                // Music offset is only meaningful for the main audio, not
                // the short preview clip, so don't propagate it.
            };
        }
    }

    public static class AssetManager
    {
        #region --- Properties ---
        private static List<string> _mixList = new List<string>();
        private static MusicDb _internalHeaders = null;
        private static int _lastOriginalId = 0;

        // Songs converted at the same time by the bulk and list imports. Each one runs ffmpeg and holds
        // the decoded audio in memory; leaves two hardware threads for the rest of the system.
        public static int ConvertWorkers => Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 8));

        public static string MixName { get; private set; }

        public static string GamePath { get; private set; }

        public static string MixPath { get; set; }

        public static MusicDb Headers { get; private set; } = new MusicDb();

        public static string MdbFilename { get; private set; }

        // Tag applied to every song in the mix, 0 = none. Persisted in <mix>/voxcharger_mix.json
        public static int MixTagId { get; private set; }

        // Lowest music id songs in the mix may use, 0 = none. Persisted in <mix>/voxcharger_mix.json
        public static int MixStartId { get; private set; }

        private const string MixSettingsFileName = "voxcharger_mix.json";
        private const int TutorialMusicId = 9001;

        // The game's song select loads a jacket only for ids below the tutorial id (its
        // CIndividualJacketLoader compares against 9001); higher ids play fine but show the
        // dummy jacket in the list and the preview.
        public const int MaxJacketMusicId = 9000;

        // Highest music id the game can use at all: its per-song tables hold 65535 ids with the
        // sdvx-mid-unlock id patch (3072 without it). Higher ids never show up, and at every start the
        // game downloads high scores for every id up to the highest one (768 per request), so a song at
        // 999999 keeps the download, and with it every card-in ("server busy"), going for 10+ minutes.
        public const int MaxMusicId = 65534;

        // Jacket thumbnail archives of the game build (data/graphics/s_jacketNN.ifs). A mix's
        // thumbnails are spread over them by music id so ifs_layeredfs keeps each injected
        // texture small: one 4096x4096 canvas for a whole mix is more than the game takes.
        private static readonly List<int> _thumbnailArchives = new List<int>();

        public const int ThumbnailSize = 128;
        #endregion

        #region --- Mix Management ---
        public static void Initialize(string gamePath)
        {
            // Validate the existence of main music db
            string dbFilename = Path.Combine(gamePath, @"data\others\music_db.xml");
            if (!File.Exists(dbFilename))
                throw new FormatException("Invalid Game Directory");

            // Look for other mixes
            _mixList.Clear();
            string modsPath = Path.Combine(gamePath, @"data_mods\");
            foreach (var modDir in Directory.GetDirectories(modsPath))
            {
                // Get directory name
                string modName = new DirectoryInfo(modDir).Name;
                if (modName == "_cache")
                    continue;

                // Validate whether the mod is a mix mod
                dbFilename = Path.Combine(modDir, @"others\music_db.merged.xml");
                if (!File.Exists(dbFilename))
                    continue;

                // Only supports latest KFC datecode with music folder
                if (!Directory.Exists(Path.Combine(modDir, @"music\")))
                    continue;

                // Confirmed mod path, append into music db, ignore cache to avoid uncached mix being excluded
                _mixList.Add(modName);
            }

            // Thumbnail archives shipped with this build, packed (.ifs) or unpacked (_ifs)
            _thumbnailArchives.Clear();
            string graphicsPath = Path.Combine(gamePath, @"data\graphics");
            if (Directory.Exists(graphicsPath))
            {
                var entries = Directory.GetFiles(graphicsPath, "s_jacket??.ifs")
                    .Concat(Directory.GetDirectories(graphicsPath, "s_jacket??_ifs"));
                foreach (string entry in entries)
                {
                    string name = Path.GetFileName(entry);
                    if (int.TryParse(name.Substring(8, 2), out int archive) && !_thumbnailArchives.Contains(archive))
                        _thumbnailArchives.Add(archive);
                }
                _thumbnailArchives.Sort();
            }

            _lastOriginalId = 0;
            GamePath = gamePath;
        }

        // Archive a song's thumbnails go into; always 00 when the build's archives are unknown
        public static int ThumbnailArchive(int musicId)
        {
            return _thumbnailArchives.Count == 0 ? 0 : _thumbnailArchives[musicId % _thumbnailArchives.Count];
        }

        public static void CreateMix(string mixName)
        {
            string mixPath = Path.Combine(GamePath, @"data_mods\", mixName);
            if (Directory.Exists(mixPath) && !IsMixSkeleton(mixPath))
                throw new IOException($"A folder named \"{mixName}\" already exists in data_mods");

            // Load Existing song DB to avoid duplicate id
            // Done before touching the disk so an unreadable db elsewhere doesn't leave a half-created mix behind
            LoadInternalDb(mixName);

            // Create necessary directory
            Directory.CreateDirectory(Path.Combine(mixPath, @"graphics\s_jacket00_ifs\"));
            Directory.CreateDirectory(Path.Combine(mixPath, @"music\"));
            Directory.CreateDirectory(Path.Combine(mixPath, @"others\"));

            // Create empty db
            MdbFilename = Path.Combine(mixPath, @"others\music_db.merged.xml");
            File.WriteAllText(MdbFilename, "<?xml version=\"1.0\" encoding=\"Shift_JIS\"?><mdb></mdb>");
            Headers.Clear();

            MixName = mixName;
            MixPath = mixPath;
            MixTagId = Headers.ForcedTagId = 0;
            MixStartId = 0;
        }

        public static void LoadMix(string mixName)
        {
            string dataPath = string.IsNullOrEmpty(mixName) ? @"data\" : @"data_mods\";
            string mixPath  = Path.Combine(GamePath, dataPath, mixName);
            if (!Directory.Exists(mixPath))
                throw new DirectoryNotFoundException("Mix directory missing");

            // Load Existing song DB to avoid duplicate id
            LoadInternalDb(mixName);
            if (!string.IsNullOrEmpty(mixName) && !_mixList.Contains(mixName))
                _mixList.Add(mixName);

            // Locate the music db, if unavailable, create it
            MdbFilename = Path.Combine(mixPath, @"others\", string.IsNullOrEmpty(mixName) ? "music_db.xml" : "music_db.merged.xml");
            Headers.Load(MdbFilename);

            MixName = mixName;
            MixPath = mixPath;

            // The original game data never gets mix settings
            var settings = string.IsNullOrEmpty(mixName) ? null : LoadMixSettings(mixPath);
            MixTagId   = Headers.ForcedTagId = GetSetting(settings, "tag");
            MixStartId = GetSetting(settings, "startId");
        }

        // Consecutive ids from startId for every song of the mix, in current id order. Ids of the
        // game, other mixes and the tutorial are skipped. Returns the songs whose id changes.
        public static List<(VoxHeader header, int newId)> PlanMixStartId(int startId)
        {
            var moves = new List<(VoxHeader header, int newId)>();
            if (startId <= 0)
                return moves;

            int next = startId;
            foreach (var header in Headers.OrderBy(h => h.Id))
            {
                while (!ValidateMusicId(next))
                    next++;

                if (header.Id != next)
                    moves.Add((header, next));
                next++;
            }

            return moves;
        }

        // Applies PlanMixStartId. Ids change in memory right away; the returned actions rename
        // the assets on disk and must run before the db is saved, in the returned order: a song
        // is only moved once no other song still occupies its new id (targets are distinct and
        // ascending, so there are no cycles and such an order always exists).
        public static List<Action> SetMixStartId(int startId)
        {
            MixStartId = startId;

            var actions = new List<Action>();
            var pending = PlanMixStartId(startId);
            while (pending.Count > 0)
            {
                int index = pending.FindIndex(m => !pending.Any(o => o.header != m.header && o.header.Id == m.newId));
                if (index < 0)
                    index = 0;

                var (header, newId) = pending[index];
                pending.RemoveAt(index);

                int oldId    = header.Id;
                string ascii = header.Ascii;
                Headers.ChangeId(header, newId);
                actions.Add(() => RenameMusicAssets(oldId, newId, ascii));
            }

            return actions;
        }

        // Consecutive ids from startId over several mixes: one mix after the other in the given order,
        // the songs of a mix in their current id order. Ids of the game, the mixes not in the list and
        // the tutorial are skipped, so the mixes fill every free id from startId on.
        // Returns mix -> (old id -> new id) for every song.
        public static Dictionary<string, Dictionary<int, int>> PlanRenumberMixes(IList<string> mixes, int startId)
        {
            var blocked = new HashSet<int>(MusicIdsIn(Path.Combine(GamePath, @"data\others\music_db.xml"))) { TutorialMusicId };
            foreach (string modDir in Directory.GetDirectories(Path.Combine(GamePath, @"data_mods\")))
            {
                string name = new DirectoryInfo(modDir).Name;
                if (name != "_cache" && !mixes.Contains(name, StringComparer.OrdinalIgnoreCase))
                    blocked.UnionWith(MusicIdsIn(Path.Combine(modDir, @"others\music_db.merged.xml")));
            }

            // Disabled mixes keep their ids, enabling one again must not clash
            foreach (string name in GetDisabledMixes())
                blocked.UnionWith(MusicIdsIn(MixDbFilename(name)));

            var plan = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
            int next = startId;
            foreach (string mix in mixes)
            {
                var ids = new Dictionary<int, int>();
                foreach (int id in MusicIdsIn(MixDbFilename(mix)).Distinct().OrderBy(id => id))
                {
                    while (blocked.Contains(next))
                        next++;

                    ids[id] = next++;
                }

                plan[mix] = ids;
            }

            return plan;
        }

        // Applies PlanRenumberMixes right away: loads each mix in turn, renames the files of the songs
        // that move and saves its db. A song whose files can't be moved keeps its id, the error says
        // why. The mix loaded last stays loaded, the caller reopens the one it shows.
        public static List<string> RenumberMixes(Dictionary<string, Dictionary<int, int>> plan, Action<string, float> progress)
        {
            var errors = new List<string>();
            int total  = plan.Values.Sum(ids => ids.Count(pair => pair.Key != pair.Value));
            int done   = 0;

            foreach (var mix in plan)
            {
                LoadMix(mix.Key);

                var pending = Headers
                    .Where(h => mix.Value.TryGetValue(h.Id, out int id) && id != h.Id)
                    .Select(h => (header: h, newId: mix.Value[h.Id]))
                    .ToList();

                // A song moves once no other song of the mix holds its new id; the new ids are distinct and
                // follow the old order, so only a song that failed to move can block another one
                while (pending.Count > 0)
                {
                    int index = pending.FindIndex(m => !Headers.Contains(m.newId));
                    if (index < 0)
                    {
                        errors.AddRange(pending.Select(m => $"{mix.Key}: {m.header.Id} ({m.header.Title}) can't move to {m.newId}, that id is still taken"));
                        break;
                    }

                    var (header, newId) = pending[index];
                    pending.RemoveAt(index);
                    progress?.Invoke($"{mix.Key}: {header.Id} -> {newId}", total == 0 ? 100f : ++done * 100f / total);

                    try
                    {
                        RenameMusicAssets(header.Id, newId, header.Ascii);
                        Headers.ChangeId(header, newId);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{mix.Key}: {header.Id} ({header.Title}): {ex.Message}");
                    }
                }

                // A starting id below the new numbers would put new songs into gaps further down
                if (MixStartId > 0 && Headers.Count > 0)
                    MixStartId = Headers.Min(h => h.Id);

                Headers.Save(MdbFilename);
                SaveMixSettings();
            }

            return errors;
        }

        // Music ids in a music db file, without loading the whole db
        public static List<int> MusicIdsIn(string dbFilename)
        {
            var ids = new List<int>();
            if (!File.Exists(dbFilename))
                return ids;

            string text = File.ReadAllText(dbFilename, Encoding.GetEncoding("Shift_JIS"));
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"<music\s+id=""(\d+)"""))
                ids.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));

            return ids;
        }

        // Music db of a mix, enabled or disabled
        public static string MixDbFilename(string mixName)
        {
            string enabled = Path.Combine(GamePath, "data_mods", mixName);
            string folder  = Directory.Exists(enabled) ? enabled : Path.Combine(DisabledMixesPath, mixName);
            return Path.Combine(folder, @"others\music_db.merged.xml");
        }

        // layeredfs loads every folder in data_mods (it only skips _cache), so a disabled mix is moved out
        // of it, into this folder next to it, and moved back when it is enabled again
        public static string DisabledMixesPath => Path.Combine(GamePath, "data_mods_disabled");

        public static string[] GetDisabledMixes()
        {
            if (string.IsNullOrEmpty(GamePath) || !Directory.Exists(DisabledMixesPath))
                return new string[0];

            return Directory.GetDirectories(DisabledMixesPath)
                .Where(dir => File.Exists(Path.Combine(dir, @"others\music_db.merged.xml")))
                .Select(dir => new DirectoryInfo(dir).Name)
                .ToArray();
        }

        public static void SetMixEnabled(string mixName, bool enabled)
        {
            string active   = Path.Combine(GamePath, "data_mods", mixName);
            string disabled = Path.Combine(DisabledMixesPath, mixName);
            string from     = enabled ? disabled : active;
            string to       = enabled ? active : disabled;

            if (!Directory.Exists(from))
                throw new DirectoryNotFoundException($"The mix \"{mixName}\" was not found in {Path.GetDirectoryName(from)}");
            if (Directory.Exists(to))
                throw new IOException($"Cannot move the mix, {to} already exists");

            Directory.CreateDirectory(Path.GetDirectoryName(to));
            try
            {
                Directory.Move(from, to);
            }
            catch (IOException ex)
            {
                throw new IOException($"The mix folder could not be moved, is the game running?\n{ex.Message}", ex);
            }

            if (enabled && !_mixList.Contains(mixName))
                _mixList.Add(mixName);
            else if (!enabled)
                _mixList.Remove(mixName);
        }

        public static void SetMixTagId(int tagId)
        {
            // Songs that only carry the tag because of the previous mix tag lose it when the mix tag is removed
            foreach (var header in Headers)
            {
                if (tagId > 0)
                    header.TagId = tagId;
                else if (header.TagId == MixTagId)
                    header.TagId = 0;
            }

            MixTagId = Headers.ForcedTagId = tagId;
        }

        public static void SaveMixSettings()
        {
            if (string.IsNullOrEmpty(MixName) || !Directory.Exists(MixPath))
                return;

            var settings = new Dictionary<string, object> { { "tag", MixTagId }, { "startId", MixStartId } };
            File.WriteAllText(Path.Combine(MixPath, MixSettingsFileName), new JavaScriptSerializer().Serialize(settings), Encoding.UTF8);
        }

        private static Dictionary<string, object> LoadMixSettings(string mixPath)
        {
            string path = Path.Combine(mixPath, MixSettingsFileName);
            if (!File.Exists(path))
                return null;

            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch
            {
                // Malformed settings shouldn't block opening the mix
                return null;
            }
        }

        private static int GetSetting(Dictionary<string, object> settings, string key)
        {
            if (settings != null && settings.TryGetValue(key, out object value) && value is int number && number > 0)
                return number;

            return 0;
        }

        public static string[] GetMixes()
        {
            return _mixList.ToArray();
        }

        // Directory left behind by an interrupted CreateMix: no music db and no files at all,
        // only (possibly) the empty folder skeleton. Safe to reuse for a new mix.
        private static bool IsMixSkeleton(string mixPath)
        {
            if (File.Exists(Path.Combine(mixPath, @"others\music_db.merged.xml")))
                return false;

            return Directory.GetFiles(mixPath, "*", SearchOption.AllDirectories).Length == 0;
        }

        private static void LoadInternalDb(string mixName)
        {
            // First, we need to populate available ids outside selected mix
            // This will prevent duplicate ID not only with originals but with other mixes too

            // Load original music,  ignore cache to avoid uncached mix being excluded or included
            string dbFilename = Path.Combine(GamePath, @"data\others\music_db.xml");
            string modsPath = Path.Combine(GamePath, @"data_mods\");

            // Load original headers data
            _internalHeaders = new MusicDb();
            _internalHeaders.Load(dbFilename);
            _lastOriginalId  = _internalHeaders.LastId;

            // Load other music db
            foreach (var modDir in Directory.GetDirectories(modsPath))
            {
                // Get directory name, exclude selected mix
                string modName = new DirectoryInfo(modDir).Name;
                if (modName == "_cache" || modName == mixName)
                    continue;

                // Validate whether the mod is a mix mod
                // Do not skip unsupported mods, just read the db and ignore the rest of assets
                dbFilename = Path.Combine(modDir, @"others\music_db.merged.xml");
                if (!File.Exists(dbFilename))
                    continue;

                // Confirmed mod path, append into music db
                _internalHeaders.Load(dbFilename, true);
            }

            // Disabled mixes keep their ids, so enabling one again can't clash with songs added meanwhile
            foreach (string disabled in GetDisabledMixes())
            {
                if (disabled != mixName)
                    _internalHeaders.Load(MixDbFilename(disabled), true);
            }
        }
        #endregion

        #region --- Asset Management ---

        public static void ImportAudio(string source, VoxHeader header, AudioImportOptions opt = null)
        {
            opt = opt ?? AudioImportOptions.Default;
            if (!File.Exists(source))
                throw new FileNotFoundException($"{source} not found", source);

            ImportAudio(source, GetAudioPath(header, opt.Format, opt.IsPreview), opt);
        }

        public static void ImportAudio(string source, VoxHeader header, Difficulty difficulty, AudioImportOptions opt = null)
        {
            opt = opt ?? AudioImportOptions.Default;
            if (!File.Exists(source))
                throw new FileNotFoundException($"{source} not found", source);

            ImportAudio(source, GetAudioPath(header, difficulty, opt.Format, opt.IsPreview), opt);
        }

        private static void ImportAudio(string source, string output, AudioImportOptions opt = null)
        {
            opt = opt ?? AudioImportOptions.Default;
            source = source.ToLower(); // Just in case something wrong with extension casing
            if (!source.ToLower().EndsWith(".2dx") && !source.EndsWith(".s3v") && !source.EndsWith(".asf"))
            {
                if (output.EndsWith(".s3v"))
                {
                    // Encode S3V file using ffmpeg (downloaded on first use when needed). Without
                    // ffmpeg and without normalization, a 2dx file is written instead
                    if (FfmpegProvider.Find() != null || opt.NormalizeLoudness)
                    {
                        S3VTool.Convert(source, output, opt);

                        // Remove conflicting 2dx file
                        string dxFile = Path.ChangeExtension(output, ".2dx");
                        if (File.Exists(dxFile))
                            File.Delete(dxFile);
                    }
                    else
                    {
                        // Fallback: encode as 2dx if ffmpeg not found
                        output = Path.ChangeExtension(output, ".2dx");
                        DxEncoder.Encode(new[] { source }, output, opt);
                    }
                }
                else
                {
                    // Encode 2dx file
                    DxEncoder.Encode(new[] { source }, output, opt);

                    // Remove conflicting file, in case s3v file is exists before hand
                    string s3VFile = Path.ChangeExtension(output, ".s3v");
                    if (File.Exists(s3VFile))
                        File.Delete(s3VFile);
                }
            }
            else
            {
                if (source.EndsWith(".asf"))
                    output = $"{output.Substring(0, output.Length - 4)}.s3v";

                File.Copy(source, output);
            }
        }

        public static void ImportJacket(VoxHeader header, Image image)
        {
            ImportJacket(header, Difficulty.Novice, image);
        }

        public static void ImportJacket(VoxHeader header, Difficulty diff, Image image)
        {
            // Song select thumbnail, 128x128 like the stock jacket archives. It lives under
            // graphics/ where ifs_layeredfs injects it into the game's s_jacketNN.ifs texture.
            SaveJacket(image, ThumbnailSize, $"{GetThumbnailJacketPath(header, diff)}.png");

            // Sizes the game reads straight from the music folder
            SaveJacket(image, 108, $"{GetJacketPath(header, diff)}_s.png");
            SaveJacket(image, 676, $"{GetJacketPath(header, diff)}_b.png");
            SaveJacket(image, 300, $"{GetJacketPath(header, diff)}.png");
        }

        public static void SaveJacket(Image image, int size, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var resized = ResizeJacket(image, size))
                resized.Save(path, ImageFormat.Png);
        }

        // Scaled the way the original tool did it, with the Bitmap constructor's default GDI+
        // filter: the high-quality bicubic filter used for a while made the jackets look soft in
        // the game
        public static Bitmap ResizeJacket(Image image, int size)
        {
            return new Bitmap(image, new Size(size, size));
        }


        public static void ImportVox(VoxHeader header)
        {
            Directory.CreateDirectory(GetMusicPath(header));
            foreach (var level in header.Levels.Values)
            {
                if (level.Chart == null)
                    continue;

                var path = GetVoxPath(header, level.Difficulty);
                level.Chart.Serialize(path);
            }
        }

        // Songs imported before VoxCharger wrote max_exscore get it from their charts on disk
        public static int FillMissingExScores(Action<string> status = null)
        {
            int done = 0;
            foreach (var header in Headers)
            {
                if (header.Levels == null)
                    continue;

                foreach (var level in header.Levels.Values)
                {
                    if (level.Level <= 0 || level.MaxExScore > 0)
                        continue;

                    string path = GetVoxPath(header, level.Difficulty);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        var chart = new VoxChart();
                        chart.Parse(path);
                        level.MaxExScore = ExScoreCalculator.Calculate(chart);
                        if (++done % 50 == 0)
                            status?.Invoke($"Max EX score of {done} charts computed..");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"max ex score of {path}: {ex.Message}");
                    }
                }
            }

            return done;
        }

        public static void ImportVox(VoxHeader header, Difficulty diff, VoxChart chart)
        {
            Directory.CreateDirectory(GetMusicPath(header));
            chart.Serialize(GetVoxPath(header, diff));
        }

        public static void ImportVox(VoxHeader header, Difficulty diff, string content)
        {
            File.WriteAllText(GetVoxPath(header, diff), content);
        }

        public static void DeleteAssets(VoxHeader header)
        {
            string musicPath = GetMusicPath(header);
            if (Directory.Exists(musicPath))
                Directory.Delete(musicPath, true);

            foreach (string jacket in FindThumbnails(header.Id))
                File.Delete(jacket);
        }

        // Deletes the files of one chart: vox, its own audio, jackets and song select thumbnail
        public static void DeleteLevelAssets(VoxHeader header, Difficulty difficulty, string voxPath, string jacketPath, string thumbnailPath)
        {
            var files = new List<string> { voxPath, $"{jacketPath}.png", $"{jacketPath}_b.png", $"{jacketPath}_s.png", $"{thumbnailPath}.png" };
            foreach (var format in new[] { AudioFormat.S3V, AudioFormat.Iidx })
            {
                files.Add(GetAudioPath(header, difficulty, format));
                files.Add(GetAudioPath(header, difficulty, format, true));
            }

            foreach (string file in files)
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    File.Delete(file);
            }
        }

        // Number of songs in a mix, counted without parsing its music db
        public static int CountSongs(string mixName)
        {
            return CountSongsIn(MixDbFilename(mixName));
        }

        public static int CountOriginalSongs()
        {
            return CountSongsIn(Path.Combine(GamePath, @"data\others\music_db.xml"));
        }

        private static int CountSongsIn(string dbFilename)
        {
            try
            {
                if (!File.Exists(dbFilename))
                    return 0;

                string text = File.ReadAllText(dbFilename, Encoding.GetEncoding("Shift_JIS"));
                return System.Text.RegularExpressions.Regex.Matches(text, @"<music\s+id=").Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // Every thumbnail of a song, whichever archive folder it is in (older mixes kept them all in 00)
        public static List<string> FindThumbnails(int musicId)
        {
            var files = new List<string>();
            string graphicsDir = Path.Combine(MixPath, "graphics");
            if (!Directory.Exists(graphicsDir))
                return files;

            foreach (string dir in Directory.GetDirectories(graphicsDir, "s_jacket*_ifs"))
                files.AddRange(Directory.GetFiles(dir, $"jk_{musicId:D4}_*"));

            return files;
        }
        #endregion

        #region --- Asset Identifier ---
        public static int GetNextMusicId()
        {
            // TODO: This probably inefficient in scenario where one or more mix has gap between each ids
            // This could be waste to those gaps, and eat up our precious limited id
            // However, these gaps may indicate deleted song, which should be taken by omnimix

            // With a starting id the mix numbers from there (ids in use by the game, other mixes
            // or this mix are skipped); without one, continue after the highest id anywhere
            // Ids past the limit (from an older version) don't count, new songs would land there too
            int highest = _internalHeaders.Select(h => h.Id).Where(id => id <= MaxMusicId).DefaultIfEmpty(0).Max();
            return GetFreeMusicId(MixStartId > 0 ? MixStartId : highest + 1);
        }

        // First id at or above `from` that no other mix, the game or this mix uses
        public static int GetFreeMusicId(int from)
        {
            int id = from;
            while (!ValidateMusicId(id) || Headers.Contains(id)) // Contains is O(1) so its should be fine
                id++;

            return id;
        }

        public static bool ValidateMusicId(int id)
        {
            return !_internalHeaders.Contains(id) && id != TutorialMusicId;
        }

        // Renames a song's assets after its id changed: the music folder and the files in it
        // (named after the code name and jk_<id>), the jacket thumbnails, the URL sidecar and the manifest.
        public static void RenameMusicAssets(int oldId, int newId, string ascii)
        {
            if (oldId == newId)
                return;

            string oldCode   = $"{oldId:D4}_{ascii}";
            string newCode   = $"{newId:D4}_{ascii}";
            string oldJacket = $"jk_{oldId:D4}_";
            string newJacket = $"jk_{newId:D4}_";

            Func<string, string> rename = name =>
            {
                if (name.StartsWith(oldCode, StringComparison.OrdinalIgnoreCase))
                    return newCode + name.Substring(oldCode.Length);
                if (name.StartsWith(oldJacket, StringComparison.OrdinalIgnoreCase))
                    return newJacket + name.Substring(oldJacket.Length);

                return name;
            };

            string oldDir = Path.Combine(MixPath, "music", oldCode);
            string newDir = Path.Combine(MixPath, "music", newCode);
            if (Directory.Exists(oldDir))
            {
                if (Directory.Exists(newDir))
                    throw new IOException($"Cannot move music {oldId} to {newId}, {newDir} already exists");

                Directory.Move(oldDir, newDir);
                foreach (string file in Directory.GetFiles(newDir))
                {
                    string name = Path.GetFileName(file);
                    string renamed = rename(name);
                    if (renamed != name)
                        File.Move(file, Path.Combine(newDir, renamed));
                }
            }

            // Thumbnails follow the id into the archive folder of the new id
            string thumbnailDir = Path.GetDirectoryName(GetThumbnailJacketPath(newId, 1));
            foreach (string file in FindThumbnails(oldId))
            {
                Directory.CreateDirectory(thumbnailDir);
                File.Move(file, Path.Combine(thumbnailDir, rename(Path.GetFileName(file))));
            }

            MixUrlStore.RenameUrl(MixPath, oldId, newId);

            var manifest = Manifest.Load(MixPath);
            if (manifest.TryGet(oldId, out var entry))
            {
                manifest.Charts.Remove(oldId.ToString(CultureInfo.InvariantCulture));
                entry.Id       = newId;
                entry.CodeName = newCode;

                var outputs = new Dictionary<string, string>();
                foreach (var output in entry.Outputs)
                {
                    string key = output.Key.Replace(oldCode, newCode).Replace(oldJacket, newJacket);
                    if (key.StartsWith("graphics/", StringComparison.OrdinalIgnoreCase))
                        key = $"graphics/s_jacket{ThumbnailArchive(newId):D2}_ifs/{key.Substring(key.LastIndexOf('/') + 1)}";
                    outputs[key] = output.Value;
                }
                entry.Outputs = outputs;

                manifest.Charts[newId.ToString(CultureInfo.InvariantCulture)] = entry;
                manifest.Save(MixPath);
            }
        }

        public static string GetDifficultyCodes(VoxHeader header, Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Novice:   return "1n";
                case Difficulty.Advanced: return "2a";
                case Difficulty.Exhaust:  return "3e";
                default:
                    if (header.InfVersion == InfiniteVersion.Mxm)
                        return "5m";

                    return "4i";
            }
        }

        public static string GetMusicPath(VoxHeader header)
        {
            return Path.Combine(
                MixPath,
                $"music\\{header.CodeName}\\"
            );
        }

        public static string GetVoxPath(VoxHeader header, Difficulty difficulty)
        {
            return Path.Combine(
                GetMusicPath(header), 
                $"{header.CodeName}_{GetDifficultyCodes(header, difficulty)}.vox"
            );
        }

        public static string GetAudioPath(VoxHeader header, AudioFormat format = AudioFormat.S3V, bool preview = false)
        {
            string prefix = preview ? "_pre" : string.Empty;
            string ext    = $"{prefix}" + (format == AudioFormat.S3V ? ".s3v" : ".2dx");
            return Path.Combine(
                GetMusicPath(header),
                $"{header.CodeName}{ext}"
            );
        }

        public static string GetAudioPath(VoxHeader header, Difficulty difficulty, AudioFormat format = AudioFormat.S3V, bool preview = false)
        {
            string prefix = preview ? "_pre" : string.Empty;
            string ext    = $"{prefix}" + (format == AudioFormat.S3V ? ".s3v" : ".2dx");
            return Path.Combine(
                GetMusicPath(header),
                $"{header.CodeName}_{GetDifficultyCodes(header, difficulty)}{ext}"
            );
        }

        public static string GetJacketPath(VoxHeader header, Difficulty difficulty)
        {
            int index = (int)difficulty;
            if (difficulty == Difficulty.Infinite && header.InfVersion == InfiniteVersion.Mxm)
                index = 5;

            return Path.Combine(GetMusicPath(header), $"jk_{header.Id:D4}_{index}");
        }

        public static string GetDefaultJacketPath(VoxHeader header)
        {
            return GetJacketPath(header, Difficulty.Novice);
        }

        public static string GetThumbnailJacketPath(VoxHeader header, Difficulty difficulty)
        {
            int index = (int)difficulty;
            if (difficulty == Difficulty.Infinite && header.InfVersion == InfiniteVersion.Mxm)
                index = 5;

            return GetThumbnailJacketPath(header.Id, index);
        }

        // graphics/s_jacketNN_ifs/jk_<id>_<index>_t, NN chosen by id (see ThumbnailArchive)
        public static string GetThumbnailJacketPath(int musicId, int index)
        {
            return Path.Combine(
                MixPath,
                "graphics",
                $"s_jacket{ThumbnailArchive(musicId):D2}_ifs",
                $"jk_{musicId:D4}_{index}_t"
            );
        }

        public static string GetDefaultThumbnailJacketPath(VoxHeader header)
        {
            return GetThumbnailJacketPath(header, Difficulty.Novice);
        }

        #endregion

        #region --- Utilities ---

        #endregion
    }
}
