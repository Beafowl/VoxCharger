using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace VoxCharger
{
    public class RemoteMixClient : IDisposable
    {
        private readonly WebClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public string BaseUrl { get; set; }

        public RemoteMixClient(string baseUrl)
        {
            BaseUrl = baseUrl.TrimEnd('/');
            _http = new WebClient { Encoding = Encoding.UTF8 };
        }

        public List<RemoteMix> GetMixes()
        {
            string response = _http.DownloadString($"{BaseUrl}/mixes");
            return _json.Deserialize<List<RemoteMix>>(response);
        }

        public RemoteMixDetail GetMix(int mixId)
        {
            string response = _http.DownloadString($"{BaseUrl}/mixes/{mixId}");
            return _json.Deserialize<RemoteMixDetail>(response);
        }

        public RemoteMixDetail CreateMix(string name, int musicIdStart)
        {
            string body = _json.Serialize(new { name, music_id_start = musicIdStart });
            _http.Headers[HttpRequestHeader.ContentType] = "application/json";
            string response = _http.UploadString($"{BaseUrl}/mixes", "POST", body);
            return _json.Deserialize<RemoteMixDetail>(response);
        }

        public RemoteSong AddSong(int mixId, string url, string title = null, string artist = null)
        {
            string body = _json.Serialize(new { url, title, artist });
            _http.Headers[HttpRequestHeader.ContentType] = "application/json";
            string response = _http.UploadString($"{BaseUrl}/mixes/{mixId}/songs", "POST", body);
            return _json.Deserialize<RemoteSong>(response);
        }

        public void RemoveMix(int mixId)
        {
            _http.Headers[HttpRequestHeader.ContentType] = "application/json";
            _http.UploadString($"{BaseUrl}/mixes/{mixId}", "DELETE", "");
        }

        public void RemoveSong(int mixId, int songId)
        {
            _http.Headers[HttpRequestHeader.ContentType] = "application/json";
            _http.UploadString($"{BaseUrl}/mixes/{mixId}/songs/{songId}", "DELETE", "");
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
