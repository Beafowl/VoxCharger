using System;
using System.Collections.Generic;
using System.Drawing;
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

            _lastOriginalId = 0;
            GamePath = gamePath;
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

        // Moves every song below startId to the first free ids at or above it.
        // Ids change in memory right away; the returned actions rename the assets on disk
        // and must run before the db is saved.
        public static List<Action> SetMixStartId(int startId)
        {
            MixStartId = startId;

            var actions = new List<Action>();
            if (startId <= 0)
                return actions;

            foreach (var header in Headers.Where(h => h.Id < startId).OrderBy(h => h.Id).ToList())
            {
                int oldId    = header.Id;
                int newId    = GetFreeMusicId(startId);
                string ascii = header.Ascii;

                Headers.ChangeId(header, newId);
                actions.Add(() => RenameMusicAssets(oldId, newId, ascii));
            }

            return actions;
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
                    // Encode S3V file using ffmpeg (used for preview audio)
                    // Fall back to 2dx if ffmpeg is not available
                    if (FindExecutable(S3VTool.ConverterFileName) != null)
                    {
                        S3VTool.ConverterFileName = FindExecutable(S3VTool.ConverterFileName);
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
            // Texture Small
            Image texSmall = new Bitmap(image, new Size(130, 130));
            texSmall.Save($"{GetThumbnailJacketPath(header, diff)}.png", ImageFormat.Png);
            texSmall.Dispose();
            texSmall = new Bitmap(image, new Size(108, 108));
            texSmall.Save($"{GetJacketPath(header, diff)}_s.png", ImageFormat.Png);
            texSmall.Dispose();

            // Texture Big
            Image texBig = new Bitmap(image, new Size(676, 676));
            texBig.Save($"{GetJacketPath(header, diff)}_b.png", ImageFormat.Png);
            texBig.Dispose();

            // Texture Normal
            Image texNormal = new Bitmap(image, new Size(300, 300));
            texNormal.Save($"{GetJacketPath(header, diff)}.png", ImageFormat.Png);
            texNormal.Dispose();
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

            string jacketPath = Path.Combine(
                MixPath,
                $"graphics\\s_jacket00_ifs\\"
            );

            string pattern = $"jk_{header.Id:D4}*";
            foreach (string jacket in Directory.GetFiles(jacketPath, pattern))
            {
                if (File.Exists(jacket))
                    File.Delete(jacket);
            }
        }
        #endregion

        #region --- Asset Identifier ---
        public static int GetNextMusicId()
        {
            // TODO: This probably inefficient in scenario where one or more mix has gap between each ids
            // This could be waste to those gaps, and eat up our precious limited id
            // However, these gaps may indicate deleted song, which should be taken by omnimix

            return GetFreeMusicId(Math.Max(_internalHeaders.LastId + 1, MixStartId));
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

            string graphicsDir = Path.Combine(MixPath, "graphics");
            if (Directory.Exists(graphicsDir))
            {
                foreach (string dir in Directory.GetDirectories(graphicsDir, "s_jacket*_ifs"))
                {
                    foreach (string file in Directory.GetFiles(dir, $"{oldJacket}*"))
                        File.Move(file, Path.Combine(dir, rename(Path.GetFileName(file))));
                }
            }

            MixUrlStore.RenameUrl(MixPath, oldId, newId);

            var manifest = Manifest.Load(MixPath);
            if (manifest.TryGet(oldId, out var entry))
            {
                manifest.Charts.Remove(oldId.ToString(CultureInfo.InvariantCulture));
                entry.Id       = newId;
                entry.CodeName = newCode;
                entry.Outputs  = entry.Outputs.ToDictionary(
                    output => output.Key.Replace(oldCode, newCode).Replace(oldJacket, newJacket),
                    output => output.Value
                );

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

            string thumbnailDir = "s_jacket00_ifs";
            string graphicsDir  = Path.Combine(MixPath, @"graphics\");

            if (Directory.Exists(graphicsDir))
            {
                foreach (string dir in Directory.GetDirectories(graphicsDir))
                {
                    string name = new DirectoryInfo(dir).Name;
                    if (name.StartsWith("s_jacket") && name.EndsWith("_ifs"))
                    {
                        thumbnailDir = name;
                        break;
                    }
                }
            }

            return Path.Combine(
                graphicsDir,
                $"{thumbnailDir}\\",
                $"jk_{header.Id:D4}_{index}_t"
            );
        }

        public static string GetDefaultThumbnailJacketPath(VoxHeader header)
        {
            return GetThumbnailJacketPath(header, Difficulty.Novice);
        }

        #endregion

        #region --- Utilities ---

        private static string FindExecutable(string fileName)
        {
            if (File.Exists(fileName))
                return Path.GetFullPath(fileName);

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? new string[0];
            foreach (var dir in paths)
            {
                string full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full))
                    return full;
            }

            return null;
        }

        #endregion
    }
}
