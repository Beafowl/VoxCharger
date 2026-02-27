using System;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace VoxCharger
{
    public class KsmDownloader : IDisposable
    {
        private readonly WebClient _http;

        public KsmDownloader()
        {
            _http = new WebClient();
            _http.Headers[HttpRequestHeader.UserAgent] = "VoxCharger";
        }

        public string DownloadAndExtract(string ksmUrl)
        {
            string downloadUrl = NormalizeDownloadUrl(ksmUrl);

            byte[] zipData = _http.DownloadData(downloadUrl);
            string tempDir = Path.Combine(Path.GetTempPath(), "VoxCharger_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "chart.zip");
            File.WriteAllBytes(zipPath, zipData);

            ZipFile.ExtractToDirectory(zipPath, tempDir);
            File.Delete(zipPath);

            return tempDir;
        }

        public static string FindKshFile(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*.ksh", SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        public static string FindKshDirectory(string directory)
        {
            string kshFile = FindKshFile(directory);
            return kshFile != null ? Path.GetDirectoryName(kshFile) : null;
        }

        public static void Cleanup(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        private static string NormalizeDownloadUrl(string url)
        {
            url = url.TrimEnd('/');
            if (!url.EndsWith("/download"))
                url += "/download";
            return url;
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
