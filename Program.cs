using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoxCharger
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        static void Main(string[] args)
        {
            // Fix numeric float / double / decimal separator from comma to dots for certain System Locales.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            if (args.Length > 0)
                RunCli(args);
            else
                RunGui();
        }

        private static void RunGui()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void RunCli(string[] args)
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
                AllocConsole();

            // Check for --debug flag anywhere in args
            var argList = new List<string>(args);
            if (argList.Remove("--debug"))
                DebugMode = true;

            if (argList.Count == 0)
            {
                PrintUsage();
                return;
            }

            string input = argList[0];

            if (input == "--help" || input == "-h")
            {
                PrintUsage();
                return;
            }

            if (input == "--full-import")
            {
                RunFullImport(argList);
                return;
            }

            if (input == "--bulk-import")
            {
                RunBulkImport(argList);
                return;
            }

            string output = argList.Count > 1 ? argList[1] : null;

            if (File.Exists(input))
            {
                if (!input.EndsWith(".ksh", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Error: Input file must be a .ksh file.");
                    return;
                }

                string outputPath = output ?? Path.ChangeExtension(input, ".vox");
                ConvertFile(input, outputPath);
            }
            else if (Directory.Exists(input))
            {
                string outputDir = output ?? input;
                ConvertDirectory(input, outputDir);
            }
            else
            {
                Console.Error.WriteLine($"Error: '{input}' not found.");
            }
        }

        private static bool DebugMode { get; set; }

        private static void RunFullImport(List<string> argList)
        {
            // Parse arguments
            string kshPath = null;
            string gamePath = null;
            string mixName = null;
            int musicId = -1;
            string musicCode = null;

            for (int i = 1; i < argList.Count; i++)
            {
                switch (argList[i])
                {
                    case "--game-path":
                        if (i + 1 < argList.Count) gamePath = argList[++i];
                        break;
                    case "--mix":
                        if (i + 1 < argList.Count) mixName = argList[++i];
                        break;
                    case "--music-id":
                        if (i + 1 < argList.Count) int.TryParse(argList[++i], out musicId);
                        break;
                    case "--music-code":
                        if (i + 1 < argList.Count) musicCode = argList[++i];
                        break;
                    default:
                        if (kshPath == null) kshPath = argList[i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(kshPath) || string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(mixName))
            {
                Console.Error.WriteLine("Error: --full-import requires <ksh_file>, --game-path, and --mix");
                Console.Error.WriteLine("Usage: VoxCharger.exe --full-import <input.ksh> --game-path <path> --mix <name> [--music-id <id>] [--music-code <code>]");
                return;
            }

            if (!File.Exists(kshPath))
            {
                Console.Error.WriteLine($"Error: KSH file not found: {kshPath}");
                return;
            }

            try
            {
                Console.WriteLine($"Initializing AssetManager with game path: {gamePath}");
                AssetManager.Initialize(gamePath);

                // Load or create mix
                string mixPath = Path.Combine(gamePath, "data_mods", mixName);
                if (Directory.Exists(mixPath) && File.Exists(Path.Combine(mixPath, "others", "music_db.merged.xml")))
                {
                    Console.WriteLine($"Loading existing mix: {mixName}");
                    AssetManager.LoadMix(mixName);
                }
                else
                {
                    Console.WriteLine($"Creating new mix: {mixName}");
                    AssetManager.CreateMix(mixName);
                }

                // Parse the main KSH file
                Console.WriteLine($"Parsing: {kshPath}");
                var mainKsh = new Ksh();
                mainKsh.Parse(kshPath);

                // Build header from KSH metadata
                var header = mainKsh.ToHeader();
                if (musicId > 0)
                    header.Id = musicId;
                else
                    header.Id = AssetManager.GetNextMusicId();

                if (!string.IsNullOrEmpty(musicCode))
                    header.Ascii = musicCode;

                header.Version = GameVersion.Nabla;
                header.GenreId = 16;
                header.BackgroundId = 0;
                header.Levels = new Dictionary<Difficulty, VoxLevelHeader>();

                Console.WriteLine($"Music ID: {header.Id}");
                Console.WriteLine($"Title: {header.Title}");
                Console.WriteLine($"Artist: {header.Artist}");
                Console.WriteLine($"ASCII: {header.Ascii}");

                // Find all charts in the same directory with matching title
                string chartDir = Path.GetDirectoryName(Path.GetFullPath(kshPath));
                var charts = Ksh.Exporter.GetCharts(chartDir, mainKsh.Title);

                if (charts.Count == 0)
                {
                    Console.Error.WriteLine("Error: No charts found.");
                    return;
                }

                Console.WriteLine($"Found {charts.Count} chart(s):");
                foreach (var entry in charts)
                    Console.WriteLine($"  {entry.Key}: Level {entry.Value.Header.Level}");

                // Run the full export pipeline
                var exporter = new Ksh.Exporter(mainKsh);
                var audioOptions = AudioImportOptions.WithFormat(AudioFormat.Iidx);
                exporter.Export(header, charts, null, audioOptions);

                // Execute the deferred import action
                if (exporter.Action != null)
                {
                    Console.WriteLine("Importing assets...");
                    exporter.Action.Invoke();
                }

                // Save the music database
                Console.WriteLine("Updating music database...");
                AssetManager.Headers.Add(header);
                AssetManager.Headers.Save(AssetManager.MdbFilename);

                Console.WriteLine($"Done! Song imported as ID {header.Id} in mix '{mixName}'");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (DebugMode)
                    Console.Error.WriteLine(ex.StackTrace);
            }
        }

        private static void RunBulkImport(List<string> argList)
        {
            string inputDir = null;
            string gamePath = null;
            string mixName = null;
            int startId = -1;

            for (int i = 1; i < argList.Count; i++)
            {
                switch (argList[i])
                {
                    case "--game-path":
                        if (i + 1 < argList.Count) gamePath = argList[++i];
                        break;
                    case "--mix":
                        if (i + 1 < argList.Count) mixName = argList[++i];
                        break;
                    case "--start-id":
                        if (i + 1 < argList.Count) int.TryParse(argList[++i], out startId);
                        break;
                    default:
                        if (inputDir == null) inputDir = argList[i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(inputDir) || string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(mixName))
            {
                Console.Error.WriteLine("Error: --bulk-import requires <input_dir>, --game-path, and --mix");
                Console.Error.WriteLine("Usage: VoxCharger.exe --bulk-import <dir_with_song_folders> --game-path <path> --mix <name> [--start-id <id>]");
                return;
            }

            if (!Directory.Exists(inputDir))
            {
                Console.Error.WriteLine($"Error: Directory not found: {inputDir}");
                return;
            }

            var sw = Stopwatch.StartNew();

            try
            {
                AssetManager.Initialize(gamePath);

                string mixPath = Path.Combine(gamePath, "data_mods", mixName);
                if (Directory.Exists(mixPath) && File.Exists(Path.Combine(mixPath, "others", "music_db.merged.xml")))
                    AssetManager.LoadMix(mixName);
                else
                    AssetManager.CreateMix(mixName);

                // Find all song folders (each should contain .ksh files)
                var songFolders = Directory.GetDirectories(inputDir)
                    .Where(d => Directory.GetFiles(d, "*.ksh").Length > 0)
                    .ToArray();

                if (songFolders.Length == 0)
                {
                    Console.Error.WriteLine("No song folders with .ksh files found.");
                    return;
                }

                Console.WriteLine($"Found {songFolders.Length} song folder(s)");
                Console.WriteLine();

                // Phase 1: Parse all charts in parallel
                Console.WriteLine("Phase 1: Parsing charts...");
                var parsedSongs = new ConcurrentBag<(string folder, Ksh mainKsh, Dictionary<Difficulty, ChartInfo> charts, string error)>();

                Parallel.ForEach(songFolders, folder =>
                {
                    try
                    {
                        var kshFiles = Directory.GetFiles(folder, "*.ksh");
                        if (kshFiles.Length == 0) return;

                        var mainKsh = new Ksh();
                        mainKsh.Parse(kshFiles[0]);

                        var charts = Ksh.Exporter.GetCharts(folder, mainKsh.Title);
                        if (charts.Count > 0)
                            parsedSongs.Add((folder, mainKsh, charts, null));
                        else
                            parsedSongs.Add((folder, null, null, "No charts found"));
                    }
                    catch (Exception ex)
                    {
                        parsedSongs.Add((folder, null, null, ex.Message));
                    }
                });

                var sorted = parsedSongs.OrderBy(s => s.folder).ToList();
                int parseErrors = sorted.Count(s => s.error != null);
                Console.WriteLine($"  Parsed: {sorted.Count - parseErrors} OK, {parseErrors} errors");

                // Phase 2: Import sequentially (AssetManager is not thread-safe)
                Console.WriteLine("Phase 2: Importing assets...");
                int success = 0;
                int failed = 0;

                foreach (var song in sorted)
                {
                    string folderName = Path.GetFileName(song.folder);

                    if (song.error != null)
                    {
                        Console.WriteLine($"  SKIP {folderName}: {song.error}");
                        failed++;
                        continue;
                    }

                    try
                    {
                        var header = song.mainKsh.ToHeader();
                        header.Id = startId > 0 ? startId++ : AssetManager.GetNextMusicId();
                        header.Version = GameVersion.Nabla;
                        header.GenreId = 16;
                        header.BackgroundId = 0;
                        header.Levels = new Dictionary<Difficulty, VoxLevelHeader>();

                        var exporter = new Ksh.Exporter(song.mainKsh);
                        var audioOptions = AudioImportOptions.WithFormat(AudioFormat.Iidx);
                        exporter.Export(header, song.charts, null, audioOptions);

                        if (exporter.Action != null)
                            exporter.Action.Invoke();

                        AssetManager.Headers.Add(header);

                        string diffs = string.Join(", ", song.charts.Select(c => $"{c.Key}:{c.Value.Header.Level}"));
                        Console.WriteLine($"  OK   {folderName} -> ID {header.Id} [{diffs}]");
                        success++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  FAIL {folderName}: {ex.Message}");
                        if (DebugMode) Console.Error.WriteLine($"       {ex.StackTrace}");
                        failed++;
                    }
                }

                // Save DB once at the end
                AssetManager.Headers.Save(AssetManager.MdbFilename);

                sw.Stop();
                Console.WriteLine();
                Console.WriteLine($"Done! {success} imported, {failed} failed in {sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (DebugMode) Console.Error.WriteLine(ex.StackTrace);
            }
        }

        private static void ConvertFile(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"Converting: {inputPath}");

                var ksh = new Ksh();
                if (DebugMode)
                    ksh.FxLog = new List<string>();

                ksh.Parse(inputPath);

                var vox = new VoxChart();
                vox.Import(ksh);
                vox.Serialize(outputPath);

                Console.WriteLine($"Output:     {outputPath}");

                if (DebugMode && ksh.FxLog != null && ksh.FxLog.Count > 0)
                {
                    string logPath = Path.ChangeExtension(outputPath, ".fxlog.txt");
                    File.WriteAllLines(logPath, ksh.FxLog);
                    Console.WriteLine($"FX Log:     {logPath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error converting '{inputPath}': {ex.Message}");
            }
        }

        private static void ConvertDirectory(string inputDir, string outputDir)
        {
            var files = Directory.GetFiles(inputDir, "*.ksh", SearchOption.AllDirectories);

            if (files.Length == 0)
            {
                Console.WriteLine("No .ksh files found.");
                return;
            }

            Console.WriteLine($"Found {files.Length} .ksh file(s).");

            int success = 0;
            int failed = 0;

            foreach (var file in files)
            {
                string relativePath = file.Substring(inputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                                          .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string outputPath = Path.Combine(outputDir, Path.ChangeExtension(relativePath, ".vox"));

                string dir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                try
                {
                    Console.WriteLine($"Converting: {relativePath}");

                    var ksh = new Ksh();
                    ksh.Parse(file);

                    var vox = new VoxChart();
                    vox.Import(ksh);
                    vox.Serialize(outputPath);

                    success++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Error: {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine($"Done. {success} converted, {failed} failed.");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("VoxCharger - KSH to VOX Converter");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  VoxCharger.exe <input.ksh> [output.vox]          Convert a single file");
            Console.WriteLine("  VoxCharger.exe <input_dir> [output_dir]          Convert all .ksh files in a directory");
            Console.WriteLine("  VoxCharger.exe --full-import <input.ksh> ...     Full import into game");
            Console.WriteLine("  VoxCharger.exe                                   Launch the GUI");
            Console.WriteLine();
            Console.WriteLine("Full Import Options:");
            Console.WriteLine("  --game-path <path>     Game root directory (required)");
            Console.WriteLine("  --mix <name>           Mix name under data_mods/ (required)");
            Console.WriteLine("  --music-id <id>        Music ID to assign (optional, auto-assigns if omitted)");
            Console.WriteLine("  --music-code <code>    ASCII code name (optional, derived from title if omitted)");
            Console.WriteLine();
            Console.WriteLine("Bulk Import:");
            Console.WriteLine("  VoxCharger.exe --bulk-import <dir> --game-path <path> --mix <name> [--start-id <id>]");
            Console.WriteLine("  Imports all song folders in <dir>. Each subfolder should contain .ksh files.");
            Console.WriteLine("  Parsing is parallelized for speed.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -h, --help    Show this help message");
            Console.WriteLine("  --debug       Enable debug logging");
        }
    }
}
