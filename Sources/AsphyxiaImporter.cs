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
    //   Download + extract each chart's zip (4 at a time) and hand it to the converters as soon as it
    //   is on disk; parse + export assets with AssetManager.ConvertWorkers converters meanwhile.
    //   Each chart's parsed KSH, chart data, jackets and temp files are released as soon as its
    //   assets are written, so memory stays flat on large lists.
    //   Finally attach headers to AssetManager.Headers (serial, mid order).
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

            ManifestRecorder.BeginBatch();
            try
            {
                FfmpegProvider.Require((p, s) => { progress.SetPercent(p); progress.SetStatus(s); });
                DownloadAndExport(items, progress);
                AttachAll(items, result, progress);
            }
            finally
            {
                ManifestRecorder.EndBatch();
                foreach (var it in items)
                {
                    if (!string.IsNullOrEmpty(it.TempDir))
                        KsmDownloader.Cleanup(it.TempDir);
                }
            }

            return result;
        }

        // With a starting id the list is numbered consecutively from it, in mid order (the list's
        // mids only define the order). Ids of the game, other mixes and the tutorial are skipped,
        // this mix's own songs aren't, so importing the same list again yields the same ids and
        // replaces the songs.
        private static void ApplyStartId(List<Item> items, Progress progress)
        {
            int next = AssetManager.MixStartId;
            if (next <= 0)
                return;

            foreach (var it in items.OrderBy(i => i.Mid).ThenBy(i => i.Song.nauticaId).ToList())
            {
                while (!AssetManager.ValidateMusicId(next))
                    next++;

                if (it.Mid != next)
                    progress.Write($"  ID {it.Mid} -> {next} {it.Song.title}");
                it.Mid = next++;
            }
        }

        // Downloads run 4 at a time (more is rude to ksm.dev's CDN); each finished download goes
        // straight into the queue the converters work through, so converting overlaps downloading
        private static void DownloadAndExport(List<Item> items, Progress progress)
        {
            int downloaded = 0, converted = 0, total = items.Count;
            void Report(string status)
            {
                progress.SetPercent((downloaded + converted) / (2f * Math.Max(1, total)) * 95f);
                progress.SetStatus($"Downloaded {downloaded}/{total}, converted {converted}/{total}: {status}");
            }

            using (var queue = new BlockingCollection<Item>())
            {
                var downloads = Task.Run(() =>
                {
                    try
                    {
                        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, it =>
                        {
                            try
                            {
                                using (var dl = new KsmDownloader())
                                    it.TempDir = dl.DownloadAndExtractDirect(it.Song.downloadUrl);

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
                                Interlocked.Increment(ref downloaded);
                                queue.Add(it);
                                Report(it.Song.title);
                            }
                        });
                    }
                    finally
                    {
                        queue.CompleteAdding();
                    }
                });

                var converters = Enumerable.Range(0, AssetManager.ConvertWorkers).Select(_ => Task.Run(() =>
                {
                    foreach (var it in queue.GetConsumingEnumerable())
                    {
                        if (it.Error == null)
                            Export(it, progress);

                        Interlocked.Increment(ref converted);
                        Report(it.Song.title);
                    }
                })).ToArray();

                Task.WaitAll(converters.Concat(new[] { downloads }).ToArray());
            }
        }

        private static void Export(Item it, Progress progress)
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
                    header.Ascii = VoxHeader.ToAscii(it.Song.title);

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
            }
        }

        // Everything the exporter produced is on disk by now; only the header metadata
        // is needed for music_db. Drop the rest so a large list doesn't pile up
        // jacket bitmaps (unmanaged GDI+ memory), parsed charts and temp files.
        private static void Release(Item it, Dictionary<Difficulty, ChartInfo> charts)
        {
            Ksh.Exporter.Release(it.Header, charts);

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
    }
}
