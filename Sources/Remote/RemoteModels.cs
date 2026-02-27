using System.Collections.Generic;

namespace VoxCharger
{
    public class RemoteMix
    {
        public int id { get; set; }
        public string name { get; set; }
        public int music_id_start { get; set; }
        public string created_at { get; set; }
        public int song_count { get; set; }

        public override string ToString()
        {
            return $"{name} ({song_count} songs)";
        }
    }

    public class RemoteSong
    {
        public int id { get; set; }
        public int music_id { get; set; }
        public string url { get; set; }
        public string title { get; set; }
        public string artist { get; set; }

        public override string ToString()
        {
            string display = title ?? "Unknown";
            if (!string.IsNullOrEmpty(artist))
                display += $" - {artist}";
            display += $" [ID:{music_id}]";
            return display;
        }
    }

    public class RemoteMixDetail
    {
        public int id { get; set; }
        public string name { get; set; }
        public int music_id_start { get; set; }
        public string created_at { get; set; }
        public List<RemoteSong> songs { get; set; } = new List<RemoteSong>();
    }
}
