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

            // Official .s3v files use WMA Pro 10 (wmapro) in ASF container.
            // ffmpeg cannot encode wmapro, so we use wmav2 as the closest available codec.
            string previewArgs = "";
            if (opt.IsPreview)
                previewArgs = $"-ss {opt.PreviewOffset / 60:00}:{opt.PreviewOffset % 60:00} -t 10 -af afade=t=in:st=0:d=1,afade=t=out:st=9:d=1";
            string args = $"-y -i \"{inputFileName}\" {previewArgs} -c:a wmav2 -b:a 384k -ac 2 -ar 44100 -f asf \"{outputFileName}\"";

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
                // Read streams before WaitForExit to avoid deadlock when output buffer fills
                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string output = (stdOut + ' ' + stdErr).Trim();
                    if (string.IsNullOrEmpty(output))
                        output = "Unknown error.";

                    throw new ApplicationException($"{Path.GetFileName(ConverterFileName)} execution failed.\n({process.ExitCode}): {output}");
                }
            }
        }
    }
}