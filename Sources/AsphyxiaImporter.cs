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
    //   Phase 1  parse KSH metadata (parallel, ≤ ProcessorCount)
    //   Phase 2  export assets (parallel, ≤ ProcessorCount, capped at 4)
    //   Phase 3  attach headers to AssetManager.Headers (serial, mid order)
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
            public string            TempDir;          // root of the per-chart download
            public string            KshPath;          // resolved .ksh under TempDir
            public Ksh               MainKsh;
            public Dictionary<Difficulty, ChartInfo> Charts;
            public VoxHeader         Header;
            public Action            ExportAction;     // deferred IO action from exporter.Export
            public string            Error;            // first error wins; later phases skip
        }

        public static Result Run(IList<AsphyxiaList.Song> songs, Progress progress = null)
        {
            progress = progress ?? new Progress();
            var result = new Result();

            if (songs == null || songs.Count == 0)
                return result;

            var items = songs.Select(s => new Item { Song = s }).ToList();

            try
            {
                DownloadAll(items, progress);
                ParseAll(items, progress);
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

        private static void ParseAll(List<Item> items, Progress progress)
        {
            progress.SetStatus("Parsing charts...");
            int done = 0;
            var live = items.Where(i => i.Error == null).ToList();
            Parallel.ForEach(live, it =>
            {
                try
                {
                    it.MainKsh = new Ksh();
                    it.MainKsh.Parse(it.KshPath);
                    string folder = Path.GetDirectoryName(Path.GetFullPath(it.KshPath));
                    it.Charts = Ksh.Exporter.GetCharts(folder, it.MainKsh.Title);
                    if (it.Charts.Count == 0)
                        it.Error = "no charts parsed";
                }
                catch (Exception ex)
                {
                    it.Error = "parse failed: " + ex.Message;
                }
                finally
                {
                    int n = Interlocked.Increment(ref done);
                    progress.SetPercent(33f + (n / (float)live.Count) * 17f);
                }
            });
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
                    try
                    {
                        var header = it.MainKsh.ToHeader();
                        header.Id          = it.Song.mid;
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
                        audioOptions.MusicOffsetMs     = it.MainKsh.MusicOffset;
                        var parseOpt = Program.ApplyLeadInAndTailPublic(it.MainKsh, audioOptions);
                        header.Volume = audioOptions.TargetVolume;

                        var exporter = new Ksh.Exporter(it.MainKsh);
                        exporter.Export(header, it.Charts, parseOpt, audioOptions);

                        if (exporter.Action != null)
                            exporter.Action.Invoke();

                        it.Header       = header;
                        it.ExportAction = exporter.Action;
                        progress.Write($"  OK   ID {header.Id} {header.Title}");
                    }
                    catch (Exception ex)
                    {
                        it.Error = "convert failed: " + ex.Message;
                        progress.Write($"  FAIL ID {it.Song.mid} {it.Song.title}: {ex.Message}");
                    }
                    finally
                    {
                        int n = Interlocked.Increment(ref done);
                        progress.SetPercent(50f + (n / (float)live.Count) * 45f);
                        progress.SetStatus($"Converted {n}/{live.Count}: {it.Song.title}");
                    }
                });
        }

        private static void AttachAll(List<Item> items, Result result, Progress progress)
        {
            progress.SetStatus("Recording headers...");
            // mid order keeps the saved music_db.merged.xml deterministic.
            foreach (var it in items.OrderBy(i => i.Song.mid))
            {
                if (it.Error != null)
                {
                    result.Errors.Add($"#{it.Song.mid} {it.Song.title}: {it.Error}");
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
