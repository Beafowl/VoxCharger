using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace VoxCharger
{
    // Wire shape of Asphyxia's nauticaExportList endpoint
    // (plugins/sdvx@asphyxia/handlers/nautica.ts:504). Only the fields
    // actually consumed during import are deserialized; unknown fields are
    // ignored so the list survives schema additions on the Asphyxia side.
    public static class AsphyxiaList
    {
        public class Chart
        {
            public int    difficulty { get; set; }
            public int    level      { get; set; }
            public string effector   { get; set; }
        }

        public class Song
        {
            public string       nauticaId   { get; set; }
            public int          mid         { get; set; }
            public string       title       { get; set; }
            public string       artist      { get; set; }
            public string       jacketUrl   { get; set; }
            public string       downloadUrl { get; set; }
            public List<Chart>  charts      { get; set; }
            public List<string> tags        { get; set; }
            public string       status      { get; set; }
        }

        public class Bundle
        {
            public int        version    { get; set; }
            public long       exportedAt { get; set; }
            public string     exportedBy { get; set; }
            public int        count      { get; set; }
            public List<Song> songs      { get; set; }
        }

        public static Bundle LoadFile(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            return Parse(text);
        }

        public static Bundle Parse(string json)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return ser.Deserialize<Bundle>(json);
        }

        // Charts the importer can actually act on: status `ready`, an
        // assigned music id, and a download URL pointing at a chart zip.
        // Other states (pending / approved without convert / error) carry
        // no playable assets, so they're skipped.
        public static bool IsImportable(Song s)
        {
            if (s == null) return false;
            if (s.mid <= 0) return false;
            if (string.IsNullOrWhiteSpace(s.downloadUrl)) return false;
            if (!string.Equals(s.status, "ready", System.StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
    }
}
