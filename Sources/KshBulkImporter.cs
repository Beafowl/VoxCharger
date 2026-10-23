using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoxCharger
{
    // Bulk-imports a folder tree of KSH charts into the currently-loaded mix.
    // Every folder that contains .ksh files is scanned, at any depth, so packs
    // that group their songs into sub folders (pack/event/song/*.ksh) work as-is.
    // Each distinct title in a folder is one song.
    //
    // Pipeline:
    //   Phase 0  find song folders, read title/artist of every .ksh, skip songs
    //            the mix already has, assign music ids (serial, folder order)
    //   Phase 1  parse + export assets per song (parallel, ≤ 4). Each song's parsed
    //            KSH, chart data, jackets and temp files are released as soon as its
    //            assets are written, so memory stays flat on packs with thousands of songs.
    //
    // Assets are written to the mix during the import. The caller owns AssetManager
    // state: Initialize + load the mix before, add Result.Imported headers to
    // AssetManager.Headers and save the db after.
    public static class KshBulkImporter
    {
        public class Settings
        {
            public int             StartId      { get; set; }               // lowest id to hand out, raised to the mix's starting id
            public Ksh.ParseOption ParseOption  { get; set; }               // null = defaults
            public GameVersion     Version      { get; set; } = GameVersion.Nabla;
            public InfiniteVersion InfVersion   { get; set; } = InfiniteVersion.Mxm;
            public short           BackgroundId { get; set; }
            public int             GenreId      { get; set; } = 16;
        }

        public class Song
        {
            public string    Folder;
            public List<string> MoreFolders = new List<string>();         // same song elsewhere, fills in missing difficulties
            public string    MainFile;
            public string    Title;
            public string    Artist;
            public int       Id;
            public VoxHeader Header;
            public string    Error;
        }

        public class Result
        {
            public List<Song>   Imported = new List<Song>();
            public int          Skipped;                                   // already in the mix
            public List<string> Errors   = new List<string>();
        }

        // Same rule as Ksh.Parse: UTF-8 when the file has a BOM, Shift-JIS otherwise
        private static readonly Encoding KshEncoding = Encoding.GetEncoding("Shift_JIS");

        public static Result Run(string root, Settings settings, AsphyxiaImporter.Progress progress = null)
        {
            progress = progress ?? new AsphyxiaImporter.Progress();
            settings = settings ?? new Settings();
            var result = new Result();

            progress.SetStatus("Looking for charts...");
            var songs = FindSongs(root, result);

            // Songs the mix already has (same title and artist) are skipped,
            // so running the import again on the same pack doesn't duplicate them
            var existing = new HashSet<string>(AssetManager.Headers.Select(h => SongKey(h.Title, h.Artist)), StringComparer.OrdinalIgnoreCase);
            var pending  = songs.Where(s => !existing.Contains(SongKey(s.Title, s.Artist))).ToList();
            result.Skipped = songs.Count - pending.Count;

            AssignIds(pending, settings.StartId);
            progress.Write($"Found {songs.Count} song(s), {result.Skipped} already in the mix, importing {pending.Count}");

            ManifestRecorder.BeginBatch();
            try
            {
                ExportAll(pending, settings, progress);
            }
            finally
            {
                ManifestRecorder.EndBatch();
            }

            foreach (var song in pending)
            {
                if (song.Error != null)
                    result.Errors.Add($"{song.Folder}: {song.Error}");
                else
                    result.Imported.Add(song);
            }

            progress.SetPercent(100f);
            return result;
        }

        // Every folder under root (root included) holding .ksh files. Walked by hand
        // so a single unreadable folder doesn't abort the whole scan.
        public static List<string> FindSongFolders(string root)
        {
            var folders = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                try
                {
                    if (Directory.EnumerateFiles(dir, "*.ksh").Any())
                        folders.Add(dir);

                    foreach (string sub in Directory.GetDirectories(dir))
                        pending.Push(sub);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Skip folders we can't read
                }
            }

            folders.Sort(StringComparer.OrdinalIgnoreCase);
            return folders;
        }

        private static List<Song> FindSongs(string root, Result result)
        {
            var songs = new List<Song>();

            // The same song (title and artist) in another folder: a difficulty kept in a sub folder of
            // the song, or a pack shipped twice. Its charts fill in difficulties the first one lacks.
            var byKey = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in FindSongFolders(root))
            {
                // One song per distinct title (same comparison as Exporter.GetCharts),
                // the first file (by name) of each title is the main chart
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(folder, "*.ksh").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        ReadTitleArtist(file, out string title, out string artist);
                        if (!seen.Add(Ksh.Exporter.NormalizeTitle(title)))
                            continue;

                        // A sub folder of a song only needs the same title, its charts often name another artist
                        string key = SongKey(Ksh.Exporter.NormalizeTitle(title), artist);
                        if (byKey.TryGetValue(key, out var first) || (first = songs.LastOrDefault(s => IsSubFolder(folder, s.Folder) &&
                                Ksh.Exporter.NormalizeTitle(s.Title) == Ksh.Exporter.NormalizeTitle(title))) != null)
                        {
                            first.MoreFolders.Add(folder);
                            continue;
                        }

                        var song = new Song { Folder = folder, MainFile = file, Title = title, Artist = artist };
                        byKey[key] = song;
                        songs.Add(song);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{file}: {ex.Message}");
                    }
                }
            }

            return songs;
        }

        private static bool IsSubFolder(string folder, string parent)
        {
            return folder.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadTitleArtist(string kshFile, out string title, out string artist)
        {
            title = artist = null;
            foreach (string line in File.ReadLines(kshFile, KshEncoding))
            {
                if (line.Trim() == "--")
                    break;

                var data = line.Split('=');
                if (data.Length < 2)
                    continue;

                switch (data[0].Trim().ToLower())
                {
                    case "title":  title  = data[1].Trim(); break;
                    case "artist": artist = data[1].Trim(); break;
                }
            }
        }

        // music_db is saved as Shift-JIS, characters it can't hold come back changed after a
        // reload ("é" -> "e", "♣" -> "?"). Compare both sides the way they read back from the db,
        // so songs imported in an earlier session are still recognized.
        private static string SongKey(string title, string artist)
        {
            return $"{AsStored(title)}\n{AsStored(artist)}";
        }

        private static string AsStored(string text)
        {
            return KshEncoding.GetString(KshEncoding.GetBytes(text ?? string.Empty));
        }

        // Free ids from the start on, in folder order. Ids of the game, other mixes,
        // this mix and earlier songs of the run are skipped. Without a start id, ids
        // continue after the highest one in use like every other import.
        private static void AssignIds(List<Song> songs, int startId)
        {
            var used = new HashSet<int>();
            int next = startId > 0 ? Math.Max(startId, AssetManager.MixStartId) : AssetManager.GetNextMusicId();
            foreach (var song in songs)
            {
                while (!AssetManager.ValidateMusicId(next) || AssetManager.Headers.Contains(next) || used.Contains(next))
                    next++;

                song.Id = next;
                used.Add(next);
            }
        }

        private static void ExportAll(List<Song> songs, Settings settings, AsphyxiaImporter.Progress progress)
        {
            progress.SetStatus("Converting charts...");
            int done = 0;
            int maxDegree = AssetManager.ConvertWorkers;

            Parallel.ForEach(
                songs,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
                song =>
                {
                    Dictionary<Difficulty, ChartInfo> charts = null;
                    VoxHeader header = null;
                    bool hadAssets   = false;
                    try
                    {
                        // Own copy, LeadInMeasures is set per song
                        var parseOpt = settings.ParseOption?.Clone() ?? new Ksh.ParseOption();

                        var mainKsh = new Ksh();
                        mainKsh.Parse(song.MainFile, parseOpt);

                        charts = Ksh.Exporter.GetCharts(song.Folder, mainKsh.Title);
                        foreach (string folder in song.MoreFolders)
                        {
                            foreach (var chart in Ksh.Exporter.GetCharts(folder, mainKsh.Title))
                            {
                                if (!charts.ContainsKey(chart.Key))
                                    charts[chart.Key] = chart.Value;
                            }
                        }

                        if (charts.Count == 0)
                        {
                            song.Error = "no charts parsed";
                            return;
                        }

                        header = mainKsh.ToHeader();
                        header.Id           = song.Id;
                        header.Version      = settings.Version;
                        header.InfVersion   = settings.InfVersion;
                        header.GenreId      = settings.GenreId;
                        header.BackgroundId = settings.BackgroundId;
                        header.Levels       = new Dictionary<Difficulty, VoxLevelHeader>();

                        // Folder names are often the song code already, fall back to the title for
                        // folders that have no ASCII in their name
                        header.Ascii = VoxHeader.ToAscii(Path.GetFileName(song.Folder));
                        if (header.Ascii == "custom")
                            header.Ascii = VoxHeader.ToAscii(mainKsh.Title);

                        var audioOptions = AudioImportOptions.WithFormat(AudioFormat.Iidx);
                        audioOptions.NormalizeLoudness = true;
                        audioOptions.MusicOffsetMs     = mainKsh.MusicOffset;
                        header.Volume = audioOptions.TargetVolume;

                        // Same auto lead-in / tail-fade as every other import path;
                        // the per-difficulty re-parses need the same measure shift
                        parseOpt.LeadInMeasures = Ksh.ApplyAutoLeadInTo(mainKsh, audioOptions);

                        hadAssets = Directory.Exists(AssetManager.GetMusicPath(header));

                        var exporter = new Ksh.Exporter(mainKsh);
                        exporter.Export(header, charts, parseOpt, audioOptions);
                        exporter.Action?.Invoke();

                        song.Header = header;
                        progress.Write($"  OK   ID {header.Id} {header.Title}");
                    }
                    catch (Exception ex)
                    {
                        song.Error = ex.Message;
                        progress.Write($"  FAIL ID {song.Id} {song.Folder}: {ex.Message}");

                        // Charts are written before the audio, don't leave a half-written song
                        // in the mix that the db doesn't know about
                        if (header != null && !hadAssets)
                        {
                            try { AssetManager.DeleteAssets(header); }
                            catch { /* best effort */ }
                        }
                    }
                    finally
                    {
                        Ksh.Exporter.Release(header, charts);

                        int n = Interlocked.Increment(ref done);
                        progress.SetPercent((n / (float)songs.Count) * 100f);
                        progress.SetStatus($"Converted {n}/{songs.Count}: {song.Title}");
                    }
                });
        }
    }
}
