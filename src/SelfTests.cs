using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace GoggleCast
{
    internal static class SelfTests
    {
        public static async Task<int> Run(string output)
        {
            Directory.CreateDirectory(output);
            var results = new List<string>();
            Action<string> log = message => { lock (results) results.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message); };
            int failures = 0;
            try
            {
                string dataPath = Path.Combine(output, "range-fixture.mp4");
                byte[] data = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
                File.WriteAllBytes(dataPath, data);
                using (var server = new MediaHttpServer(dataPath))
                {
                    server.Log += log;
                    server.Start(IPAddress.Loopback);
                    string url = server.UrlFor(IPAddress.Loopback);
                    var full = await Fetch(url, null, "GET");
                    Require(full.Status == 200 && full.Data.SequenceEqual(data), "full file response");
                    var range = await Fetch(url, "bytes=13-37", "GET");
                    Require(range.Status == 206 && range.Data.SequenceEqual(data.Skip(13).Take(25)), "byte range exactness");
                    Require(range.Headers.IndexOf("Content-Range: bytes 13-37/4096", StringComparison.OrdinalIgnoreCase) >= 0, "Content-Range header");
                    var suffix = await Fetch(url, "bytes=-19", "GET");
                    Require(suffix.Status == 206 && suffix.Data.SequenceEqual(data.Skip(4077)), "suffix range");
                    var invalid = await Fetch(url, "bytes=9000-", "GET");
                    Require(invalid.Status == 416, "unsatisfiable range");
                    var head = await Fetch(url, null, "HEAD");
                    Require(head.Status == 200 && head.Data.Length == 0, "HEAD sends no body");
                    foreach (string headRange in new[] { "bytes=0-0", "bytes=9000-", "invalid-range" })
                    {
                        var rangedHead = await Fetch(url, headRange, "HEAD");
                        Require(rangedHead.Status == 200 && rangedHead.Data.Length == 0, "file HEAD ignores Range: " + headRange);
                        Require(HeaderValue(rangedHead.Headers, "Content-Length") == data.Length.ToString() &&
                            HeaderValue(rangedHead.Headers, "Content-Range") == null, "file HEAD describes full file: " + headRange);
                    }
                    await CheckMetadataFeatures(url, "video/mp4", false, head.Headers);
                    var denied = await Fetch("http://127.0.0.1:" + server.Port + "/anything", null, "GET");
                    Require(denied.Status == 404, "unguessable media path / no directory exposure");
                    var method = await Fetch(url, null, "POST");
                    Require(method.Status == 405, "write methods rejected");
                    log("PASS: finite HTTP file, seeking, HEAD including valid/invalid Range, 404, 405, 416; DIDL/HTTP OP=01 CI=0 flags=011 agree.");
                }
                File.Delete(dataPath);
            }
            catch (Exception ex) { failures++; log("FAIL HTTP: " + ex); }

            string ffmpeg = FFmpegLocator.Find();
            if (string.IsNullOrEmpty(ffmpeg)) { failures++; log("FAIL: FFmpeg not found."); }
            else
            {
                foreach (string container in new[] { "ts", "mp4" })
                {
                    try
                    {
                        var settings = new CaptureSettings { SourceKind = "test", OutputWidth = 640, OutputHeight = 360,
                            Fps = 24, BitrateKbps = 1200, Encoder = "x264", Container = container };
                        int starts = 0;
                        var server = new MediaHttpServer(async token =>
                        {
                            Interlocked.Increment(ref starts);
                            return await MediaSession.StartAsync(ffmpeg, settings, token, log);
                        }, container == "ts" ? "video/mp2t" : "video/mp4");
                        try
                        {
                            server.Log += log;
                            server.Start(IPAddress.Loopback);
                            string url = server.UrlFor(IPAddress.Loopback);
                            var head = await Fetch(url, null, "HEAD");
                            Require(head.Status == 200 && starts == 0, "live HEAD must not start screen capture");
                            foreach (string headRange in new[] { "bytes=0-0", "bytes=99-", "invalid-range" })
                            {
                                var rangedHead = await Fetch(url, headRange, "HEAD");
                                Require(rangedHead.Status == 200 && rangedHead.Data.Length == 0 && starts == 0,
                                    "live HEAD ignores Range without encoder: " + headRange);
                                Require(HeaderValue(rangedHead.Headers, "Content-Length") == null &&
                                    HeaderValue(rangedHead.Headers, "Content-Range") == null, "live HEAD does not invent size or offsets");
                            }
                            await CheckMetadataFeatures(url, container == "ts" ? "video/mp2t" : "video/mp4", true, head.Headers);
                            var invalid = await Fetch(url, "bytes=99-", "GET");
                            Require(invalid.Status == 416 && starts == 0, "live nonzero seek rejection");
                            string file = Path.Combine(output, "live-test." + container);
                            int received = await ReadLive(url, file, 5500);
                            Require(received > 40000, "continuous live payload");
                            Require(starts == 1, "one encoder per playback GET");
                            byte[] bytes = File.ReadAllBytes(file);
                            if (container == "ts") Require(bytes[0] == 0x47 && bytes[188] == 0x47, "MPEG-TS sync");
                            else
                            {
                                string atoms = Encoding.ASCII.GetString(bytes);
                                Require(atoms.Contains("ftyp") && atoms.Contains("moov") && atoms.Contains("moof"), "fragmented MP4 headers");
                            }
                            log("PASS: live " + container + " delivered " + received + " bytes; HEAD ignores valid/invalid Range without encoder; DIDL/HTTP OP=00 CI=1 flags=011 agree.");
                        }
                        finally { server.Dispose(); }
                        await Task.Delay(1300);
                        Require(server.ActiveClients == 0, "stop releases streaming clients");
                        log("PASS: " + container + " stop cleaned active clients.");
                    }
                    catch (Exception ex) { failures++; log("FAIL live " + container + ": " + ex); }
                }
            }
            log("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + "; failures=" + failures + ". Goggles hardware playback NOT tested.");
            File.WriteAllLines(Path.Combine(output, "results.txt"), results, new UTF8Encoding(false));
            return failures == 0 ? 0 : 1;
        }

        private static void Require(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Assertion failed: " + name); }

        private static string HeaderValue(string headers, string name)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        // Compare metadata actually sent over SOAP with the HTTP origin's actual headers.
        private static async Task CheckMetadataFeatures(string mediaUrl, string mime, bool live, string headers)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using (var timeout = new CancellationTokenSource(8000))
                using (timeout.Token.Register(() => listener.Stop()))
                {
                    var device = new DlnaDevice {
                        Name = "Local protocol regression mock", ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
                        ControlUri = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/transport")
                    };
                    Task<string> received = ReadMetadataRequest(listener, timeout.Token);
                    Task sent = new DlnaClient(device).SetUriAsync(mediaUrl, "GoggleCast regression", mime, live, timeout.Token);
                    await Task.WhenAll(received, sent);
                    string features = HeaderValue(headers, "contentFeatures.dlna.org");
                    string expected = "DLNA.ORG_OP=" + (live ? "00" : "01") + ";DLNA.ORG_CI=" + (live ? "1" : "0") +
                        ";DLNA.ORG_FLAGS=01100000000000000000000000000000";
                    Require(features == expected, "HTTP feature flags accurately describe " + (live ? "live encoding" : "file playback"));
                    Require(received.Result == "http-get:*:" + mime + ":" + features, "SOAP DIDL agrees with HTTP content features");
                }
            }
            finally { listener.Stop(); }
        }

        private static async Task<string> ReadMetadataRequest(TcpListener listener, CancellationToken token)
        {
            using (TcpClient client = await listener.AcceptTcpClientAsync())
            using (token.Register(() => client.Close()))
            using (NetworkStream stream = client.GetStream())
            {
                var header = new StringBuilder();
                byte[] one = new byte[1];
                while (header.Length < 16384)
                {
                    if (await stream.ReadAsync(one, 0, 1, token) == 0) throw new IOException("SOAP mock request ended early.");
                    header.Append((char)one[0]);
                    if (header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
                }
                int length;
                Require(Int32.TryParse(HeaderValue(header.ToString(), "Content-Length"), out length) && length > 0 && length < 1048576,
                    "SOAP request has bounded content length");
                byte[] body = new byte[length];
                int offset = 0;
                while (offset < body.Length)
                {
                    int count = await stream.ReadAsync(body, offset, body.Length - offset, token);
                    if (count == 0) throw new IOException("SOAP mock request body ended early.");
                    offset += count;
                }
                XmlDocument soap = DlnaXml.Parse(body);
                XmlNode metadata = soap.SelectSingleNode("//*[local-name()='CurrentURIMetaData']");
                Require(metadata != null, "SetAVTransportURI includes DIDL metadata");
                XmlDocument didl = DlnaXml.Parse(Encoding.UTF8.GetBytes(metadata.InnerText));
                XmlElement resource = didl.SelectSingleNode("//*[local-name()='res']") as XmlElement;
                Require(resource != null, "DIDL includes media resource");
                byte[] payload = Encoding.UTF8.GetBytes("<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body>" +
                    "<u:SetAVTransportURIResponse xmlns:u='urn:schemas-upnp-org:service:AVTransport:1'/></s:Body></s:Envelope>");
                byte[] replyHeader = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/xml; charset=utf-8\r\nContent-Length: " +
                    payload.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(replyHeader, 0, replyHeader.Length, token);
                await stream.WriteAsync(payload, 0, payload.Length, token);
                return resource.GetAttribute("protocolInfo");
            }
        }

        private sealed class Reply { public int Status; public string Headers; public byte[] Data; }
        private static async Task<Reply> Fetch(string address, string range, string method)
        {
            var uri = new Uri(address);
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(uri.Host, uri.Port);
                using (var timeout = new CancellationTokenSource(8000))
                using (timeout.Token.Register(() => client.Close()))
                using (var stream = client.GetStream())
                {
                    byte[] request = Encoding.ASCII.GetBytes(method + " " + uri.PathAndQuery + " HTTP/1.1\r\nHost: " + uri.Authority + "\r\nConnection: close\r\n" +
                        (range == null ? "" : "Range: " + range + "\r\n") + "\r\n");
                    await stream.WriteAsync(request, 0, request.Length);
                    var all = new MemoryStream();
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0) all.Write(buffer, 0, count);
                    byte[] bytes = all.ToArray();
                    int split = HeaderEnd(bytes);
                    string header = Encoding.ASCII.GetString(bytes, 0, split);
                    return new Reply { Status = int.Parse(header.Split(' ')[1]), Headers = header, Data = bytes.Skip(split + 4).ToArray() };
                }
            }
        }

        private static int HeaderEnd(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length - 3; i++)
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) return i;
            throw new IOException("Incomplete HTTP headers");
        }

        private static async Task<int> ReadLive(string address, string file, int milliseconds)
        {
            var uri = new Uri(address);
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(uri.Host, uri.Port);
                using (var timeout = new CancellationTokenSource(25000))
                using (timeout.Token.Register(() => client.Close()))
                using (var stream = client.GetStream())
                {
                    byte[] request = Encoding.ASCII.GetBytes("GET " + uri.PathAndQuery + " HTTP/1.1\r\nHost: " + uri.Authority + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(request, 0, request.Length);
                    var header = new MemoryStream();
                    byte[] one = new byte[1];
                    int matched = 0;
                    byte[] end = { 13, 10, 13, 10 };
                    while (matched < 4)
                    {
                        if (await stream.ReadAsync(one, 0, 1, timeout.Token) == 0) throw new IOException("No live response");
                        header.WriteByte(one[0]);
                        matched = one[0] == end[matched] ? matched + 1 : (one[0] == 13 ? 1 : 0);
                    }
                    Require(Encoding.ASCII.GetString(header.ToArray()).StartsWith("HTTP/1.0 200"), "live HTTP status");
                    int bytes = 0;
                    var clock = Stopwatch.StartNew();
                    using (var output = File.Create(file))
                    {
                        byte[] buffer = new byte[32768];
                        while (clock.ElapsedMilliseconds < milliseconds)
                        {
                            int count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token);
                            if (count == 0) break;
                            output.Write(buffer, 0, count);
                            bytes += count;
                        }
                    }
                    return bytes;
                }
            }
        }
    }
}
