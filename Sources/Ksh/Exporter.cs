using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VoxCharger
{
    

    public class ChartInfo
    {
        public Ksh Source { get; set; }
        public VoxLevelHeader Header { get; set; }
        public string FileName { get; set; }
        public string MusicFileName { get; set; }

        public ChartInfo(Ksh source, VoxLevelHeader header, string fileName)
        {
            Source = source;
            Header = header;
            FileName = fileName;
        }
    }

    public partial class Ksh
    {
        public class Exporter
        {
            public Ksh Source       { get; private set; } = null;
            public Action Action    { get; private set; } = null;

            public Exporter(Ksh src)
            {
                Source = src;
            }

            public void Export(VoxHeader header, ParseOption options = null)
            {
                Export(header, null, options);
            }

            public void Export(VoxHeader header, Dictionary<Difficulty, ChartInfo> charts, ParseOption options = null, AudioImportOptions importOptions = null)
            {
                // Assign level info and charts
                foreach (var chart in charts)
                {
                    var info = chart.Value;
                    if (!File.Exists(info.FileName))
                        throw new IOException($"Chart file was moved or deleted\n{info.FileName}");

                    // If you happen to read the source, this is probably what you're looking for
                    var ksh = new Ksh();
                    ksh.Parse(info.FileName, options);

                    // Conversion is actually boring because its already "pre-converted"
                    var vox = new VoxChart();
                    vox.Import(ksh);

                    var level   = ksh.ToLevelHeader();
                    level.Chart = vox;
                    level.Radar = RadarCalculator.Calculate(vox, level.Level);

                    info.Source = ksh;
                    header.Levels[chart.Key] = charts[chart.Key].Header = level;
                }

                // Prevent resource files being moved or deleted, copy them into temporary storage
                string musicFile = string.Empty;
                string tmpFile = string.Empty;
                foreach (var chart in charts)
                {
                    var info = chart.Value;

                    // Make sure to reuse 2dx / s3v file for music that share same file
                    // Compare against the original source music path, not ChartInfo.MusicFileName
                    string music = Path.Combine(Path.GetDirectoryName(info.FileName) ?? "", info.Source.MusicFileName);
                    if (string.IsNullOrEmpty(musicFile) || music != musicFile)
                    {
                        if (File.Exists(music))
                        {
                            string tmp = Path.Combine(
                                Path.GetTempPath(),
                                $"{Path.GetRandomFileName()}{new FileInfo(info.Source.MusicFileName).Extension}"
                            );

                            musicFile = music;
                            info.MusicFileName = tmpFile = tmp;

                            File.Copy(music, tmp);
                        }
                        else
                            info.MusicFileName = string.Empty;
                    }
                    else
                        info.MusicFileName = tmpFile;

                    string jacket = Path.Combine(Path.GetDirectoryName(info.FileName), info.Source.JacketFileName);
                    if (File.Exists(jacket))
                    {                       
                        try
                        {
                            using (var image = Image.FromFile(jacket))
                                info.Header.Jacket = new Bitmap(image);
                        }
                        catch (Exception)
                        {
                            info.Header.Jacket = null;
                        }                    
                    }
                }

                Action = () =>
                {
                    bool unique = false;
                    musicFile = charts.Values.First().MusicFileName;
                    foreach (var chart in charts)
                    {
                        if (chart.Value.MusicFileName != musicFile)
                        {
                            unique = true;
                            break;
                        }
                    }

                    // Build preview options with KSH preview offset if not already specified
                    var previewOptions = (importOptions ?? AudioImportOptions.Default).AsPreview();
                    if (previewOptions.PreviewOffset == 0)
                    {
                        var src = charts.Values.Select(c => c.Source).FirstOrDefault(s => s != null && s.PreviewOffset > 0);
                        if (src != null)
                            previewOptions.PreviewOffset = src.PreviewOffset / 1000;
                    }

                    // Without any music there's nothing to play; name the file the chart asks for
                    if (!charts.Values.Any(c => !string.IsNullOrEmpty(c.MusicFileName) && File.Exists(c.MusicFileName)))
                    {
                        string wanted = charts.Values.Select(c => c.Source?.MusicFileName).FirstOrDefault(m => !string.IsNullOrEmpty(m));
                        throw new FileNotFoundException(wanted != null
                            ? $"music file not found: {wanted}"
                            : "the chart names no music file (m=)");
                    }

                    // Import all music assets
                    AssetManager.ImportVox(header);

                    // Make sure to use single asset for music for shared music file
                    if (!unique)
                    {
                        AssetManager.ImportAudio(musicFile, header, importOptions);
                        AssetManager.ImportAudio(musicFile, header, previewOptions);
                    }

                    var highest = charts.Max(e => e.Key);
                    foreach (var entry in charts)
                    {
                        var chart = entry.Value;
                        if (unique && File.Exists(chart.MusicFileName))
                        {
                            AssetManager.ImportAudio(chart.MusicFileName, header, chart.Header.Difficulty, importOptions);

                            // Use shared preview using highest level music file
                            if (entry.Key == highest)
                                AssetManager.ImportAudio(chart.MusicFileName, header, previewOptions);
                        }

                        if (chart.Header.Jacket != null)
                            AssetManager.ImportJacket(header, chart.Header.Difficulty, chart.Header.Jacket);
                    }

                    // Manifest update: hash every source we read (per-difficulty
                    // KSH, original audio, jacket) and every output we wrote, so
                    // MixRepairer can later detect sync corruption / source drift.
                    try
                    {
                        ManifestRecorder.RecordExport(header, charts, importOptions);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"manifest update failed for {header.CodeName}: {ex.Message}");
                    }
                };
            }

            // Once Action has run, everything the export produced is on disk and only the header
            // metadata is needed for music_db. Drops jacket bitmaps (unmanaged GDI+ memory), chart
            // data, parsed sources and the temp music copies, so bulk imports don't pile them up.
            public static void Release(VoxHeader header, Dictionary<Difficulty, ChartInfo> charts)
            {
                if (header?.Levels != null)
                {
                    foreach (var level in header.Levels.Values)
                    {
                        level.Jacket?.Dispose();
                        level.Jacket = null;
                        level.Chart  = null;
                    }
                }

                if (charts == null)
                    return;

                foreach (var info in charts.Values)
                {
                    info.Header?.Jacket?.Dispose();
                    if (info.Header != null)
                        info.Header.Jacket = null;

                    info.Source = null;

                    // Export copies the music into %TEMP% under a random name
                    if (!string.IsNullOrEmpty(info.MusicFileName) && File.Exists(info.MusicFileName))
                    {
                        try { File.Delete(info.MusicFileName); }
                        catch { /* best effort */ }
                    }
                }
            }

            // Difficulties of one song sometimes differ in spacing or case of the title
            // ("friends! ~" / "friends!~"), compare without either
            public static bool IsSameTitle(string a, string b)
            {
                return string.Equals(NormalizeTitle(a), NormalizeTitle(b), StringComparison.OrdinalIgnoreCase);
            }

            public static string NormalizeTitle(string title)
            {
                return new string((title ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
            }

            public static Dictionary<Difficulty, ChartInfo> GetCharts(string dir, string title)
            {
                // Try to locate another difficulty
                var charts = new Dictionary<Difficulty, ChartInfo>();
                foreach (string fn in Directory.GetFiles(dir, "*.ksh"))
                {
                    try
                    {
                        var chart = new Ksh();
                        chart.Parse(fn);

                        // Different chart
                        if (!IsSameTitle(chart.Title, title))
                            continue;

                        charts[chart.Difficulty] = new ChartInfo(chart, chart.ToLevelHeader(), fn);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Failed attempt to parse ksh file: {0} ({1})", fn, ex.Message);
                    }
                }

                return charts;
            }

        }
    }
}
