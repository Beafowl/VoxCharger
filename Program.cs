using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
            var argList = new System.Collections.Generic.List<string>(args);
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

        private static void ConvertFile(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"Converting: {inputPath}");

                var ksh = new Ksh();
                if (DebugMode)
                    ksh.FxLog = new System.Collections.Generic.List<string>();

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
            Console.WriteLine("  VoxCharger.exe <input.ksh> [output.vox]   Convert a single file");
            Console.WriteLine("  VoxCharger.exe <input_dir> [output_dir]   Convert all .ksh files in a directory");
            Console.WriteLine("  VoxCharger.exe                            Launch the GUI");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -h, --help    Show this help message");
        }
    }
}
