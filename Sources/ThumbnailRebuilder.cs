using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace VoxCharger
{
    // Rebuilds the song select thumbnails of the loaded mix from the jackets in the music
    // folders: 128x128 like the stock archives, placed in the archive folder chosen by music id
    // (see AssetManager.ThumbnailArchive). Thumbnails left in other archive folders are removed
    // and the repair manifest is updated. Used to migrate mixes whose thumbnails were all in
    // s_jacket00_ifs, which made ifs_layeredfs build one huge texture the game rejected.
    public static class ThumbnailRebuilder
    {
        public class Result
        {
            public int          Rebuilt;
            public int          Missing;   // charts with no jacket to rebuild from
            public int          Removed;   // stale thumbnails deleted from other archive folders
            public List<string> Errors = new List<string>();
        }

        public static Result Run(AsphyxiaImporter.Progress progress = null)
        {
            progress = progress ?? new AsphyxiaImporter.Progress();
            var result  = new Result();
            var headers = new List<VoxHeader>(AssetManager.Headers);

            ManifestRecorder.BeginBatch();
            try
            {
                int done = 0;
                foreach (var header in headers)
                {
                    var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var level in header.Levels.Values)
                    {
                        string target = $"{AssetManager.GetThumbnailJacketPath(header, level.Difficulty)}.png";
                        string source = FindSource(header, level.Difficulty, target);
                        if (source == null)
                        {
                            result.Missing++;
                            continue;
                        }

                        try
                        {
                            using (var image = Image.FromFile(source))
                                AssetManager.SaveJacket(image, AssetManager.ThumbnailSize, target);

                            keep.Add(Path.GetFullPath(target));
                            result.Rebuilt++;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"{header.CodeName} {level.Difficulty}: {ex.Message}");
                        }
                    }

                    foreach (string file in AssetManager.FindThumbnails(header.Id))
                    {
                        if (keep.Contains(Path.GetFullPath(file)))
                            continue;

                        try { File.Delete(file); result.Removed++; }
                        catch (Exception ex) { result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
                    }

                    try
                    {
                        ManifestRecorder.UpdateThumbnails(header);
                    }
                    catch (Exception)
                    {
                        // Manifest is best effort, like everywhere else
                    }

                    done++;
                    progress.SetPercent(done / (float)headers.Count * 100f);
                    progress.SetStatus($"Thumbnails {done}/{headers.Count}: {header.Title}");
                }
            }
            finally
            {
                ManifestRecorder.EndBatch();
            }

            return result;
        }

        // Best jacket to scale the thumbnail from: the music folder sizes first, then an
        // existing thumbnail (of this difficulty) wherever an older import left it
        private static string FindSource(VoxHeader header, Difficulty difficulty, string target)
        {
            string jacket = AssetManager.GetJacketPath(header, difficulty);
            foreach (string candidate in new[] { $"{jacket}.png", $"{jacket}_b.png", $"{jacket}_s.png" })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            string name = Path.GetFileName(target);
            return AssetManager.FindThumbnails(header.Id)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
