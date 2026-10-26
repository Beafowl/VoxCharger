using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VoxCharger
{
    // Runs the game's own XML reader over a music db: avs2-core.dll's "query memsize" export, the
    // first thing the game calls on /data/others/music_db.xml. When it fails the game gives up on the
    // song list without a log line, so this is the check that tells whether the game will see the songs.
    // Needs the game's modules\avs2-core.dll, which is 64-bit like VoxCharger.
    internal static class AvsCheck
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate UIntPtr ReadCallback(uint context, IntPtr buffer, UIntPtr count);

        // property_read_query_memsize(read, context, 0, 0, 0): the parsed size, or an error code
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate long QueryMemSize(ReadCallback read, uint context, IntPtr a, IntPtr b, IntPtr c);

        // The game imports it by this ordinal (name XCgsqzn00000b1 in AVS 2.17)
        private const int QueryOrdinal = 178;

        private static readonly object _lock = new object();
        private static string _loadedPath;
        private static QueryMemSize _query;

        public static string DllPath => string.IsNullOrEmpty(AssetManager.GamePath)
            ? null
            : Path.Combine(AssetManager.GamePath, "modules", "avs2-core.dll");

        public static bool Available => Load() != null;

        private static QueryMemSize Load()
        {
            lock (_lock)
            {
                string path = DllPath;
                if (path == _loadedPath)
                    return _query;

                _loadedPath = path;
                _query      = null;
                try
                {
                    if (path == null || !File.Exists(path) || IntPtr.Size != 8)
                        return null;

                    IntPtr module = LoadLibraryW(path);
                    if (module == IntPtr.Zero)
                        return null;

                    IntPtr proc = GetProcAddress(module, (IntPtr)QueryOrdinal);
                    if (proc != IntPtr.Zero)
                        _query = Marshal.GetDelegateForFunctionPointer<QueryMemSize>(proc);
                }
                catch (Exception)
                {
                    _query = null;
                }

                return _query;
            }
        }

        // Null when the game's reader accepts the file or the check can't run (no avs2-core.dll);
        // otherwise the error code, as the game would get it
        public static string Check(string xmlPath)
        {
            var query = Load();
            if (query == null || !File.Exists(xmlPath))
                return null;

            byte[] data  = File.ReadAllBytes(xmlPath);
            int position = 0;
            ReadCallback read = (context, buffer, count) =>
            {
                int n = (int)Math.Min(count.ToUInt64(), (ulong)(data.Length - position));
                if (n > 0)
                    Marshal.Copy(data, position, buffer, n);
                position += n;
                return (UIntPtr)(uint)n;
            };

            long result;
            lock (_lock)
                result = query(read, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(read);

            int code = unchecked((int)result);
            return code < 0 ? $"0x{code:X8}" : null;
        }
    }
}
