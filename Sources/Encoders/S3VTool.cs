using System;
using System.Diagnostics;
using System.IO;

namespace VoxCharger
{
    public static class S3VTool
    {
        public static string ConverterFileName { get; set; } = "ffmpeg.exe";

        public static void Convert(string inputFileName, string outputFileName, AudioImportOptions opt = null)
        {
            opt = opt ?? AudioImportOptions.Default;
            if (!File.Exists(ConverterFileName))
                throw new FileNotFoundException($"{ConverterFileName} not found", ConverterFileName);

            // Pre-normalize the full track to a temp WAV so every chart lands at
            // a consistent loudness; preview trim/fade below then operates on the
            // already-equalized audio.
            string source   = inputFileName;
            string tempNorm = null;
            try
            {
                if (opt.NormalizeLoudness)
                {
                    LoudnessNormalizer.FfmpegFileName = ConverterFileName;
                    tempNorm = LoudnessNormalizer.Normalize(inputFileName, opt.TargetLufs, opt.TargetTruePeak);
                    source   = tempNorm;
                }

                // Official .s3v files use WMA Pro 10 (wmapro) in ASF container.
                // ffmpeg cannot encode wmapro, so we use wmav2 as the closest available codec.
                string previewArgs = "";
                if (opt.IsPreview)
                    previewArgs = $"-ss {opt.PreviewOffset / 60:00}:{opt.PreviewOffset % 60:00} -t 10 -af afade=t=in:st=0:d=1,afade=t=out:st=9:d=1";
                string args = $"-y -i \"{source}\" {previewArgs} -c:a wmav2 -b:a 384k -ac 2 -ar 44100 -f asf \"{outputFileName}\"";

                var info = new ProcessStartInfo()
                {
                    FileName               = ConverterFileName,
                    Arguments              = args,
                    WorkingDirectory       = Environment.CurrentDirectory,
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                using (var process = Process.Start(info))
                {
                    // Drain stdout and stderr CONCURRENTLY. The previous
                    // ReadToEnd-then-ReadToEnd order is sequential: if ffmpeg
                    // writes enough to stderr to fill its pipe buffer (~4-32
                    // KB) before the parent starts draining, the child blocks
                    // on the stderr write and never exits, so stdout never
                    // EOFs and the parent hangs forever. ffmpeg dumps progress
                    // and stats to stderr, so this hits basically every real
                    // audio file under parallel imports.
                    var outTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
                    var errTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());
                    process.WaitForExit();
                    string stdOut = outTask.Result;
                    string stdErr = errTask.Result;

                    if (process.ExitCode != 0)
                    {
                        string output = (stdOut + ' ' + stdErr).Trim();
                        if (string.IsNullOrEmpty(output))
                            output = "Unknown error.";

                        throw new ApplicationException($"{Path.GetFileName(ConverterFileName)} execution failed.\n({process.ExitCode}): {output}");
                    }
                }
            }
            finally
            {
                if (tempNorm != null && File.Exists(tempNorm))
                {
                    try { File.Delete(tempNorm); }
                    catch { /* best-effort cleanup */ }
                }
            }
        }
    }
}
