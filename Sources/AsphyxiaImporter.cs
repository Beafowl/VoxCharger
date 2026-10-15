using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoxCharger
{
    // Bulk-imports an Asphyxia curated list (the JSON shape produced by the
    // server's nauticaExportList endpoint) into the currently-loaded mix.
    // Mids from the JSON are preserved verbatim — every client that imports
    // the same list gets the same chart→mid mapping the server has.
    //
    // Pipeline:
    //   Phase 0  download + extract every chart's zip (parallel, ≤ 4)
    //   Phase 1  parse + export assets per chart (parallel, ≤ ProcessorCount, capped at 4)
    //            Each chart's parsed KSH, chart data, jackets and temp files are released
    //            as soon as its assets are written, so memory stays flat on large lists.
    //   Phase 2  attach headers to AssetManager.Headers (serial, mid order)
    //
    // The caller owns AssetManager state — Initialize + LoadOrRepairMix
    // before calling, and Save after success. The CLI driver and the GUI
    // menu handler both follow that contract.
    public static class AsphyxiaImporter
    {
        public class Progress
        {
            public Action<string> Status;
            public Action<float>  Percent;
            public Action<string> Log;

            internal void SetStatus(string s)  { Status?.Invoke(s); }
            internal void SetPercent(float p)  { Percent?.Invoke(p); }
            internal void Write(string s)      { Log?.Invoke(s); }
        }

        public class Result
        {
            public int          Imported;
            public int          Failed;
            public List<string> Errors = new List<string>();
        }

        // Per-chart state as it travels through the pipeline. Mutable so
        // each phase can attach its outputs without re-keying a dictionary.
        private class Item
        {
            public AsphyxiaList.Song Song;
            public int               Mid;              // song.mid, or remapped to the mix's starting id
            public string            TempDir;          // root of the per-chart download
            public string            KshPath;          // resolved .ksh under TempDir
            public VoxHeader         Header;
            public string            Error;            // first error wins; later phases skip
        }

        public static Result Run(IList<AsphyxiaList.Song> songs, Progress progress = null)
        {
            progress = progress ?? new Progress();
            var result = new Result();

            if (songs == null || songs.Count == 0)
                return result;

            var items = songs.Select(s => new Item { Song = s, Mid = s.mid }).ToList();
            ApplyStartId(items, progress);

            try
            {
                DownloadAll(items, progress);
                ExportAll(items, progress);
                AttachAll(items, result, progress);
            }
            finally
            {
                foreach (var it in items)
                {
                    if (!string.IsNullOrEmpty(it.TempDir))
                        KsmDownloader.Cleanup(it.TempDir);
                }
            }

            return result;
        }

        // Songs below the mix's starting id move to the first free ids at or above it, in mid order.
        // Ids kept by other songs of the list count as taken, this mix's own songs don't, so importing
        // the same list again maps every song to the same id and replaces it.
        private static void ApplyStartId(List<Item> items, Progress progress)
        {
            int startId = AssetManager.MixStartId;
            if (startId <= 0)
                return;

            var taken = new HashSet<int>(items.Where(i => i.Mid >= startId).Select(i => i.Mid));
            int next  = startId;
            foreach (var it in items.Where(i => i.Mid < startId).OrderBy(i => i.Mid))
            {
                while (taken.Contains(next) || !AssetManager.ValidateMusicId(next))
                    next++;

                progress.Write($"  ID {it.Mid} -> {next} {it.Song.title}");
                it.Mid = next;
                taken.Add(next);
            }
        }

        private static void DownloadAll(List<Item> items, Progress progress)
        {
            progress.SetStatus($"Downloading {items.Count} chart(s)...");
            int done = 0;
            // Single-flight per chart, but up to 4 charts concurrently. Higher
            // is rude to ksm.dev's CDN and saturates a typical home upload-side
            // before it helps the download path.
            using (var sem = new SemaphoreSlim(4))
            {
                var tasks = items.Select(it => Task.Run(() =>
                {
                    sem.Wait();
                    try
                    {
                        using (var dl = new KsmDownloader())
                        {
                            it.TempDir = dl.DownloadAndExtractDirect(it.Song.downloadUrl);
                        }
                        it.KshPath = KsmDownloader.FindKshFile(it.TempDir);
                        if (it.KshPath == null)
                            it.Error = "no .ksh in archive";
                    }
                    catch (Exception ex)
                    {
                        it.Error = "download failed: " + ex.Message;
                    }
                    finally
                    {
                        int n = Interlocked.Increment(ref done);
                        progress.SetPercent((n / (float)items.Count) * 33f);
                        progress.SetStatus($"Downloaded {n}/{items.Count}: {it.Song.title}");
                        sem.Release();
                    }
                })).ToArray();
                Task.WaitAll(tasks);
            }
        }

        private static void ExportAll(List<Item> items, Progress progress)
        {
            progress.SetStatus("Converting assets...");
            int done = 0;
            var live = items.Where(i => i.Error == null).ToList();
            int maxDegree = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));

            Parallel.ForEach(
                live,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
                it =>
                {
                    Dictionary<Difficulty, ChartInfo> charts = null;
                    try
                    {
                        var mainKsh = new Ksh();
                        mainKsh.Parse(it.KshPath);
                        string folder = Path.GetDirectoryName(Path.GetFullPath(it.KshPath));
                        charts = Ksh.Exporter.GetCharts(folder, mainKsh.Title);
                        if (charts.Count == 0)
                        {
                            it.Error = "no charts parsed";
                            return;
                        }

                        var header = mainKsh.ToHeader();
                        header.Id          = it.Mid;
                        header.Version     = GameVersion.Nabla;
                        header.GenreId     = 16;
                        header.BackgroundId = 0;
                        header.Levels      = new Dictionary<Difficulty, VoxLevelHeader>();

                        // Asphyxia's curated list carries cleaner ksm.dev
                        // metadata than what `Ksh.ToHeader()` reads from
                        // the file's title/artist tags (which are often
                        // truncated or wrongly-encoded by upstream
                        // converters). Prefer the JSON when present.
                        if (!string.IsNullOrWhiteSpace(it.Song.title))
                            header.Title = it.Song.title;
                        if (!string.IsNullOrWhiteSpace(it.Song.artist))
                            header.Artist = it.Song.artist;

                        // Stable ASCII code derived from the title; same
                        // sanitization the asphyxia bulk path uses.
                        if (string.IsNullOrEmpty(header.Ascii))
                            header.Ascii = SanitizeAscii(it.Song.title);

                        var audioOptions = AudioImportOptions.WithFormat(AudioFormat.Iidx);
                        audioOptions.NormalizeLoudness = true;
                        audioOptions.MusicOffsetMs     = mainKsh.MusicOffset;
                        var parseOpt = Program.ApplyLeadInAndTailPublic(mainKsh, audioOptions);
                        header.Volume = audioOptions.TargetVolume;

                        var exporter = new Ksh.Exporter(mainKsh);
                        exporter.Export(header, charts, parseOpt, audioOptions);

                        if (exporter.Action != null)
                            exporter.Action.Invoke();

                        it.Header = header;
                        progress.Write($"  OK   ID {header.Id} {header.Title}");
                    }
                    catch (Exception ex)
                    {
                        it.Error = "convert failed: " + ex.Message;
                        progress.Write($"  FAIL ID {it.Mid} {it.Song.title}: {ex.Message}");
                    }
                    finally
                    {
                        Release(it, charts);

                        int n = Interlocked.Increment(ref done);
                        progress.SetPercent(33f + (n / (float)live.Count) * 62f);
                        progress.SetStatus($"Converted {n}/{live.Count}: {it.Song.title}");
                    }
                });
        }

        // Everything the exporter produced is on disk by now; only the header metadata
        // is needed for music_db. Drop the rest so a large list doesn't pile up
        // jacket bitmaps (unmanaged GDI+ memory), parsed charts and temp files.
        private static void Release(Item it, Dictionary<Difficulty, ChartInfo> charts)
        {
            if (it.Header?.Levels != null)
            {
                foreach (var level in it.Header.Levels.Values)
                {
                    level.Jacket?.Dispose();
                    level.Jacket = null;
                    level.Chart  = null;
                }
            }

            if (charts != null)
            {
                foreach (var info in charts.Values)
                {
                    info.Header?.Jacket?.Dispose();
                    if (info.Header != null)
                        info.Header.Jacket = null;

                    info.Source = null;

                    // Exporter copies the music into %TEMP% under a random name
                    if (!string.IsNullOrEmpty(info.MusicFileName) && File.Exists(info.MusicFileName))
                    {
                        try { File.Delete(info.MusicFileName); }
                        catch { /* best effort */ }
                    }
                }
            }

            if (!string.IsNullOrEmpty(it.TempDir))
            {
                KsmDownloader.Cleanup(it.TempDir);
                it.TempDir = null;
            }
        }

        private static void AttachAll(List<Item> items, Result result, Progress progress)
        {
            progress.SetStatus("Recording headers...");
            // mid order keeps the saved music_db.merged.xml deterministic.
            foreach (var it in items.OrderBy(i => i.Mid))
            {
                if (it.Error != null)
                {
                    result.Errors.Add($"#{it.Mid} {it.Song.title}: {it.Error}");
                    result.Failed++;
                    continue;
                }
                if (it.Header == null) { result.Failed++; continue; }
                AssetManager.Headers.Add(it.Header);
                result.Imported++;
            }
            progress.SetPercent(100f);
        }

        // Mirror of the asphyxia plugin's sanitizeAscii: lowercase ASCII
        // alphanumerics from the title, underscored, capped at 16 chars.
        private static string SanitizeAscii(string title)
        {
            if (string.IsNullOrEmpty(title)) return "custom";
            var chars = new char[title.Length];
            for (int i = 0; i < title.Length; i++)
            {
                char c = title[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                chars[i] = ok ? char.ToLowerInvariant(c) : '_';
            }
            string s = new string(chars);
            while (s.Contains("__")) s = s.Replace("__", "_");
            s = s.Trim('_');
            if (s.Length > 16) s = s.Substring(0, 16);
            return s.Length == 0 ? "custom" : s;
        }
    }
}
