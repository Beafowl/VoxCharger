using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Xml;
using System.Globalization;

namespace VoxCharger
{
    public class MusicDb : IEnumerable<VoxHeader>
    {
        private const string DummyYomigana = "ダミー";
        private static readonly Encoding DefaultEncoding = Encoding.GetEncoding("Shift_JIS");

        private Dictionary<int, VoxHeader> _headers;
        private int _max = 0;

        public int Count  => _headers.Count;

        public int LastId => _headers.Values.Max(h => h.Id);

        // When > 0, every song is written with this tag regardless of its own TagId (mix-wide tag)
        public int ForcedTagId { get; set; }

        public MusicDb()
        {
            _headers = new Dictionary<int, VoxHeader>();
        }

        // For some reason, konmai decide to use int32 with thousands value for the BPM, e.g 18000 to represent 180.00 BPM
        // Why? I don't know, probably the usual fuckery. Values that still read 1000 or more were stored scaled twice.
        private static double ParseBpm(string text)
        {
            if (!long.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return 0;

            double bpm = value / 100D;
            return bpm >= 1000 ? bpm / 100D : bpm;
        }

        public void Load(string path, bool append = false)
        {
            _headers = append ? _headers : new Dictionary<int, VoxHeader>();
            var doc = new XmlDocument();
            using (var reader = new StreamReader(path, DefaultEncoding))
            {
                // Load via reader to ensure correct encoding is used
                doc.Load(reader);
            }

            foreach (XmlNode music in doc.SelectSingleNode("mdb"))
            {
                int id = int.Parse(music.Attributes["id"].Value);
                if (append && _headers.ContainsKey(id))
                    continue;

                var info = music.SelectSingleNode("info");
                var header = new VoxHeader
                {
                    Id               = id,
                    Title            = info.SelectSingleNode("title_name").InnerText,
                    TitleYomigana    = info.SelectSingleNode("title_yomigana").InnerText,
                    Artist           = info.SelectSingleNode("artist_name").InnerText,
                    ArtistYomigana   = info.SelectSingleNode("artist_yomigana").InnerText,
                    Ascii            = info.SelectSingleNode("ascii").InnerText,
                    DistributionDate = DateTime.ParseExact(info.SelectSingleNode("distribution_date").InnerText, "yyyyMMdd", CultureInfo.InvariantCulture),
                    Volume           = short.Parse(info.SelectSingleNode("volume").InnerText),
                    BackgroundId     = short.Parse(info.SelectSingleNode("bg_no").InnerText),
                    GenreId          = int.Parse(info.SelectSingleNode("genre").InnerText),
                    Version          = (GameVersion)int.Parse(info.SelectSingleNode("version").InnerText),
                    InfVersion       = (InfiniteVersion)int.Parse(info.SelectSingleNode("inf_ver").InnerText),
                };

                var tag = music.SelectSingleNode("tag");
                if (tag?.Attributes["id"] != null && int.TryParse(tag.Attributes["id"].Value, out int tagId))
                    header.TagId = tagId;

                header.BpmMax = ParseBpm(info.SelectSingleNode("bpm_max").InnerText);
                header.BpmMin = ParseBpm(info.SelectSingleNode("bpm_min").InnerText);

                header.Levels = new Dictionary<Difficulty, VoxLevelHeader>();
                // Nabla (V7+) stores difnum as level*10 for every song, older databases store the level.
                // VoxCharger writes *10 for Nabla songs; any value above 20 also gives a song away as *10
                // (a stock song's NOV 2 is "20", which the old "> 20" check alone read as level 20)
                bool timesTen = header.Version >= GameVersion.Nabla;
                foreach (XmlNode diffInfo in music.SelectSingleNode("difficulty"))
                {
                    if (int.TryParse(diffInfo.SelectSingleNode("difnum")?.InnerText, out int value) && value > 20)
                        timesTen = true;
                }

                foreach (XmlNode diffInfo in music.SelectSingleNode("difficulty"))
                {
                    Difficulty current;
                    if (diffInfo.Name == "novice")
                        current = Difficulty.Novice;
                    else if (diffInfo.Name == "advanced")
                        current = Difficulty.Advanced;
                    else if (diffInfo.Name == "exhaust")
                        current = Difficulty.Exhaust;
                    else if (diffInfo.Name == "infinite" || diffInfo.Name == "maximum")
                        current = Difficulty.Infinite;
                    else
                        continue; // Unknown difficulty, 

                    int rawLevel = int.Parse(diffInfo.SelectSingleNode("difnum").InnerText);
                    if (timesTen)
                        rawLevel /= 10;

                    var lvHeader = new VoxLevelHeader
                    {
                        Difficulty  = current,
                        Level       = rawLevel,
                        Effector    = diffInfo.SelectSingleNode("effected_by").InnerText,
                        Illustrator = diffInfo.SelectSingleNode("illustrator").InnerText,
                        Price       = int.Parse(diffInfo.SelectSingleNode("price").InnerText),
                        Limited     = int.Parse(diffInfo.SelectSingleNode("limited").InnerText),
                        JacketPrint = int.Parse(diffInfo.SelectSingleNode("jacket_print").InnerText),
                        JacketMask  = int.Parse(diffInfo.SelectSingleNode("jacket_mask").InnerText),
                        MaxExScore  = int.TryParse(diffInfo.SelectSingleNode("max_exscore")?.InnerText, out int maxEx) ? maxEx : 0,
                    };

                    if (diffInfo.SelectSingleNode("radar") != null)
                    {
                        var radar = diffInfo.SelectSingleNode("radar");
                        lvHeader.Radar = new VoxLevelRadar()
                        {
                            Notes    = ParseRadar(radar.SelectSingleNode("notes").InnerText),
                            Peak     = ParseRadar(radar.SelectSingleNode("peak").InnerText),
                            Lasers   = ParseRadar(radar.SelectSingleNode("tsumami").InnerText),
                            Tricky   = ParseRadar(radar.SelectSingleNode("tricky").InnerText),
                            HandTrip = ParseRadar(radar.SelectSingleNode("hand-trip").InnerText),
                            OneHand  = ParseRadar(radar.SelectSingleNode("one-hand").InnerText),
                        };


                    }

                    if (lvHeader.Level <= 0)
                        continue; // Invalid level?
                    
                    header.Levels[current] = lvHeader;
                }

                Add(header);
            }
        }

        // Newer datecodes store radar values as u16 and a few stock charts exceed 255 (e.g. peak 293)
        // Clamp instead of failing the whole db load, radar is display-only on our side
        private static byte ParseRadar(string value)
        {
            int radar = int.Parse(value);
            return (byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, radar));
        }

        public void Save(string filename)
        {
            var doc    = new XmlDocument();
            var master = doc.CreateElement("mdb");

            var createMetaElement = new Func<XmlDocument, string, string, string, XmlElement>((d, n, v, t) =>
            {
                var prop  = doc.CreateElement(n);
                if (v != null)
                    prop.AppendChild(doc.CreateTextNode(AvsText(v)));
                
                if (!string.IsNullOrEmpty(t))
                    prop.SetAttribute("__type", t);

                return prop;
            });

            foreach (var header in _headers.Values)
            {
                if (header == null)
                    continue;

                var music = doc.CreateElement("music");
                music.SetAttribute("id", header.Id.ToString());

                var info = doc.CreateElement("info");
                info.AppendChild(createMetaElement(doc, "label",             header.Id.ToString(), string.Empty));
                info.AppendChild(createMetaElement(doc, "title_name",        header.Title, string.Empty));
                info.AppendChild(createMetaElement(doc, "title_yomigana",    header.TitleYomigana, string.Empty));
                info.AppendChild(createMetaElement(doc, "artist_name",       header.Artist, string.Empty));
                info.AppendChild(createMetaElement(doc, "artist_yomigana",   header.ArtistYomigana, string.Empty));
                info.AppendChild(createMetaElement(doc, "ascii",             header.Ascii, string.Empty));
                // BPM stored as (bpm * 100) in a u32 field. The old path formatted as
                // "000.00" then stripped the decimal separator which left leading zeros
                // for BPMs under 100 (e.g. 63.8 -> "06380"). Konami's strict prop parser
                // then read the value as invalid octal and aborted music_db parsing with
                // 80092209. Emit the integer directly — no zero-padding.
                info.AppendChild(createMetaElement(doc, "bpm_max",           ((uint)Math.Round(header.BpmMax * 100)).ToString(), "u32"));
                info.AppendChild(createMetaElement(doc, "bpm_min",           ((uint)Math.Round(header.BpmMin * 100)).ToString(), "u32"));
                info.AppendChild(createMetaElement(doc, "distribution_date", header.DistributionDate.ToString("yyyyMMdd"), "u32"));
                info.AppendChild(createMetaElement(doc, "volume",            header.Volume.ToString(), "u16"));
                info.AppendChild(createMetaElement(doc, "bg_no",             header.BackgroundId.ToString(), "u16"));
                bool isNabla = header.Version >= GameVersion.Nabla;
                info.AppendChild(createMetaElement(doc, "genre",             header.GenreId.ToString(), isNabla ? "u32" : "u8"));
                info.AppendChild(createMetaElement(doc, "is_fixed",          "1", "u8"));
                info.AppendChild(createMetaElement(doc, "version",           ((int)header.Version).ToString(), "u8"));
                info.AppendChild(createMetaElement(doc, "demo_pri",          "0", "s8"));
                info.AppendChild(createMetaElement(doc, "inf_ver",           ((int)header.InfVersion).ToString(), "u8"));

                var diffInfo = doc.CreateElement("difficulty");
                foreach (Difficulty difficulty in Enum.GetValues(typeof(Difficulty)))
                {
                    string diffName = Enum.GetName(typeof(Difficulty), difficulty).ToLower();
                    if (difficulty == Difficulty.Infinite && header.InfVersion == InfiniteVersion.Mxm)
                        diffName = "maximum";

                    var detail = doc.CreateElement(diffName);
                    var lvHeader  = new VoxLevelHeader() { Level = 0 };
                    if (header.Levels.ContainsKey(difficulty))
                        lvHeader = header.Levels[difficulty];

                    int difnum = isNabla ? lvHeader.Level * 10 : lvHeader.Level;
                    detail.AppendChild(createMetaElement(doc, "difnum",       difnum.ToString(), "u8"));
                    detail.AppendChild(createMetaElement(doc, "illustrator",  lvHeader.Illustrator, string.Empty));
                    detail.AppendChild(createMetaElement(doc, "effected_by",  lvHeader.Effector, string.Empty));
                    detail.AppendChild(createMetaElement(doc, "price",        lvHeader.Price.ToString(), "s32"));
                    detail.AppendChild(createMetaElement(doc, "limited",      lvHeader.Limited.ToString(), "u8"));
                    detail.AppendChild(createMetaElement(doc, "jacket_print", lvHeader.JacketPrint.ToString(), "s32"));
                    detail.AppendChild(createMetaElement(doc, "jacket_mask",  lvHeader.JacketMask.ToString(), "s32"));
                    detail.AppendChild(createMetaElement(doc, "max_exscore",  lvHeader.MaxExScore.ToString(), "s32"));

                    if (lvHeader.Radar != null)
                    {
                        // Nabla stores radar values as u16 (stock peak goes above 255). The game's
                        // property reader is type-strict, u8 nodes come back empty.
                        string radarType = isNabla ? "u16" : "u8";
                        var radar = detail.AppendChild(createMetaElement(doc, "radar", null, null));
                        radar.AppendChild(createMetaElement(doc, "notes", lvHeader.Radar.Notes.ToString(), radarType));
                        radar.AppendChild(createMetaElement(doc, "peak", lvHeader.Radar.Peak.ToString(), radarType));
                        radar.AppendChild(createMetaElement(doc, "tsumami", lvHeader.Radar.Lasers.ToString(), radarType));
                        radar.AppendChild(createMetaElement(doc, "tricky", lvHeader.Radar.Tricky.ToString(), radarType));
                        radar.AppendChild(createMetaElement(doc, "hand-trip", lvHeader.Radar.HandTrip.ToString(), radarType));
                        radar.AppendChild(createMetaElement(doc, "one-hand", lvHeader.Radar.OneHand.ToString(), radarType));
                    }

                    diffInfo.AppendChild(detail);
                }

                music.AppendChild(info);
                music.AppendChild(diffInfo);

                int tagId = ForcedTagId > 0 ? ForcedTagId : header.TagId;
                if (tagId > 0)
                {
                    var tag = doc.CreateElement("tag");
                    tag.SetAttribute("id", tagId.ToString());
                    music.AppendChild(tag);
                }

                master.AppendChild(music);
            }

            var settings = new XmlWriterSettings { Indent = true, Encoding = DefaultEncoding };
            using (var output = new StreamWriter(filename, false, DefaultEncoding))
            using (var writer = XmlWriter.Create(output, settings))
            {
                doc.AppendChild(master);
                doc.Save(writer);
            }
        }

        // The game's XML reader (avs2-core) rejects the Shift-JIS bytes 0x80, 0xA0 and 0xFD-0xFF, which
        // .NET's codec maps to these code points, and control characters. One such byte anywhere in the
        // merged music db makes the game load no songs at all, without a word in its log (the reader's
        // size query returns an error code the game only tests for being negative). 0xA0 is a Latin-1
        // non-breaking space in most files it comes from.
        public static string AvsText(string text)
        {
            if (text == null || !text.Any(IsUnreadable))
                return text;

            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (IsUnreadable(chars[i]))
                    chars[i] = chars[i] == '' ? ' ' : '?';
            }

            return new string(chars);
        }

        private static bool IsUnreadable(char c)
        {
            return c == '\u0080' || (c >= '' && c <= '') || (c < ' ' && c != '\t' && c != '\n' && c != '\r');
        }

        // True when saving would change one of the song's texts
        public static bool HasUnreadableText(VoxHeader header)
        {
            var texts = new List<string> { header.Title, header.TitleYomigana, header.Artist, header.ArtistYomigana, header.Ascii };
            if (header.Levels != null)
            {
                foreach (var level in header.Levels.Values)
                {
                    texts.Add(level.Illustrator);
                    texts.Add(level.Effector);
                }
            }

            return texts.Any(t => t != null && t.Any(IsUnreadable));
        }

        public void Add(VoxHeader header)
        {
            if (_max < header.Id)
                _max = header.Id;

            _headers[header.Id] = header;
        }

        // Songs are keyed by id, so changing VoxHeader.Id directly would leave the entry under the old key
        public void ChangeId(VoxHeader header, int id)
        {
            _headers.Remove(header.Id);
            header.Id = id;
            Add(header);
        }

        public void Remove(int id)
        {
            if (_max == id)
                _max = 0;

            _headers.Remove(id);
        }

        public void Remove(VoxHeader header)
        {
            Remove(header.Id);
        }

        public bool Contains(int id)
        {
            return _headers.ContainsKey(id);
        }

        public bool Contains(VoxHeader header)
        {
            return Contains(header.Id);
        }

        public VoxHeader GetHeader(int id)
        {
            return _headers[id];
        }

        public void Clear()
        {
            // Clear the list but not the last id
            _headers.Clear();
        }

        public IEnumerator<VoxHeader> GetEnumerator()
        {
            return _headers.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _headers.Values.GetEnumerator();
        }
    }
}
