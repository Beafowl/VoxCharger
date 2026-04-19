using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace VoxCharger
{
    // Two-pass EBU R128 loudness normalization via ffmpeg's `loudnorm` filter.
    // Pass 1 measures the input, pass 2 applies with linear=true so transients
    // are preserved. Produces a 44.1 kHz / 16-bit / stereo PCM WAV at a
    // temporary path; the caller owns deletion.
    public static class LoudnessNormalizer
    {
        // Resolved lazily so callers can still override FfmpegFileName before
        // the first Normalize() call, but by default we look in VoxCharger's
        // own folder first, then PATH. When VoxCharger is spawned from another
        // process (e.g. asphyxia), cwd is the caller's working dir — NOT this
        // assembly's folder and NOT PATH-aware without an explicit lookup.
        // Setting the default to bare "ffmpeg.exe" made File.Exists succeed
        // only when cwd happened to contain the binary.
        private static string _ffmpegFileName;
        public static string FfmpegFileName
        {
            get { return _ffmpegFileName ?? (_ffmpegFileName = ResolveFfmpegPath()); }
            set { _ffmpegFileName = value; }
        }

        private static string ResolveFfmpegPath()
        {
            const string name = "ffmpeg.exe";

            // 1. Alongside the VoxCharger binary (Program.cs's AppDomain base).
            try
            {
                string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                if (File.Exists(beside)) return beside;
            }
            catch { /* ignored */ }

            // 2. PATH lookup — Process.Start with UseShellExecute=false does
            // NOT walk PATH on its own, so do it manually.
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* bad PATH entry — skip */ }
            }

            // Nothing resolved; return the bare name so the File.Exists check
            // in Normalize() throws with a clear "ffmpeg.exe not found" message.
            return name;
        }

        public static string Normalize(string inputFileName, double targetLufs = -14.0, double targetTruePeak = -1.5, int musicOffsetMs = 0)
        {
            if (!File.Exists(FfmpegFileName))
                throw new FileNotFoundException($"{FfmpegFileName} not found — install ffmpeg and place it next to VoxCharger.exe or add its folder to PATH", FfmpegFileName);
            if (!File.Exists(inputFileName))
                throw new FileNotFoundException($"{inputFileName} not found", inputFileName);

            var stats = Measure(inputFileName, targetLufs, targetTruePeak);

            // We deliberately do NOT apply the loudnorm filter in pass 2. Even
            // with linear=true, loudnorm runs an internal true-peak limiter
            // with a release time; on dynamic songs the limiter ducks loud
            // sections and you can hear the gain ramp back up over a few
            // seconds — described by users as "sudden quiet patches that
            // gradually return to normal volume."
            //
            // Instead, compute a single dB gain from the measurement and apply
            // it with ffmpeg's flat `volume` filter. The headroom clamp below
            // caps the gain so the output's true peak never exceeds
            // targetTruePeak, which keeps the result clip-free without the
            // limiter artifact. Songs whose natural peaks are very loud get
            // less LUFS gain (i.e. they end up slightly quieter than target)
            // but the playback stays consistent with itself throughout.
            double gainForLufs = targetLufs - stats.InputI;
            double maxHeadroom = targetTruePeak - stats.InputTp;
            double gainDb      = Math.Min(gainForLufs, maxHeadroom);

            string output = Path.Combine(Path.GetTempPath(), $"vc_loudnorm_{Guid.NewGuid():N}.wav");
            string volumeFilter = string.Format(
                CultureInfo.InvariantCulture,
                "volume={0:0.##}dB", gainDb
            );
            string args = $"-hide_banner -y -i \"{inputFileName}\" -af \"{volumeFilter}\" -ar 44100 -ac 2 -c:a pcm_s16le \"{output}\"";
            Run(args);

            // Apply KSH music offset to the audio, not to the chart. Keeps
            // every chart event exactly on its KSH tick position — notes
            // land on the beat grid in-game. The sign convention here was
            // confirmed empirically: on Roar of Chronos (o=1400, 175 BPM),
            // shifting chart positions LATER by 196 ticks produced correct
            // alignment — which is equivalent to moving audio EARLIER by
            // the same amount, i.e. trimming 1400 ms off the audio start.
            //   Positive o= -> trim N ms from the audio front
            //   Negative o= -> pad N ms of silence to the audio front
            if (musicOffsetMs != 0)
            {
                string shifted = Path.Combine(Path.GetTempPath(), $"vc_offset_{Guid.NewGuid():N}.wav");
                string shiftArgs;
                if (musicOffsetMs > 0)
                {
                    // Positive offset: seek-trim from the start. -ss before
                    // -i uses fast keyframe seek; since WAV is PCM, every
                    // sample is a keyframe and the seek is sample-accurate.
                    double trimSec = musicOffsetMs / 1000.0;
                    shiftArgs = string.Format(
                        CultureInfo.InvariantCulture,
                        "-hide_banner -y -ss {0:0.####} -i \"{1}\" -ar 44100 -ac 2 -c:a pcm_s16le \"{2}\"",
                        trimSec, output, shifted
                    );
                }
                else
                {
                    // Negative offset: pad front with silence. adelay takes
                    // ms per channel — two values for stereo.
                    int padMs = Math.Abs(musicOffsetMs);
                    shiftArgs = $"-hide_banner -y -i \"{output}\" -af \"adelay={padMs}|{padMs}\" -ar 44100 -ac 2 -c:a pcm_s16le \"{shifted}\"";
                }
                Run(shiftArgs);
                try { File.Delete(output); } catch { }
                output = shifted;
            }

            return output;
        }

        private struct LoudnormStats
        {
            public double InputI, InputTp, InputLra, InputThresh, TargetOffset;
        }

        private static LoudnormStats Measure(string inputFileName, double targetLufs, double targetTruePeak)
        {
            string filter = string.Format(
                CultureInfo.InvariantCulture,
                "loudnorm=I={0:0.##}:TP={1:0.##}:LRA=11:print_format=json",
                targetLufs, targetTruePeak
            );
            string args   = $"-hide_banner -i \"{inputFileName}\" -af \"{filter}\" -f null -";
            string stderr = Run(args);

            int start = stderr.LastIndexOf('{');
            int end   = stderr.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new ApplicationException("loudnorm measurement did not emit JSON");

            string json = stderr.Substring(start, end - start + 1);
            return new LoudnormStats
            {
                InputI       = ParseField(json, "input_i"),
                InputTp      = ParseField(json, "input_tp"),
                InputLra     = ParseField(json, "input_lra"),
                InputThresh  = ParseField(json, "input_thresh"),
                TargetOffset = ParseField(json, "target_offset"),
            };
        }

        private static double ParseField(string json, string name)
        {
            var match = Regex.Match(json, $"\"{name}\"\\s*:\\s*\"(-?\\d+(?:\\.\\d+)?|-?inf)\"");
            if (!match.Success)
                throw new ApplicationException($"loudnorm JSON missing field: {name}");

            string raw = match.Groups[1].Value;
            // loudnorm can emit "-inf" for silence / below-threshold measurements;
            // map to a very negative finite value so pass 2 still runs.
            if (raw.EndsWith("inf", StringComparison.OrdinalIgnoreCase))
                return raw.StartsWith("-") ? -70.0 : 0.0;

            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Run(string args)
        {
            var info = new ProcessStartInfo
            {
                FileName               = FfmpegFileName,
                Arguments              = args,
                WorkingDirectory       = Environment.CurrentDirectory,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };

            using var process = Process.Start(info);

            // Drain stdout and stderr concurrently. The previous
            // ReadToEnd-then-ReadToEnd pattern deadlocks whenever ffmpeg fills
            // the stderr pipe buffer (~4-32 KB) before the parent starts
            // reading it — which is basically always for a real audio file,
            // because ffmpeg dumps progress / loudnorm stats to stderr while
            // stdout stays near-empty. Under Parallel.ForEach the deadlock was
            // hitting every bulk run. Read both streams in parallel so neither
            // can block the child.
            var outTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
            var errTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());
            process.WaitForExit();
            string stdOut = outTask.Result;
            string stdErr = errTask.Result;

            if (process.ExitCode != 0)
            {
                string msg = (stdOut + ' ' + stdErr).Trim();
                if (string.IsNullOrEmpty(msg))
                    msg = "Unknown error.";
                throw new ApplicationException($"ffmpeg loudnorm failed.\n({process.ExitCode}): {msg}");
            }
            return stdErr;
        }
    }
}
