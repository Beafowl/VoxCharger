using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace VoxCharger
{
    // EBU R128 loudness normalization with ffmpeg.
    //
    // One measurement per source file (ffmpeg's `ebur128` scanner: integrated loudness and true peak),
    // cached, so the main audio and the preview of a song share it. The gain, the KSH music offset,
    // the lead-in and the tail fade are then applied in a single ffmpeg pass.
    public static class LoudnessNormalizer
    {
        private static string _ffmpegFileName;

        // ffmpeg.exe; downloaded on first use when there is none (see FfmpegProvider)
        public static string FfmpegFileName
        {
            get { return _ffmpegFileName ?? (_ffmpegFileName = FfmpegProvider.Require()); }
            set { _ffmpegFileName = value; }
        }

        private struct LoudnessStats
        {
            public double IntegratedLufs;
            public double TruePeakDb;
        }

        // Keyed by path, size and write time, so a changed file is measured again
        private static readonly ConcurrentDictionary<string, Lazy<LoudnessStats>> _measurements =
            new ConcurrentDictionary<string, Lazy<LoudnessStats>>(StringComparer.OrdinalIgnoreCase);

        // Writes the normalized, offset, padded and faded audio to a temporary 44.1 kHz 16-bit stereo
        // WAV and returns its path; the caller deletes it.
        public static string Normalize(
            string inputFileName,
            double targetLufs = -14.0,
            double targetTruePeak = -1.5,
            int musicOffsetMs = 0,
            int leadInPadMs = 0,
            int truncateAtMs = 0,
            int tailFadeOutMs = 0)
        {
            string output = Path.Combine(Path.GetTempPath(), $"vc_norm_{Guid.NewGuid():N}.wav");
            string args = BuildArguments(inputFileName, targetLufs, targetTruePeak, musicOffsetMs, leadInPadMs, truncateAtMs, tailFadeOutMs, null)
                        + $" -ar 44100 -ac 2 -c:a pcm_s16le \"{output}\"";
            Run(args);
            return output;
        }

        // Input options, filter chain and cut of one normalizing ffmpeg run, without codec and output file.
        // `extraFilters` are appended to the chain (e.g. a preview cut).
        public static string BuildArguments(
            string inputFileName,
            double targetLufs,
            double targetTruePeak,
            int musicOffsetMs,
            int leadInPadMs,
            int truncateAtMs,
            int tailFadeOutMs,
            IEnumerable<string> extraFilters)
        {
            if (!File.Exists(inputFileName))
                throw new FileNotFoundException($"{inputFileName} not found", inputFileName);

            var stats = Measure(inputFileName);

            // A flat gain instead of loudnorm's own pass: its true-peak limiter ducks loud sections and
            // the gain audibly ramps back up. The gain is capped so the true peak stays below the
            // target; songs with very loud peaks end up a little below the target loudness.
            double gainDb = Math.Min(targetLufs - stats.IntegratedLufs, targetTruePeak - stats.TruePeakDb);

            var filters = new List<string> { string.Format(CultureInfo.InvariantCulture, "volume={0:0.##}dB", gainDb) };
            string input = string.Empty;

            // KSH music offset: a positive o= trims the audio front, a negative one pads it with silence.
            // The lead-in pad adds more silence in front.
            if (musicOffsetMs > 0)
                input = string.Format(CultureInfo.InvariantCulture, "-ss {0:0.###} ", musicOffsetMs / 1000.0);

            int delayMs = (musicOffsetMs < 0 ? -musicOffsetMs : 0) + Math.Max(0, leadInPadMs);
            if (delayMs > 0)
                filters.Add(string.Format(CultureInfo.InvariantCulture, "adelay=delays={0}:all=1", delayMs));

            // Fade out so the audio is silent at the cut. The cut itself is ffmpeg's -t output option:
            // atrim after adelay drops the inserted silence
            if (truncateAtMs > 0 && tailFadeOutMs > 0)
            {
                double start = Math.Max(0, truncateAtMs - tailFadeOutMs) / 1000.0;
                filters.Add(string.Format(CultureInfo.InvariantCulture, "afade=t=out:st={0:0.###}:d={1:0.###}", start, tailFadeOutMs / 1000.0));
            }

            if (extraFilters != null)
                filters.AddRange(extraFilters);

            string cut = truncateAtMs > 0 ? string.Format(CultureInfo.InvariantCulture, " -t {0:0.###}", truncateAtMs / 1000.0) : string.Empty;
            return $"-hide_banner -y {input}-i \"{inputFileName}\" -af \"{string.Join(",", filters)}\"{cut}";
        }

        private static LoudnessStats Measure(string inputFileName)
        {
            var info = new FileInfo(inputFileName);
            string key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var lazy = _measurements.GetOrAdd(key, _ => new Lazy<LoudnessStats>(() => Scan(info.FullName)));
            try
            {
                return lazy.Value;
            }
            catch
            {
                _measurements.TryRemove(key, out _);
                throw;
            }
        }

        // ebur128 prints a summary at the end: "I: -3.9 LUFS" and, with peak=true, "Peak: 3.3 dBFS"
        private static LoudnessStats Scan(string inputFileName)
        {
            string stderr = Run($"-hide_banner -nostats -i \"{inputFileName}\" -af \"ebur128=peak=true\" -f null -");
            int summary = stderr.LastIndexOf("Summary:", StringComparison.Ordinal);
            if (summary < 0)
                throw new ApplicationException("ebur128 did not print a summary");

            string text = stderr.Substring(summary);
            return new LoudnessStats
            {
                IntegratedLufs = ParseValue(text, @"\bI:\s*(-?inf|-?\d+(?:\.\d+)?)\s*LUFS", -70.0),
                TruePeakDb     = ParseValue(text, @"\bPeak:\s*(-?inf|-?\d+(?:\.\d+)?)\s*dBFS", -70.0),
            };
        }

        private static double ParseValue(string text, string pattern, double silence)
        {
            var match = Regex.Match(text, pattern);
            if (!match.Success)
                throw new ApplicationException($"ebur128 summary is missing {pattern}");

            string raw = match.Groups[1].Value;
            // Silence measures as -inf (or -70 LUFS, the gate); a finite value keeps the gain sane
            if (raw.EndsWith("inf", StringComparison.OrdinalIgnoreCase))
                return silence;

            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // Runs ffmpeg and returns its stderr
        public static string Run(string args)
        {
            string ffmpeg = FfmpegFileName;
            if (!File.Exists(ffmpeg))
                throw new FileNotFoundException("ffmpeg.exe not found; place it next to VoxCharger.exe or let VoxCharger download it", ffmpeg);

            var info = new ProcessStartInfo
            {
                FileName               = ffmpeg,
                Arguments              = args,
                WorkingDirectory       = Environment.CurrentDirectory,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };

            using var process = Process.Start(info);

            // Drain both streams at once: ffmpeg writes progress and statistics to stderr, and reading
            // one stream after the other deadlocks as soon as the stderr pipe buffer is full
            var outTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
            var errTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());
            process.WaitForExit();
            string stdOut = outTask.Result;
            string stdErr = errTask.Result;

            if (process.ExitCode != 0)
            {
                string msg = (stdOut + ' ' + stdErr).Trim();
                if (msg.Length > 2000)
                    msg = msg.Substring(msg.Length - 2000);
                if (string.IsNullOrEmpty(msg))
                    msg = "Unknown error.";
                throw new ApplicationException($"ffmpeg failed.\n({process.ExitCode}): {msg}");
            }

            return stdErr;
        }
    }
}
