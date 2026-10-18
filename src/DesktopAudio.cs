using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace GoggleCast
{
    // Windows shared-mode WASAPI loopback. No recording file, virtual driver,
    // microphone access, volume changes, or default-device changes are involved.
    // See Microsoft Learn: Loopback Recording; IAudioCaptureClient::GetBuffer;
    // IAudioClient::GetMixFormat and IAudioClient::Initialize.
    public sealed class DesktopAudio : IDisposable
    {
        private readonly NamedPipeServerStream pipe;
        private readonly BlockingCollection<byte[]> packets = new BlockingCollection<byte[]>(200);
        private readonly CancellationTokenSource lifetime;
        private readonly CancellationTokenRegistration pipeCancellation;
        private readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<string> log;
        private readonly Thread captureThread;
        private readonly Task writer;
        private long originQpc;
        private long timelineFrames, writtenFrames, capturedFrames, silenceFrames, trimmedFrames, discontinuities;
        private int disposed;
        private Exception failure;
        private int blockAlign;
        private byte silenceByte;
        private bool timestampWarning;

        public string PipePath { get; private set; }
        public string PcmFormat { get; private set; }
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public double StartUnixSeconds { get; private set; }
        public double BufferMilliseconds { get; private set; }
        public long WrittenFrames { get { return Interlocked.Read(ref writtenFrames); } }
        public long CapturedFrames { get { return Interlocked.Read(ref capturedFrames); } }
        public long SilenceFrames { get { return Interlocked.Read(ref silenceFrames); } }
        public long TrimmedFrames { get { return Interlocked.Read(ref trimmedFrames); } }
        public long Discontinuities { get { return Interlocked.Read(ref discontinuities); } }
        public Exception Failure { get { return Volatile.Read(ref failure); } }
        public event Action<Exception> Failed;

        private DesktopAudio(CancellationToken token, Action<string> log)
        {
            this.log = log;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            string name = "GoggleCast-Audio-" + Guid.NewGuid().ToString("N");
            PipePath = @"\\.\pipe\" + name;
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 65536, security);
            pipeCancellation = lifetime.Token.Register(delegate { try { pipe.Dispose(); } catch { } });
            writer = Task.Run((Func<Task>)WritePipeAsync);
            captureThread = new Thread(CaptureLoop);
            captureThread.Name = "GoggleCast WASAPI loopback";
            captureThread.IsBackground = true;
            captureThread.SetApartmentState(ApartmentState.STA);
            captureThread.Start();
        }

        public static async Task<DesktopAudio> StartAsync(CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            DesktopAudio audio = new DesktopAudio(token, log);
            try
            {
                Task timeout = Task.Delay(8000, token);
                Task completed = await Task.WhenAny(audio.ready.Task, timeout).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (completed != audio.ready.Task) throw new TimeoutException("系统声音初始化超过 8 秒，请检查默认播放设备。");
                await audio.ready.Task.ConfigureAwait(false);
                return audio;
            }
            catch { audio.Dispose(); throw; }
        }

        private void CaptureLoop()
        {
            object enumeratorObject = null, clientObject = null, captureObject = null;
            IMMDevice device = null;
            IAudioClient client = null;
            IAudioCaptureClient capture = null;
            IntPtr format = IntPtr.Zero;
            bool comInitialized = false, started = false;
            try
            {
                int comResult = CoInitializeEx(IntPtr.Zero, 2);
                Check(comResult, "初始化音频 COM"); comInitialized = true;
                lifetime.Token.ThrowIfCancellationRequested();
                enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)enumeratorObject;
                Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device), "读取默认播放设备");
                Guid clientId = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
                Check(device.Activate(ref clientId, 23, IntPtr.Zero, out clientObject), "打开默认播放设备");
                client = (IAudioClient)clientObject;
                Check(client.GetMixFormat(out format), "读取系统混音格式");
                ParseFormat(format);
                Check(client.Initialize(0, 0x00020000, 1000000, 0, format, IntPtr.Zero), "初始化共享模式系统声音采集");
                uint bufferFrames; Check(client.GetBufferSize(out bufferFrames), "读取声音缓冲大小");
                BufferMilliseconds = bufferFrames * 1000.0 / SampleRate;
                Guid captureId = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
                Check(client.GetService(ref captureId, out captureObject), "读取系统声音采集接口");
                capture = (IAudioCaptureClient)captureObject;

                // gdigrab timestamps use the system wall clock. Pair it with QPC once;
                // all sample positions thereafter use QPC rather than pipe arrival time.
                long before = Stopwatch.GetTimestamp();
                DateTime utc = DateTime.UtcNow;
                long after = Stopwatch.GetTimestamp();
                originQpc = before + (after - before) / 2;
                StartUnixSeconds = (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                Check(client.Start(), "开始系统声音采集"); started = true;
                WriteLog("系统声音已连接默认播放设备：" + SampleRate + " Hz / " + Channels + " 声道 / " + PcmFormat + "；缓冲 " + BufferMilliseconds.ToString("0.#", CultureInfo.InvariantCulture) + " ms。音视频使用共同时间起点。");
                ready.TrySetResult(true);
                while (!lifetime.IsCancellationRequested)
                {
                    uint next; Check(capture.GetNextPacketSize(out next), "读取系统声音数据");
                    while (next != 0 && !lifetime.IsCancellationRequested)
                    {
                        IntPtr data; uint count, flags; ulong devicePosition, qpcPosition;
                        Check(capture.GetBuffer(out data, out count, out flags, out devicePosition, out qpcPosition), "获取系统声音缓冲");
                        try { AcceptPacket(data, count, flags, qpcPosition); }
                        finally { Check(capture.ReleaseBuffer(count), "释放系统声音缓冲"); }
                        Check(capture.GetNextPacketSize(out next), "读取下一段系统声音");
                    }
                    // When the Windows mixer is idle it may provide no packets. A 100 ms
                    // safety margin avoids replacing real packets that have not arrived yet.
                    long safeSilenceEnd = (long)(((Stopwatch.GetTimestamp() - originQpc) / (double)Stopwatch.Frequency - 0.100) * SampleRate);
                    if (safeSilenceEnd > timelineFrames) EnqueueSilence(safeSilenceEnd - timelineFrames);
                    lifetime.Token.WaitHandle.WaitOne(10);
                }
            }
            catch (OperationCanceledException) { ready.TrySetCanceled(); }
            catch (Exception error)
            {
                if (!lifetime.IsCancellationRequested)
                {
                    IOException detailed = new IOException("系统声音采集失败：" + error.Message + "。请检查默认播放设备，或关闭「系统声音」后重试。", error);
                    ready.TrySetException(detailed); Fail(detailed);
                }
                else ready.TrySetCanceled();
            }
            finally
            {
                if (started && client != null) { try { client.Stop(); } catch { } }
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                Release(captureObject); Release(clientObject); Release(device); Release(enumeratorObject);
                if (comInitialized) CoUninitialize();
                try { packets.CompleteAdding(); } catch (ObjectDisposedException) { }
            }
        }

        private void ParseFormat(IntPtr format)
        {
            int tag = (ushort)Marshal.ReadInt16(format, 0);
            Channels = (ushort)Marshal.ReadInt16(format, 2);
            SampleRate = Marshal.ReadInt32(format, 4);
            blockAlign = (ushort)Marshal.ReadInt16(format, 12);
            int bits = (ushort)Marshal.ReadInt16(format, 14);
            if (tag == 0xFFFE)
            {
                if ((ushort)Marshal.ReadInt16(format, 16) < 22) throw new NotSupportedException("系统混音扩展格式不完整");
                Guid subtype = (Guid)Marshal.PtrToStructure(IntPtr.Add(format, 24), typeof(Guid));
                if (subtype == new Guid("00000003-0000-0010-8000-00AA00389B71")) tag = 3;
                else if (subtype == new Guid("00000001-0000-0010-8000-00AA00389B71")) tag = 1;
                else throw new NotSupportedException("系统混音不是可用的 PCM 或浮点格式");
            }
            if (Channels < 1 || Channels > 32 || SampleRate < 8000 || SampleRate > 384000 || blockAlign != Channels * bits / 8)
                throw new NotSupportedException("系统混音的采样率、声道或帧长度不受支持");
            if (tag == 3 && bits == 32) PcmFormat = "f32le";
            else if (tag == 3 && bits == 64) PcmFormat = "f64le";
            else if (tag == 1 && (bits == 16 || bits == 24 || bits == 32)) PcmFormat = "s" + bits + "le";
            else if (tag == 1 && bits == 8) { PcmFormat = "u8"; silenceByte = 128; }
            else throw new NotSupportedException("系统混音的位深不受支持：" + bits);
        }

        private void AcceptPacket(IntPtr data, uint count, uint flags, ulong qpcPosition)
        {
            if (count == 0) return;
            long target;
            if ((flags & 4) == 0 && qpcPosition != 0)
                target = (long)Math.Round((qpcPosition / 10000000.0 - originQpc / (double)Stopwatch.Frequency) * SampleRate);
            else
            {
                target = (long)Math.Round((Stopwatch.GetTimestamp() - originQpc) * SampleRate / (double)Stopwatch.Frequency) - count;
                if (!timestampWarning) { timestampWarning = true; WriteLog("音频设备报告时间戳异常；当前包按采集时刻估算，同步精度可能降低。"); }
            }
            if ((flags & 1) != 0) Interlocked.Increment(ref discontinuities);
            if (target > timelineFrames) EnqueueSilence(target - timelineFrames);
            long overlap = Math.Max(0, timelineFrames - target);
            int skip = (int)Math.Min((long)count, overlap);
            Interlocked.Add(ref trimmedFrames, skip);
            int remaining = (int)count - skip;
            if (remaining == 0) return;
            byte[] bytes = new byte[checked(remaining * blockAlign)];
            if ((flags & 2) != 0 || data == IntPtr.Zero)
            {
                if (silenceByte != 0) for (int i = 0; i < bytes.Length; i++) bytes[i] = silenceByte;
                Interlocked.Add(ref silenceFrames, remaining);
            }
            else { Marshal.Copy(IntPtr.Add(data, checked(skip * blockAlign)), bytes, 0, bytes.Length); Interlocked.Add(ref capturedFrames, remaining); }
            Enqueue(bytes); timelineFrames += remaining;
        }

        private void EnqueueSilence(long frames)
        {
            if (frames > SampleRate * 5L) throw new IOException("系统声音时钟跳变超过 5 秒，请停止后重新开始投屏");
            int packetFrames = Math.Max(1, SampleRate / 100);
            while (frames > 0)
            {
                int count = (int)Math.Min(frames, packetFrames);
                byte[] bytes = new byte[count * blockAlign];
                if (silenceByte != 0) for (int i = 0; i < bytes.Length; i++) bytes[i] = silenceByte;
                Enqueue(bytes); timelineFrames += count; frames -= count;
                Interlocked.Add(ref silenceFrames, count);
            }
        }

        private void Enqueue(byte[] bytes)
        {
            if (!packets.TryAdd(bytes, 200, lifetime.Token)) throw new IOException("声音编码未及时读取数据，采集队列已满");
        }

        private async Task WritePipeAsync()
        {
            try
            {
                await Task.Factory.FromAsync(pipe.BeginWaitForConnection, pipe.EndWaitForConnection, null).ConfigureAwait(false);
                foreach (byte[] packet in packets.GetConsumingEnumerable(lifetime.Token))
                {
                    await pipe.WriteAsync(packet, 0, packet.Length, lifetime.Token).ConfigureAwait(false);
                    Interlocked.Add(ref writtenFrames, packet.Length / blockAlign);
                }
            }
            catch (Exception error)
            {
                if (!lifetime.IsCancellationRequested) Fail(new IOException("系统声音管道已断开：" + error.Message, error));
            }
        }

        private void Fail(Exception error)
        {
            if (Interlocked.CompareExchange(ref failure, error, null) != null) return;
            WriteLog(error.Message);
            try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
            Action<Exception> callback = Failed;
            if (callback != null) try { callback(error); } catch { }
        }
        private void WriteLog(string message) { if (log != null) try { log(message); } catch { } }
        private static void Check(int result, string operation)
        {
            if (result < 0) throw new COMException(operation + "（0x" + result.ToString("X8", CultureInfo.InvariantCulture) + "）", result);
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) try { Marshal.FinalReleaseComObject(value); } catch { } }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
            try { pipe.Dispose(); } catch { }
            if (Thread.CurrentThread != captureThread && captureThread.IsAlive) captureThread.Join(1500);
            // A failure callback can run inside writer itself; never wait on the current task.
            if (Task.CurrentId != writer.Id) try { writer.Wait(1500); } catch (AggregateException) { }
            pipeCancellation.Dispose();
            if (!captureThread.IsAlive && writer.IsCompleted) { packets.Dispose(); lifetime.Dispose(); }
        }

        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint concurrency);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
            [PreserveSig] int GetId(out IntPtr id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int mode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
