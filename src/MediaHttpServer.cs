using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GoggleCast
{
    // A deliberately small, read-only DLNA HTTP origin. No URL ACL or admin rights.
    // Every playback session gets a different unguessable path; no directory is served.
    public sealed class MediaHttpServer : IDisposable
    {
        private readonly Func<CancellationToken, Task<MediaSession>> factory;
        private readonly string filePath;
        private readonly string mime;
        private readonly string path;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
        private readonly SemaphoreSlim slots = new SemaphoreSlim(6);
        private TcpListener listener;
        private long bytesSent;
        private int activeClients;
        private int totalRequests;
        private int clientId;
        private int disposed;
        private int port;
        public event Action<string> Log;
        public long BytesSent { get { return Interlocked.Read(ref bytesSent); } }
        public int ActiveClients { get { return Volatile.Read(ref activeClients); } }
        public int TotalRequests { get { return Volatile.Read(ref totalRequests); } }
        public int Port { get { return port; } }

        public MediaHttpServer(Func<CancellationToken, Task<MediaSession>> factory, string mime)
        {
            if (factory == null) throw new ArgumentNullException("factory");
            if (mime != "video/mp2t" && mime != "video/mp4") throw new ArgumentException("Unsupported live content type.");
            this.factory = factory;
            this.mime = mime;
            this.path = "/" + Guid.NewGuid().ToString("N") + (mime == "video/mp4" ? "/screen.mp4" : "/screen.ts");
        }

        public MediaHttpServer(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("找不到视频文件。", filePath);
            this.filePath = Path.GetFullPath(filePath);
            mime = "video/mp4";
            path = "/" + Guid.NewGuid().ToString("N") + "/video.mp4";
        }

        public void Start(IPAddress bindAddress, int port = 0)
        {
            if (disposed != 0) throw new ObjectDisposedException("MediaHttpServer");
            if (listener != null) throw new InvalidOperationException("Server has already started.");
            if (bindAddress == null || bindAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("请选择可到达眼镜的 IPv4 网络地址。");
            listener = new TcpListener(bindAddress, port);
            listener.Start(8);
            this.port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run((Func<Task>)AcceptLoop);
            WriteLog("视频服务已启动：" + bindAddress + ":" + this.port);
        }

        public string UrlFor(IPAddress address)
        {
            if (port == 0) throw new InvalidOperationException("Server has not started.");
            return "http://" + address + ":" + port + path;
        }

        private async Task AcceptLoop()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    int id = Interlocked.Increment(ref clientId);
                    clients[id] = client;
                    Task.Run(() => HandleClient(id, client));
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException ex) { if (!lifetime.IsCancellationRequested) WriteLog("视频服务异常：" + ex.Message); }
            catch (Exception ex) { if (!lifetime.IsCancellationRequested) WriteLog("视频服务异常：" + ex.Message); }
        }

        private async Task HandleClient(int id, TcpClient client)
        {
            bool slot = false;
            bool active = false;
            try
            {
                client.NoDelay = true;
                using (lifetime.Token.Register(() => CloseQuietly(client)))
                using (NetworkStream network = client.GetStream())
                {
                    if (!slots.Wait(0)) { await Error(network, 503, "Busy", lifetime.Token).ConfigureAwait(false); return; }
                    slot = true;
                    string header;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        timeout.CancelAfter(7000);
                        using (timeout.Token.Register(() => { if (timeout.IsCancellationRequested) CloseQuietly(client); }))
                            header = await ReadHeader(network, timeout.Token).ConfigureAwait(false);
                    }
                    if (header == null) return;
                    string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] request = lines[0].Split(' ');
                    if (request.Length != 3) { await Error(network, 400, "Bad Request", lifetime.Token).ConfigureAwait(false); return; }
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in lines)
                    {
                        int colon = line.IndexOf(':');
                        if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }
                    if (request[1] != path) { await Error(network, 404, "Not Found", lifetime.Token).ConfigureAwait(false); return; }
                    if (request[0] != "GET" && request[0] != "HEAD")
                    { await Error(network, 405, "Method Not Allowed", lifetime.Token).ConfigureAwait(false); return; }
                    Interlocked.Increment(ref totalRequests);
                    string remote = Convert.ToString(client.Client.RemoteEndPoint, CultureInfo.InvariantCulture);
                    WriteLog("收到设备请求：" + request[0] + " · " + remote);
                    if (filePath != null)
                    {
                        if (request[0] == "GET") { active = true; Interlocked.Increment(ref activeClients); }
                        await SendFile(network, headers, request[0] == "HEAD", lifetime.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        // Range only changes GET semantics; HEAD describes the whole resource.
                        if (request[0] == "HEAD")
                        { await SendHeader(network, 200, "OK", null, false, null, lifetime.Token).ConfigureAwait(false); return; }
                        string range;
                        if (headers.TryGetValue("Range", out range) && range != "bytes=0-" && range != "bytes=0-0")
                        {
                            // A live resource has no random access. Never pretend to fulfill an offset.
                            await Error(network, 416, "Range Not Satisfiable", lifetime.Token).ConfigureAwait(false);
                            return;
                        }
                        using (MediaSession session = await factory(lifetime.Token).ConfigureAwait(false))
                        {
                            // The first real encoded bytes are required before a success response.
                            byte[] buffer = new byte[64 * 1024];
                            int count;
                            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                            {
                                startup.CancelAfter(20000);
                                using (startup.Token.Register(() => { if (startup.IsCancellationRequested) session.Dispose(); }))
                                    count = await session.Output.ReadAsync(buffer, 0, buffer.Length, startup.Token).ConfigureAwait(false);
                            }
                            if (count == 0) { await Error(network, 502, "Encoder produced no video", lifetime.Token).ConfigureAwait(false); return; }
                            await SendHeader(network, 200, "OK", null, false, null, lifetime.Token).ConfigureAwait(false);
                            active = true;
                            Interlocked.Increment(ref activeClients);
                            WriteLog("开始向设备传送视频（" + session.EncoderName + "）。");
                            do
                            {
                                await WriteData(network, buffer, count, lifetime.Token).ConfigureAwait(false);
                                Interlocked.Add(ref bytesSent, count);
                                count = await session.Output.ReadAsync(buffer, 0, buffer.Length, lifetime.Token).ConfigureAwait(false);
                            } while (count > 0);
                            WriteLog("视频编码进程已结束。");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException ex) { if (!lifetime.IsCancellationRequested) WriteLog("设备关闭视频连接：" + ex.Message); }
            catch (Exception ex) { if (!lifetime.IsCancellationRequested) WriteLog("视频发送失败：" + ex.Message); }
            finally
            {
                if (active) Interlocked.Decrement(ref activeClients);
                if (slot) slots.Release();
                TcpClient removed;
                clients.TryRemove(id, out removed);
                CloseQuietly(client);
            }
        }

        private async Task SendFile(NetworkStream network, Dictionary<string, string> headers, bool head, CancellationToken token)
        {
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                long start = 0, end = file.Length - 1;
                string range = null;
                bool partial = !head && headers.TryGetValue("Range", out range);
                if (partial && !TryRange(range, file.Length, out start, out end))
                {
                    await SendHeader(network, 416, "Range Not Satisfiable", 0, true, "bytes */" + file.Length, token).ConfigureAwait(false);
                    return;
                }
                long count = Math.Max(0, end - start + 1);
                await SendHeader(network, partial ? 206 : 200, partial ? "Partial Content" : "OK", count, true,
                    partial ? "bytes " + start + "-" + end + "/" + file.Length : null, token).ConfigureAwait(false);
                if (head) return;
                file.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[65536];
                while (count > 0)
                {
                    int read = await file.ReadAsync(buffer, 0, (int)Math.Min(count, buffer.Length), token).ConfigureAwait(false);
                    if (read == 0) break;
                    await WriteData(network, buffer, read, token).ConfigureAwait(false);
                    Interlocked.Add(ref bytesSent, read);
                    count -= read;
                }
            }
        }

        internal static bool TryRange(string range, long length, out long start, out long end)
        {
            start = 0; end = length - 1;
            if (length <= 0 || range == null || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
            string[] parts = range.Substring(6).Trim().Split('-');
            if (parts.Length != 2 || parts[0].Contains(",") || parts[1].Contains(",")) return false;
            long n;
            if (parts[0].Length == 0)
            {
                if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out n) || n <= 0) return false;
                start = Math.Max(0, length - n);
                return true;
            }
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start) || start >= length) return false;
            if (parts[1].Length > 0)
            {
                if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < start) return false;
                end = Math.Min(n, length - 1);
            }
            return true;
        }

        private async Task SendHeader(NetworkStream network, int status, string reason, long? length, bool seekable, string range, CancellationToken token)
        {
            var h = new StringBuilder("HTTP/1.0 " + status + " " + reason + "\r\n");
            h.Append("Server: GoggleCast/0.1 UPnP/1.0\r\nConnection: close\r\nCache-Control: no-store\r\n");
            h.Append("Content-Type: " + mime + "\r\n");
            h.Append("transferMode.dlna.org: Streaming\r\n");
            h.Append("contentFeatures.dlna.org: DLNA.ORG_OP=" + (seekable ? "01" : "00") + ";DLNA.ORG_CI=" + (seekable ? "0" : "1") + ";DLNA.ORG_FLAGS=01100000000000000000000000000000\r\n");
            h.Append("Accept-Ranges: " + (seekable ? "bytes" : "none") + "\r\n");
            if (length.HasValue) h.Append("Content-Length: " + length.Value.ToString(CultureInfo.InvariantCulture) + "\r\n");
            if (range != null) h.Append("Content-Range: " + range + "\r\n");
            h.Append("\r\n");
            byte[] bytes = Encoding.ASCII.GetBytes(h.ToString());
            await network.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
        }

        private static async Task Error(NetworkStream network, int status, string reason, CancellationToken token)
        {
            byte[] data = Encoding.ASCII.GetBytes("HTTP/1.0 " + status + " " + reason + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await network.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
        }

        private static async Task<string> ReadHeader(NetworkStream stream, CancellationToken token)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] one = new byte[1];
                int match = 0;
                byte[] delimiter = { 13, 10, 13, 10 };
                while (buffer.Length < 16384)
                {
                    int n = await stream.ReadAsync(one, 0, 1, token).ConfigureAwait(false);
                    if (n == 0) return null;
                    buffer.WriteByte(one[0]);
                    match = one[0] == delimiter[match] ? match + 1 : (one[0] == 13 ? 1 : 0);
                    if (match == 4) return Encoding.ASCII.GetString(buffer.ToArray());
                }
                throw new IOException("HTTP 请求头过长。");
            }
        }

        private static async Task WriteData(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            var write = stream.WriteAsync(buffer, 0, count, token);
            var timeout = Task.Delay(15000, token);
            if (await Task.WhenAny(write, timeout).ConfigureAwait(false) != write)
                throw new IOException("接收设备超过 15 秒没有继续读取，已结束本次连接。");
            await write.ConfigureAwait(false);
        }

        private void WriteLog(string message)
        {
            var handler = Log;
            if (handler != null) { try { handler(message); } catch { } }
        }
        private static void CloseQuietly(TcpClient client) { try { client.Close(); } catch { } }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            if (listener != null) listener.Stop();
            foreach (var pair in clients) CloseQuietly(pair.Value);
            // Tokens stay alive until outstanding reads and encoders have observed cancellation.
        }
    }
}
