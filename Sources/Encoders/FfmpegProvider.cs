using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VoxCharger
{
    // Finds ffmpeg.exe, and downloads a pinned build into %LocalAppData%\VoxCharger when there is none.
    //
    // Lookup order: next to VoxCharger.exe, the downloaded copy, PATH.
    // The download takes only ffmpeg.exe out of the release zip (HTTP range requests, about 37 MB
    // instead of 115 MB) and checks it against a pinned SHA-256 before using it.
    public static class FfmpegProvider
    {
        public const string Version     = "9.0.2";
        private const string ZipUrl      = "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip";
        private const string EntrySuffix = "/bin/ffmpeg.exe";
        private const string ExeSha256   = "3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec";
        public const string SourcePage  = "https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2";
        public const long DownloadSize  = 37386196;

        private static readonly object _lock = new object();
        private static string _path;

        public static string DownloadedPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxCharger", "ffmpeg", Version, "ffmpeg.exe");

        // ffmpeg.exe if one is available without downloading, otherwise null
        public static string Find()
        {
            lock (_lock)
            {
                if (_path != null && File.Exists(_path))
                    return _path;

                foreach (string candidate in Candidates())
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        return _path = candidate;
                }

                return null;
            }
        }

        private static System.Collections.Generic.IEnumerable<string> Candidates()
        {
            yield return SafeCombine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            yield return DownloadedPath;

            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(dir))
                    yield return SafeCombine(dir.Trim(), "ffmpeg.exe");
            }
        }

        private static string SafeCombine(string dir, string name)
        {
            try { return Path.Combine(dir, name); }
            catch (ArgumentException) { return null; }
        }

        // ffmpeg.exe, downloading it first when needed. Progress goes to `progress` (0-100, text);
        // used by the command line and as the fallback for code paths without a window.
        public static string Require(Action<float, string> progress = null)
        {
            lock (_lock)
            {
                string found = Find();
                if (found != null)
                    return found;

                Download(progress ?? ((p, s) => Console.WriteLine($"[ffmpeg] {s}")));
                return _path = DownloadedPath;
            }
        }

        // For the window: asks before downloading and shows the progress. False when the user declines
        // or the download fails (the error has been shown).
        public static bool EnsureAvailable(IWin32Window owner)
        {
            if (Find() != null)
                return true;

            var answer = MessageBox.Show(
                owner,
                $"VoxCharger converts audio with FFmpeg, which is not installed.\n\n" +
                $"Download FFmpeg {Version} now? About {DownloadSize / 1000000} MB, once, into\n{Path.GetDirectoryName(DownloadedPath)}\n\n" +
                $"Source: {SourcePage}",
                "FFmpeg",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (answer != DialogResult.Yes)
                return false;

            Exception error = null;
            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog => new System.Threading.Thread(() =>
                {
                    try
                    {
                        Require((p, s) => { dialog.SetProgress(p); dialog.SetStatus(s); });
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        dialog.Complete();
                    }
                }).Start());
                loader.ShowDialog(owner);
            }

            if (error == null && Find() != null)
                return true;

            MessageBox.Show(
                owner,
                $"FFmpeg could not be downloaded:\n{error?.Message}\n\nYou can also place ffmpeg.exe next to VoxCharger.exe.",
                "FFmpeg",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return false;
        }

        #region --- Download ---
        private static void Download(Action<float, string> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            string target = DownloadedPath;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string partial = target + ".part";

            try
            {
                progress(0, $"Downloading FFmpeg {Version}...");
                bool partialOk;
                try
                {
                    partialOk = TryDownloadEntry(partial, progress);
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    // The server answered, but not to range requests
                    partialOk = false;
                }

                if (!partialOk)
                    DownloadWholeZip(partial, progress);

                progress(100, "Checking FFmpeg...");
                string hash = Sha256(partial);
                if (!string.Equals(hash, ExeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The downloaded ffmpeg.exe does not match the expected checksum ({hash}).");

                if (File.Exists(target))
                    File.Delete(target);
                File.Move(partial, target);
            }
            finally
            {
                if (File.Exists(partial))
                {
                    try { File.Delete(partial); } catch (IOException) { }
                }
            }
        }

        // Reads the zip's central directory from its end and inflates only ffmpeg.exe.
        // False when the server ignores range requests.
        private static bool TryDownloadEntry(string output, Action<float, string> progress)
        {
            // The download CDN rejects suffix ranges ("bytes=-N"), so the size comes from a one-byte range first
            long total;
            using (var response = Range(0, 0, out total))
            {
                if (response == null || total <= 0)
                    return false;
            }

            byte[] tail;
            using (var response = Range(Math.Max(0, total - 65536), total - 1, out _))
                tail = ReadAll(response.GetResponseStream());

            long tailStart = total - tail.Length;
            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50) { eocd = i; break; }
            }
            if (eocd < 0)
                throw new InvalidDataException("FFmpeg archive: end of central directory not found");

            long cdSize   = BitConverter.ToUInt32(tail, eocd + 12);
            long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
            int entries   = BitConverter.ToUInt16(tail, eocd + 10);

            byte[] cd;
            if (cdOffset >= tailStart)
            {
                cd = new byte[cdSize];
                Array.Copy(tail, cdOffset - tailStart, cd, 0, cdSize);
            }
            else
            {
                using (var response = Range(cdOffset, cdOffset + cdSize - 1, out _))
                    cd = ReadAll(response.GetResponseStream());
            }

            long localOffset = -1, compressed = 0;
            int method = 0;
            for (int pos = 0, n = 0; n < entries && pos + 46 <= cd.Length; n++)
            {
                if (BitConverter.ToUInt32(cd, pos) != 0x02014b50)
                    break;

                method        = BitConverter.ToUInt16(cd, pos + 10);
                compressed    = BitConverter.ToUInt32(cd, pos + 20);
                int nameLen   = BitConverter.ToUInt16(cd, pos + 28);
                int extraLen  = BitConverter.ToUInt16(cd, pos + 30);
                int comment   = BitConverter.ToUInt16(cd, pos + 32);
                long offset   = BitConverter.ToUInt32(cd, pos + 42);
                string name   = Encoding.UTF8.GetString(cd, pos + 46, nameLen);
                if (name.EndsWith(EntrySuffix, StringComparison.OrdinalIgnoreCase))
                {
                    localOffset = offset;
                    break;
                }

                pos += 46 + nameLen + extraLen + comment;
            }

            if (localOffset < 0)
                throw new InvalidDataException("FFmpeg archive: ffmpeg.exe not found");

            byte[] local;
            using (var response = Range(localOffset, localOffset + 29, out _))
                local = ReadAll(response.GetResponseStream());

            long dataStart = localOffset + 30 + BitConverter.ToUInt16(local, 26) + BitConverter.ToUInt16(local, 28);
            using (var response = Range(dataStart, dataStart + compressed - 1, out _))
            using (var counted = new ProgressStream(response.GetResponseStream(), compressed, progress))
            using (var file = File.Create(output))
            {
                if (method == 8)
                {
                    using (var inflate = new DeflateStream(counted, CompressionMode.Decompress))
                        inflate.CopyTo(file);
                }
                else if (method == 0)
                    counted.CopyTo(file);
                else
                    throw new InvalidDataException($"FFmpeg archive: unsupported compression {method}");
            }

            return true;
        }

        private static void DownloadWholeZip(string output, Action<float, string> progress)
        {
            string zip = output + ".zip";
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(ZipUrl);
                request.UserAgent = "VoxCharger";
                using (var response = request.GetResponse())
                using (var counted = new ProgressStream(response.GetResponseStream(), response.ContentLength, progress))
                using (var file = File.Create(zip))
                    counted.CopyTo(file);

                using (var archive = ZipFile.OpenRead(zip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (("/" + entry.FullName.Replace('\\', '/')).EndsWith(EntrySuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.ExtractToFile(output, true);
                            return;
                        }
                    }
                }

                throw new InvalidDataException("FFmpeg archive: ffmpeg.exe not found");
            }
            finally
            {
                if (File.Exists(zip))
                {
                    try { File.Delete(zip); } catch (IOException) { }
                }
            }
        }

        // Range request for bytes from..to. Null when the server answers without a range.
        private static WebResponse Range(long from, long to, out long total)
        {
            var request = (HttpWebRequest)WebRequest.Create(ZipUrl);
            request.UserAgent = "VoxCharger";
            request.AddRange(from, to);

            var response = (HttpWebResponse)request.GetResponse();
            total = -1;
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                response.Close();
                return null;
            }

            string range = response.Headers["Content-Range"];
            int slash = range?.LastIndexOf('/') ?? -1;
            if (slash > 0)
                long.TryParse(range.Substring(slash + 1), out total);

            return response;
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (stream)
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        // Reports how much of a download has been read
        private class ProgressStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _length;
            private readonly Action<float, string> _progress;
            private long _read;
            private int _lastPercent = -1;

            public ProgressStream(Stream inner, long length, Action<float, string> progress)
            {
                _inner    = inner;
                _length   = length;
                _progress = progress;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                _read += n;
                if (_length > 0)
                {
                    int percent = (int)(_read * 100 / _length);
                    if (percent != _lastPercent)
                    {
                        _lastPercent = percent;
                        _progress(percent, $"Downloading FFmpeg {Version}: {_read / 1000000} of {_length / 1000000} MB");
                    }
                }
                return n;
            }

            public override bool CanRead  => true;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => _length;
            public override long Position { get => _read; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
        #endregion
    }
}
