using System;
using System.Collections.Generic;
using System.IO;

namespace VoxCharger
{
    // Recomputes the radar of every chart in the loaded mix from its .vox file on disk,
    // e.g. after the radar weights changed. The caller saves the music db afterwards.
    public static class RadarRecalculator
    {
        public class Result
        {
            public int          Updated;
            public int          Missing;  // charts without a .vox file
            public List<string> Errors = new List<string>();
        }

        public static Result Run(AsphyxiaImporter.Progress progress = null)
        {
            progress = progress ?? new AsphyxiaImporter.Progress();
            var result  = new Result();
            var headers = new List<VoxHeader>(AssetManager.Headers);

            int done = 0;
            foreach (var header in headers)
            {
                foreach (var level in header.Levels.Values)
                {
                    string path = AssetManager.GetVoxPath(header, level.Difficulty);
                    if (!File.Exists(path))
                    {
                        result.Missing++;
                        continue;
                    }

                    try
                    {
                        var chart = new VoxChart();
                        chart.Parse(path);
                        level.Radar = RadarCalculator.Calculate(chart.Events, level.Level);
                        result.Updated++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{header.CodeName} {level.Difficulty}: {ex.Message}");
                    }
                }

                done++;
                progress.SetPercent(done / (float)headers.Count * 100f);
                progress.SetStatus($"Radar {done}/{headers.Count}: {header.Title}");
            }

            return result;
        }
    }
}
