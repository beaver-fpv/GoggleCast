using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace GoggleCast
{
    public sealed class DlnaDevice
    {
        public string Name, Model, ServiceType;
        public Uri DescriptionUri, ControlUri;
        internal string UniqueId;
        public override string ToString()
        {
            return (String.IsNullOrWhiteSpace(Name) ? "DLNA 接收设备" : Name) +
                (ControlUri == null ? "" : "  ·  " + ControlUri.Host);
        }
    }

    public static class Lan
    {
        public static List<IPAddress> GetAddresses()
        {
            var result = new List<IPAddress>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                    {
                        IPAddress address = info.Address;
                        if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) &&
                            !address.Equals(IPAddress.Any) && !result.Contains(address)) result.Add(address);
                    }
                }
                catch (NetworkInformationException) { }
            }
            return result;
        }

        // A connected UDP socket selects the route without sending a packet.
        public static IPAddress RouteTo(Uri target)
        {
            if (target == null || !target.IsAbsoluteUri) throw new ArgumentException("需要完整的设备地址。", "target");
            IPAddress address;
            if (!IPAddress.TryParse(target.DnsSafeHost, out address))
                address = Dns.GetHostAddresses(target.DnsSafeHost).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                throw new InvalidOperationException("首版仅支持 IPv4 局域网，请使用设备的 IPv4 地址。");
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect(new IPEndPoint(address, target.Port > 0 ? target.Port : 1900));
                return ((IPEndPoint)socket.LocalEndPoint).Address;
            }
        }
    }

    public static class DlnaDiscovery
    {
        private const string TransportPrefix = "urn:schemas-upnp-org:service:AVTransport:";
        private static readonly IPAddress Multicast = IPAddress.Parse("239.255.255.250");

        public static Task<List<DlnaDevice>> DiscoverAsync(int timeoutMs, CancellationToken token)
        {
            return DiscoverCoreAsync(timeoutMs, null, token);
        }

        public static async Task<DlnaDevice> FromAddressAsync(string address, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            address = (address ?? "").Trim();
            IPAddress ip;
            if (IPAddress.TryParse(address, out ip))
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException("请输入 IPv4 地址，或完整的 http/https 设备描述 URL。");
                List<DlnaDevice> found = await DiscoverCoreAsync(6000, ip, token).ConfigureAwait(false);
                if (found.Count == 0)
                    throw new InvalidOperationException("设备 " + ip + " 没有响应标准 UDP 1900 定向发现，或没有提供 AVTransport 投屏服务。请开启眼镜的无线投屏并确认同一局域网；也可输入已知的完整设备描述 URL。程序不会猜测设备端口。");
                return found[0];
            }
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || !DlnaXml.IsHttp(uri))
                throw new ArgumentException("请输入设备的 IPv4 地址，或完整的设备描述 URL（例如 http://设备地址:端口/description.xml）。");
            List<DlnaDevice> devices = await ReadDevicesAsync(uri, token).ConfigureAwait(false);
            if (devices.Count == 0) throw new InvalidOperationException("此设备描述没有 AVTransport 服务，无法作为 DLNA 视频接收端。");
            return devices[0];
        }

        private static async Task<List<DlnaDevice>> DiscoverCoreAsync(int timeoutMs, IPAddress target, CancellationToken token)
        {
            if (timeoutMs < 100 || timeoutMs > 60000) throw new ArgumentOutOfRangeException("timeoutMs", "发现超时必须为 100～60000 毫秒。");
            token.ThrowIfCancellationRequested();
            List<IPAddress> addresses = target == null ? Lan.GetAddresses() : new List<IPAddress> { Lan.RouteTo(new Uri("http://" + target + "/")) };
            if (addresses.Count == 0) throw new InvalidOperationException("没有已连接的 IPv4 网卡，请先连接 Wi-Fi 或有线局域网。");
            var state = new DiscoveryState();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var slots = new SemaphoreSlim(6))
            {
                deadline.CancelAfter(timeoutMs);
                var receivers = new List<Task>();
                foreach (IPAddress address in addresses)
                    receivers.Add(ReceiveAsync(address, target, Math.Max(1, Math.Min(3, timeoutMs / 2000)), state, slots, deadline.Token));
                await Task.WhenAll(receivers).ConfigureAwait(false);
                Task[] pending;
                lock (state.Gate) pending = state.Reads.ToArray();
                await Task.WhenAll(pending).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (state.Sent == 0 && state.Errors.Count > 0)
                    throw new IOException("无法发送局域网发现请求：" + state.Errors[0]);
                if (target != null && state.Devices.Count == 0 && state.Errors.Count > 0)
                    throw new IOException("发现 " + target + " 时发生错误：" + state.Errors[0]);
                return state.Devices.Values.OrderByDescending(d => (d.Name + " " + d.Model).IndexOf("goggles", StringComparison.OrdinalIgnoreCase) >= 0).ThenBy(d => d.Name).ToList();
            }
        }

        private sealed class DiscoveryState
        {
            internal readonly object Gate = new object();
            internal readonly HashSet<string> Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, DlnaDevice> Devices = new Dictionary<string, DlnaDevice>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<Task> Reads = new List<Task>();
            internal readonly List<string> Errors = new List<string>();
            internal int Sent;
        }

        private static async Task ReceiveAsync(IPAddress local, IPAddress target, int mx, DiscoveryState state, SemaphoreSlim slots, CancellationToken token)
        {
            try
            {
                using (var udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    udp.Client.Bind(new IPEndPoint(local, 0));
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                    using (token.Register(delegate { udp.Close(); }))
                    {
                        var endpoint = new IPEndPoint(target ?? Multicast, 1900);
                        string[] searches = { TransportPrefix + "1", "urn:schemas-upnp-org:device:MediaRenderer:1", "ssdp:all" };
                        // UDP is unreliable; issue each search twice, as recommended by UDA.
                        for (int repeat = 0; repeat < 2; repeat++)
                        {
                            foreach (string search in searches)
                            {
                                token.ThrowIfCancellationRequested();
                                string request = "M-SEARCH * HTTP/1.1\r\nHOST: " + endpoint + "\r\nMAN: \"ssdp:discover\"\r\n" +
                                    (target == null ? "MX: " + mx.ToString(CultureInfo.InvariantCulture) + "\r\n" : "") +
                                    "ST: " + search + "\r\nUSER-AGENT: Windows/10.0 UPnP/1.1 GoggleCast/0.1\r\n\r\n";
                                byte[] bytes = Encoding.ASCII.GetBytes(request);
                                await udp.SendAsync(bytes, bytes.Length, endpoint).ConfigureAwait(false);
                                Interlocked.Increment(ref state.Sent);
                            }
                        }
                        while (!token.IsCancellationRequested)
                        {
                            UdpReceiveResult reply = await udp.ReceiveAsync().ConfigureAwait(false);
                            if (target != null && !reply.RemoteEndPoint.Address.Equals(target)) continue;
                            if (reply.Buffer.Length > 32768) continue;
                            string[] lines = Encoding.UTF8.GetString(reply.Buffer).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                            if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)) continue;
                            foreach (string line in lines)
                            {
                                int colon = line.IndexOf(':');
                                if (colon < 0 || !line.Substring(0, colon).Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase)) continue;
                                Uri location;
                                if (!Uri.TryCreate(line.Substring(colon + 1).Trim(), UriKind.Absolute, out location) || !DlnaXml.IsHttp(location)) continue;
                                lock (state.Gate)
                                {
                                    if (state.Locations.Count < 64 && state.Locations.Add(location.AbsoluteUri))
                                        state.Reads.Add(ReadDiscoveredAsync(location, state, slots, token));
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { if (!token.IsCancellationRequested) AddError(state, "网络接口已关闭：" + local); }
            catch (SocketException error) { if (!token.IsCancellationRequested) AddError(state, local + "：" + error.Message); }
            catch (IOException error) { if (!token.IsCancellationRequested) AddError(state, local + "：" + error.Message); }
        }

        private static void AddError(DiscoveryState state, string error)
        {
            lock (state.Gate) if (state.Errors.Count < 8) state.Errors.Add(error);
        }

        private static async Task ReadDiscoveredAsync(Uri uri, DiscoveryState state, SemaphoreSlim slots, CancellationToken token)
        {
            bool entered = false;
            try
            {
                await slots.WaitAsync(token).ConfigureAwait(false);
                entered = true;
                List<DlnaDevice> found = await ReadDevicesAsync(uri, token).ConfigureAwait(false);
                lock (state.Gate)
                    foreach (DlnaDevice device in found)
                    {
                        string key = String.IsNullOrWhiteSpace(device.UniqueId) ? device.ControlUri.AbsoluteUri : device.UniqueId;
                        if (!state.Devices.ContainsKey(key)) state.Devices.Add(key, device);
                    }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { AddError(state, uri + "：" + error.Message); }
            finally { if (entered) slots.Release(); }
        }

        private static async Task<List<DlnaDevice>> ReadDevicesAsync(Uri uri, CancellationToken token)
        {
            HttpXmlReply reply = await DlnaXml.SendAsync(uri, null, null, token).ConfigureAwait(false);
            if (reply.Status < 200 || reply.Status >= 300) throw new IOException("读取设备描述失败，HTTP " + reply.Status + "：" + uri);
            XmlDocument doc = DlnaXml.Parse(reply.Bytes);
            Uri baseUri = reply.ResponseUri ?? uri;
            string specifiedBase = DlnaXml.Text(doc.DocumentElement, "URLBase");
            if (!String.IsNullOrWhiteSpace(specifiedBase))
            {
                Uri resolved;
                if (!Uri.TryCreate(baseUri, specifiedBase, out resolved) || !DlnaXml.IsHttp(resolved)) throw new XmlException("设备 URLBase 无效。");
                baseUri = resolved;
            }
            var result = new List<DlnaDevice>();
            foreach (XmlNode device in doc.SelectNodes("//*[local-name()='device']"))
            {
                // The service must belong to this device, not a nested sibling/device.
                foreach (XmlNode service in device.SelectNodes("./*[local-name()='serviceList']/*[local-name()='service']"))
                {
                    string type = DlnaXml.Text(service, "serviceType");
                    int version;
                    if (!type.StartsWith(TransportPrefix, StringComparison.Ordinal) || !Int32.TryParse(type.Substring(TransportPrefix.Length), out version) || version < 1) continue;
                    string control = DlnaXml.Text(service, "controlURL");
                    Uri controlUri;
                    if (String.IsNullOrWhiteSpace(control) || !Uri.TryCreate(baseUri, control, out controlUri) || !DlnaXml.IsHttp(controlUri)) continue;
                    result.Add(new DlnaDevice {
                        Name = DlnaXml.Text(device, "friendlyName"), Model = DlnaXml.Text(device, "modelName"),
                        ServiceType = type, DescriptionUri = uri, ControlUri = controlUri,
                        UniqueId = DlnaXml.Text(device, "UDN")
                    });
                    break;
                }
            }
            return result;
        }
    }

    public sealed class DlnaClient
    {
        private readonly DlnaDevice device;
        public DlnaClient(DlnaDevice device)
        {
            if (device == null || device.ControlUri == null || String.IsNullOrWhiteSpace(device.ServiceType))
                throw new ArgumentException("设备缺少 AVTransport 控制地址。", "device");
            this.device = device;
        }

        public async Task SetUriAsync(string url, string title, string mime, bool live, CancellationToken token)
        {
            Uri media;
            if (!Uri.TryCreate(url, UriKind.Absolute, out media) || !DlnaXml.IsHttp(media)) throw new ArgumentException("媒体地址必须为完整的 http/https URL。", "url");
            if (mime != "video/mp4" && mime != "video/mp2t" && mime != "video/mpeg") throw new ArgumentException("当前支持 video/mp4、video/mp2t、video/mpeg。", "mime");
            var didl = new XmlDocument();
            const string ns = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
            XmlElement root = didl.CreateElement("DIDL-Lite", ns);
            didl.AppendChild(root);
            XmlElement item = didl.CreateElement("item", ns);
            item.SetAttribute("id", "gogglecast-1"); item.SetAttribute("parentID", "0"); item.SetAttribute("restricted", "1");
            root.AppendChild(item);
            XmlElement name = didl.CreateElement("dc", "title", "http://purl.org/dc/elements/1.1/");
            name.InnerText = String.IsNullOrWhiteSpace(title) ? "GoggleCast" : title;
            item.AppendChild(name);
            XmlElement kind = didl.CreateElement("upnp", "class", "urn:schemas-upnp-org:metadata-1-0/upnp/");
            kind.InnerText = "object.item.videoItem"; item.AppendChild(kind);
            XmlElement resource = didl.CreateElement("res", ns);
            // OP bits are time-seek, byte-seek: only finite files advertise byte ranges.
            resource.SetAttribute("protocolInfo", "http-get:*:" + mime + ":DLNA.ORG_OP=" + (live ? "00" : "01") +
                ";DLNA.ORG_CI=" + (live ? "1" : "0") + ";DLNA.ORG_FLAGS=01100000000000000000000000000000");
            resource.InnerText = media.AbsoluteUri; item.AppendChild(resource);
            await InvokeAsync("SetAVTransportURI", new[] { "InstanceID", "0", "CurrentURI", media.AbsoluteUri, "CurrentURIMetaData", didl.OuterXml }, token).ConfigureAwait(false);
        }

        public async Task PlayAsync(CancellationToken token)
        {
            await InvokeAsync("Play", new[] { "InstanceID", "0", "Speed", "1" }, token).ConfigureAwait(false);
        }

        public async Task StopAsync(CancellationToken token)
        {
            await InvokeAsync("Stop", new[] { "InstanceID", "0" }, token).ConfigureAwait(false);
        }

        public async Task<string> GetTransportStateAsync(CancellationToken token)
        {
            XmlDocument response = await InvokeAsync("GetTransportInfo", new[] { "InstanceID", "0" }, token).ConfigureAwait(false);
            XmlNode state = response.SelectSingleNode("//*[local-name()='CurrentTransportState']");
            if (state == null) throw new IOException("设备没有返回 CurrentTransportState。");
            return state.InnerText.Trim();
        }

        private async Task<XmlDocument> InvokeAsync(string action, string[] arguments, CancellationToken token)
        {
            var xml = new XmlDocument();
            const string ns = "http://schemas.xmlsoap.org/soap/envelope/";
            XmlElement envelope = xml.CreateElement("s", "Envelope", ns);
            envelope.SetAttribute("encodingStyle", ns, "http://schemas.xmlsoap.org/soap/encoding/");
            xml.AppendChild(envelope);
            XmlElement body = xml.CreateElement("s", "Body", ns); envelope.AppendChild(body);
            XmlElement call = xml.CreateElement("u", action, device.ServiceType); body.AppendChild(call);
            for (int i = 0; i < arguments.Length; i += 2)
            {
                XmlElement argument = xml.CreateElement(arguments[i]);
                argument.InnerText = arguments[i + 1]; call.AppendChild(argument);
            }
            // Some renderers finish buffering/decoder preparation before replying to Play.
            // Give only that action more time; user cancellation still aborts immediately.
            int timeoutMs = action == "Play" ? 30000 : 8000;
            HttpXmlReply reply = await DlnaXml.SendAsync(device.ControlUri, Encoding.UTF8.GetBytes(xml.OuterXml), device.ServiceType + "#" + action, token, timeoutMs).ConfigureAwait(false);
            XmlDocument response;
            try { response = DlnaXml.Parse(reply.Bytes); }
            catch (XmlException error) { throw new IOException(action + " 返回无效 XML（HTTP " + reply.Status + "）：" + device.ControlUri, error); }
            XmlNode fault = response.SelectSingleNode("//*[local-name()='Fault']");
            if (fault != null)
            {
                XmlNode code = fault.SelectSingleNode(".//*[local-name()='errorCode']");
                XmlNode detail = fault.SelectSingleNode(".//*[local-name()='errorDescription']");
                string fallback = DlnaXml.Text(fault, "faultstring");
                throw new IOException(action + " 被设备拒绝（HTTP " + reply.Status +
                    (code == null ? "" : "，UPnP " + code.InnerText.Trim()) + "）：" + (detail == null ? fallback : detail.InnerText.Trim()));
            }
            if (reply.Status < 200 || reply.Status >= 300) throw new IOException(action + " 失败，HTTP " + reply.Status + "：" + device.ControlUri);
            if (response.SelectSingleNode("//*[local-name()='" + action + "Response']") == null)
                throw new IOException(action + " 返回了无法识别的响应：" + device.ControlUri);
            return response;
        }
    }

    internal sealed class HttpXmlReply
    {
        internal int Status;
        internal byte[] Bytes;
        internal Uri ResponseUri;
    }

    internal static class DlnaXml
    {
        internal static bool IsHttp(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && String.IsNullOrEmpty(uri.UserInfo);
        }

        internal static string Text(XmlNode parent, string localName)
        {
            if (parent == null) return "";
            XmlNode node = parent.SelectSingleNode("./*[local-name()='" + localName + "']");
            return node == null ? "" : node.InnerText.Trim();
        }

        internal static XmlDocument Parse(byte[] bytes)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 };
            var doc = new XmlDocument { XmlResolver = null };
            using (var stream = new MemoryStream(bytes))
            using (XmlReader reader = XmlReader.Create(stream, settings)) doc.Load(reader);
            return doc;
        }

        internal static async Task<HttpXmlReply> SendAsync(Uri uri, byte[] body, string action, CancellationToken token, int timeoutMs = 8000)
        {
            token.ThrowIfCancellationRequested();
            if (!IsHttp(uri)) throw new ArgumentException("设备 URL 必须使用 http/https，且不能包含登录凭据。");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = null; request.Timeout = timeoutMs; request.ReadWriteTimeout = timeoutMs;
            request.KeepAlive = false; request.AllowAutoRedirect = false;
            request.UserAgent = "GoggleCast/0.1 UPnP/1.1";
            request.ServicePoint.Expect100Continue = false;
            if (body != null)
            {
                request.Method = "POST"; request.ContentType = "text/xml; charset=\"utf-8\"";
                request.Headers["SOAPACTION"] = "\"" + action + "\""; request.ContentLength = body.Length;
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(timeoutMs);
                using (timeout.Token.Register(delegate { request.Abort(); }))
                {
                    try
                    {
                        if (body != null)
                            using (Stream outgoing = await request.GetRequestStreamAsync().ConfigureAwait(false))
                                await outgoing.WriteAsync(body, 0, body.Length, timeout.Token).ConfigureAwait(false);
                        HttpWebResponse response;
                        try { response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false); }
                        catch (WebException error)
                        {
                            response = error.Response as HttpWebResponse;
                            if (response == null) throw;
                        }
                        using (response)
                        using (Stream incoming = response.GetResponseStream())
                        using (var data = new MemoryStream())
                        {
                            var buffer = new byte[8192];
                            int count;
                            while ((count = await incoming.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                if (data.Length + count > 2097152) throw new IOException("设备 XML 响应超过 2 MB 上限。");
                                data.Write(buffer, 0, count);
                            }
                            return new HttpXmlReply { Status = (int)response.StatusCode, Bytes = data.ToArray(), ResponseUri = response.ResponseUri };
                        }
                    }
                    catch (Exception error)
                    {
                        token.ThrowIfCancellationRequested();
                        if (timeout.IsCancellationRequested) throw new TimeoutException("设备连接超过 " + (timeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " 秒：" + uri, error);
                        if (error is WebException || error is SocketException) throw new IOException("无法连接设备 " + uri + "：" + error.Message, error);
                        throw;
                    }
                }
            }
        }
    }
}
