using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GoggleCast;

// Real loopback HTTP requests; never discovers or controls a physical receiver.
public static class PlayPreparationMock
{
    public static string Run() { return RunAsync().GetAwaiter().GetResult(); }

    private static async Task<string> RunAsync()
    {
        long preparationMs;
        using (var slow = new SlowRenderer(9000))
        {
            var clock = Stopwatch.StartNew();
            await new DlnaClient(slow.Device).PlayAsync(CancellationToken.None);
            preparationMs = clock.ElapsedMilliseconds;
            if (preparationMs < 8500 || preparationMs > 20000)
                throw new Exception("Unexpected successful preparation duration: " + preparationMs);
            await slow.Completion;
        }

        long cancelMs;
        using (var slow = new SlowRenderer(9000))
        using (var cancellation = new CancellationTokenSource())
        {
            Task play = new DlnaClient(slow.Device).PlayAsync(cancellation.Token);
            if (await Task.WhenAny(slow.RequestSeen, Task.Delay(3000)) != slow.RequestSeen)
                throw new Exception("Cancellation test did not reach the renderer.");
            await slow.RequestSeen;
            await Task.Delay(250);
            var clock = Stopwatch.StartNew();
            cancellation.Cancel();
            bool cancelled = false;
            try { await play; }
            catch (OperationCanceledException) { cancelled = true; }
            cancelMs = clock.ElapsedMilliseconds;
            if (!cancelled || cancelMs > 2000)
                throw new Exception("Play preparation cancellation was not prompt: " + cancelMs + " ms");
        }
        return "PASS: Play tolerates 9-second receiver preparation (" + preparationMs +
            " ms); user cancellation during preparation exits promptly (" + cancelMs + " ms).";
    }

    private sealed class SlowRenderer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> requestSeen = new TaskCompletionSource<bool>();
        private readonly int delayMs;
        internal DlnaDevice Device;
        internal Task Completion;
        internal Task RequestSeen { get { return requestSeen.Task; } }

        internal SlowRenderer(int delayMs)
        {
            this.delayMs = delayMs;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Device = new DlnaDevice {
                Name = "Slow local Play receiver", ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
                ControlUri = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/transport")
            };
            Completion = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (lifetime.Token.Register(() => client.Close()))
                using (NetworkStream stream = client.GetStream())
                {
                    var header = new StringBuilder();
                    byte[] one = new byte[1];
                    while (header.Length < 16384)
                    {
                        if (await stream.ReadAsync(one, 0, 1, lifetime.Token) == 0) throw new IOException("Truncated request.");
                        header.Append((char)one[0]);
                        if (header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
                    }
                    string headers = header.ToString();
                    if (headers.IndexOf("#Play\"", StringComparison.Ordinal) < 0) throw new Exception("Expected SOAP Play.");
                    int length = 0;
                    foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = Int32.Parse(line.Substring(15).Trim());
                    if (length <= 0 || length > 1048576) throw new IOException("Invalid request length.");
                    byte[] body = new byte[length];
                    int offset = 0;
                    while (offset < body.Length)
                    {
                        int count = await stream.ReadAsync(body, offset, body.Length - offset, lifetime.Token);
                        if (count == 0) throw new IOException("Truncated request body.");
                        offset += count;
                    }
                    requestSeen.TrySetResult(true);
                    await Task.Delay(delayMs, lifetime.Token);
                    byte[] reply = Encoding.UTF8.GetBytes("<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body>" +
                        "<u:PlayResponse xmlns:u='urn:schemas-upnp-org:service:AVTransport:1'/></s:Body></s:Envelope>");
                    byte[] replyHeader = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/xml; charset=utf-8\r\nContent-Length: " +
                        reply.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(replyHeader, 0, replyHeader.Length, lifetime.Token);
                    await stream.WriteAsync(reply, 0, reply.Length, lifetime.Token);
                }
            }
            catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) throw; }
            catch (ObjectDisposedException) { if (!lifetime.IsCancellationRequested) throw; }
            catch (IOException) { if (!lifetime.IsCancellationRequested) throw; }
            catch (SocketException) { if (!lifetime.IsCancellationRequested) throw; }
        }

        public void Dispose()
        {
            lifetime.Cancel();
            listener.Stop();
            if (!Completion.Wait(2000)) throw new Exception("Slow renderer failed to stop.");
            lifetime.Dispose();
        }
    }
}
