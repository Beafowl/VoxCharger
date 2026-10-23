using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VoxCharger
{
    // Encodes .s3v (WMA in an ASF container) with ffmpeg in one pass: normalization, offset, lead-in,
    // tail fade and the preview cut are all part of the same filter chain.
    public static class S3VTool
    {
        // Official .s3v files use WMA Pro 10; ffmpeg cannot encode wmapro, wmav2 is the closest codec
        private const string Encode = "-c:a wmav2 -b:a 384k -ac 2 -ar 44100 -f asf";

        public static void Convert(string inputFileName, string outputFileName, AudioImportOptions opt = null)
        {
            opt = opt ?? AudioImportOptions.Default;
            if (!File.Exists(inputFileName))
                throw new FileNotFoundException($"{inputFileName} not found", inputFileName);

            // Preview: 10 seconds from PreviewOffset with a one second fade in and out. The cut is part of
            // the filter chain, so the fades are timed relative to the clip.
            List<string> preview = null;
            if (opt.IsPreview)
            {
                preview = new List<string>
                {
                    string.Format(CultureInfo.InvariantCulture, "atrim=start={0}:duration=10", Math.Max(0, opt.PreviewOffset)),
                    "asetpts=PTS-STARTPTS",
                    "afade=t=in:st=0:d=1",
                    "afade=t=out:st=9:d=1",
                };
            }

            string args;
            if (opt.NormalizeLoudness)
            {
                args = LoudnessNormalizer.BuildArguments(
                    inputFileName,
                    opt.TargetLufs,
                    opt.TargetTruePeak,
                    opt.MusicOffsetMs,
                    opt.LeadInPadMs,
                    opt.TruncateAtMs,
                    opt.TailFadeOutMs,
                    preview);
            }
            else
            {
                string filter = preview != null ? $" -af \"{string.Join(",", preview)}\"" : string.Empty;
                args = $"-hide_banner -y -i \"{inputFileName}\"{filter}";
            }

            LoudnessNormalizer.Run($"{args} {Encode} \"{outputFileName}\"");
        }
    }
}
