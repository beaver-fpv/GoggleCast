using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GoggleCast
{
    public class CaptureSettings
    {
        public string SourceKind = "desktop";
        public string FilePath;
        public int X, Y, Width, Height;
        public int OutputWidth = 1920, OutputHeight = 1080, Fps = 30, BitrateKbps = 6000;
        public string Encoder = "auto", Container = "ts";
        // For desktop sources, capture the default render endpoint with WASAPI when true.
        // Files retain their existing audio regardless of this desktop-only option.
        public bool Audio = false;
    }

    public static class FFmpegLocator
    {
        public static string Find()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(directory, "runtime", "ffmpeg.exe"));
            candidates.Add(Path.Combine(directory, "ffmpeg.exe"));
            string configured = Environment.GetEnvironmentVariable("GOGGLECAST_FFMPEG");
            if (!String.IsNullOrWhiteSpace(configured)) candidates.Add(configured.Trim().Trim('"'));
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string entry in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (!String.IsNullOrWhiteSpace(entry))
                        candidates.Add(Path.Combine(entry.Trim().Trim('"'), "ffmpeg.exe"));
                }
                catch (ArgumentException) { }
            }
            foreach (string candidate in candidates)
            {
                try { if (File.Exists(candidate)) return Path.GetFullPath(candidate); }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            return null;
        }
    }

    public sealed class MediaSession : IDisposable
    {
        private static readonly object EncoderGate = new object();
        private static readonly Dictionary<string, Task<bool>> EncoderProbes = new Dictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);
        private readonly Process process;
        private readonly Action<string> initialLog;
        private readonly object errorGate = new object();
        private readonly StringBuilder errors = new StringBuilder();
        private Stream output;
        private DesktopAudio desktopAudio;
        private CancellationTokenRegistration cancellation;
        private int disposed;

        public Stream Output { get { return output; } }
        public string EncoderName { get; private set; }
        public event Action<string> Log;

        private MediaSession(Process process, string encoder, Action<string> log)
        {
            this.process = process;
            EncoderName = encoder;
            initialLog = log;
        }

        public static async Task<MediaSession> StartAsync(string ffmpeg, CaptureSettings options, CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
                throw new FileNotFoundException("没有找到 FFmpeg。请把 ffmpeg.exe 放入程序旁的 runtime 文件夹。", ffmpeg);
            CaptureSettings settings = ValidateAndCopy(options);
            string encoder = settings.Encoder;
            bool requestedHardware = encoder == "auto" || encoder == "nvenc" || encoder == "h264_nvenc";
            if (requestedHardware)
            {
                bool available = await AwaitProbeAsync(GetEncoderProbe(ffmpeg), token).ConfigureAwait(false);
                encoder = available ? "h264_nvenc" : "libx264";
                if (!available) SafeLog(log, "NVIDIA 硬件编码不可用，已切换为 CPU 编码。首次探测结果会缓存。 ");
            }
            else encoder = "libx264";

            if (settings.SourceKind == "desktop" && !settings.Audio)
                SafeLog(log, "桌面采集未启用系统声音，输出静音音轨以兼容接收设备。");
            Exception hardwareFailure;
            try
            {
                return await StartProcessAsync(ffmpeg, settings, encoder, token, log).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { throw; }
            catch (Exception first)
            {
                if (encoder != "h264_nvenc" || token.IsCancellationRequested) throw;
                hardwareFailure = first;
            }
            SafeLog(log, "硬件编码启动失败，尝试 CPU 编码。原因：" + hardwareFailure.Message);
            try
            {
                return await StartProcessAsync(ffmpeg, settings, "libx264", token, log).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception second)
            {
                throw new InvalidOperationException("硬件与 CPU 编码均未能启动。CPU 编码详情：" + second.Message, second);
            }
        }

        private static CaptureSettings ValidateAndCopy(CaptureSettings options)
        {
            if (options == null) throw new ArgumentNullException("options");
            CaptureSettings settings = new CaptureSettings
            {
                SourceKind = (options.SourceKind ?? "desktop").ToLowerInvariant(),
                FilePath = options.FilePath, X = options.X, Y = options.Y, Width = options.Width, Height = options.Height,
                OutputWidth = options.OutputWidth, OutputHeight = options.OutputHeight, Fps = options.Fps,
                BitrateKbps = options.BitrateKbps, Encoder = (options.Encoder ?? "auto").ToLowerInvariant(),
                Container = (options.Container ?? "ts").ToLowerInvariant(), Audio = options.Audio
            };
            if (settings.SourceKind != "desktop" && settings.SourceKind != "file" && settings.SourceKind != "test")
                throw new ArgumentException("不支持的画面来源：" + settings.SourceKind);
            if (settings.Container != "ts" && settings.Container != "mp4")
                throw new ArgumentException("输出格式只能是 ts 或 mp4。");
            if (settings.Encoder != "auto" && settings.Encoder != "nvenc" && settings.Encoder != "h264_nvenc" &&
                settings.Encoder != "x264" && settings.Encoder != "libx264" && settings.Encoder != "cpu")
                throw new ArgumentException("编码器只能选择 auto、nvenc 或 x264。");
            if (settings.Fps < 1 || settings.Fps > 60) throw new ArgumentException("帧率应在 1–60 之间。");
            if (settings.OutputWidth < 160 || settings.OutputHeight < 90 || settings.OutputWidth > 3840 || settings.OutputHeight > 2160 ||
                settings.OutputWidth % 2 != 0 || settings.OutputHeight % 2 != 0)
                throw new ArgumentException("输出尺寸应为偶数，范围在 160×90 至 3840×2160 之间。");
            if (settings.BitrateKbps < 250 || settings.BitrateKbps > 50000) throw new ArgumentException("码率应在 250–50000 Kbps 之间。");
            if (settings.Width < 0 || settings.Height < 0 || (settings.Width == 0) != (settings.Height == 0))
                throw new ArgumentException("屏幕采集宽高必须同时指定为正数，或同时为 0 使用默认屏幕。");
            if (settings.SourceKind == "file")
            {
                if (String.IsNullOrWhiteSpace(settings.FilePath) || !File.Exists(settings.FilePath))
                    throw new FileNotFoundException("所选视频文件不存在。", settings.FilePath);
                settings.FilePath = Path.GetFullPath(settings.FilePath);
            }
            return settings;
        }

        private static Task<bool> GetEncoderProbe(string ffmpeg)
        {
            string key = Path.GetFullPath(ffmpeg) + "|" + File.GetLastWriteTimeUtc(ffmpeg).Ticks.ToString(CultureInfo.InvariantCulture);
            lock (EncoderGate)
            {
                Task<bool> task;
                if (!EncoderProbes.TryGetValue(key, out task))
                {
                    task = Task.Run(() => ProbeEncoder(ffmpeg));
                    EncoderProbes[key] = task;
                }
                return task;
            }
        }

        private static async Task<bool> AwaitProbeAsync(Task<bool> probe, CancellationToken token)
        {
            using (CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task cancelled = Task.Delay(Timeout.Infinite, wait.Token);
                await Task.WhenAny(probe, cancelled).ConfigureAwait(false);
                wait.Cancel();
                token.ThrowIfCancellationRequested();
                return await probe.ConfigureAwait(false);
            }
        }

        private static bool ProbeEncoder(string ffmpeg)
        {
            using (Process probe = new Process())
            {
                probe.StartInfo = MakeStartInfo(ffmpeg, JoinArguments(new string[] {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=1",
                    "-frames:v", "1", "-c:v", "h264_nvenc", "-preset", "p4", "-tune", "ll", "-pix_fmt", "yuv420p", "-f", "null", "-"
                }));
                probe.ErrorDataReceived += (sender, e) => { };
                probe.OutputDataReceived += (sender, e) => { };
                try
                {
                    if (!probe.Start()) return false;
                    probe.BeginErrorReadLine();
                    probe.BeginOutputReadLine();
                    if (!probe.WaitForExit(6000)) { TryKill(probe); return false; }
                    probe.WaitForExit();
                    return probe.ExitCode == 0;
                }
                catch { TryKill(probe); return false; }
            }
        }

        private static async Task<MediaSession> StartProcessAsync(string ffmpeg, CaptureSettings settings, string encoder, CancellationToken token, Action<string> log)
        {
            Process process = new Process();
            MediaSession session = new MediaSession(process, encoder, log);
            process.ErrorDataReceived += session.OnError;
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) =>
            {
                if (Volatile.Read(ref session.disposed) == 0)
                {
                    try { session.WriteLog("编码进程已退出（代码 " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "）。"); }
                    catch (InvalidOperationException) { }
                }
            };
            try
            {
                token.ThrowIfCancellationRequested();
                if (settings.SourceKind == "desktop" && settings.Audio)
                {
                    session.desktopAudio = await DesktopAudio.StartAsync(token, session.WriteLog).ConfigureAwait(false);
                    session.desktopAudio.Failed += session.OnAudioFailed;
                    if (session.desktopAudio.Failure != null) throw session.desktopAudio.Failure;
                }
                process.StartInfo = MakeStartInfo(ffmpeg, BuildArguments(settings, encoder, session.desktopAudio));
                token.ThrowIfCancellationRequested();
                if (!process.Start()) throw new InvalidOperationException("无法启动 FFmpeg。");
                session.output = process.StandardOutput.BaseStream;
                process.BeginErrorReadLine();
                session.cancellation = token.Register(session.Dispose);
                byte[] prefix = new byte[4096];
                Task<int> read = session.output.ReadAsync(prefix, 0, prefix.Length);
                Task timeout = Task.Delay(12000, token);
                Task completed = await Task.WhenAny(read, timeout).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (completed != read) throw new TimeoutException("编码器 12 秒内未产生视频，请检查文件、屏幕或显卡状态。" + session.GetErrors());
                int count = await read.ConfigureAwait(false);
                if (count == 0)
                {
                    // Wait briefly for the asynchronously drained error output, without holding up the UI.
                    await Task.Run(() => { try { process.WaitForExit(500); } catch (InvalidOperationException) { } }).ConfigureAwait(false);
                    string details = session.GetErrors();
                    if (settings.SourceKind == "desktop" && details.IndexOf("Failed to capture image", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new IOException("Windows 拒绝读取当前桌面。请在已登录且解锁的桌面中正常启动程序；受限会话或锁屏可能无法采集。" + details);
                    throw new InvalidOperationException("编码器没有产生视频。" + details);
                }
                token.ThrowIfCancellationRequested();
                session.output = new PrefixReadStream(prefix, count, session.output);
                session.WriteLog("正在输出 H.264 / " + (settings.Container == "mp4" ? "fragmented MP4" : "MPEG-TS") + "，编码器：" + encoder + "。");
                return session;
            }
            catch
            {
                session.Dispose();
                token.ThrowIfCancellationRequested();
                throw;
            }
        }

        private static string BuildArguments(CaptureSettings settings, string encoder, DesktopAudio audio)
        {
            List<string> args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats", "-nostdin", "-fflags", "+genpts" };
            if (audio != null) args.Add("-copyts");
            string fps = settings.Fps.ToString(CultureInfo.InvariantCulture);
            if (settings.SourceKind == "desktop")
            {
                args.AddRange(new string[] { "-thread_queue_size", "512", "-f", "gdigrab", "-framerate", fps, "-draw_mouse", "1" });
                if (settings.Width > 0)
                    args.AddRange(new string[] { "-offset_x", settings.X.ToString(CultureInfo.InvariantCulture), "-offset_y", settings.Y.ToString(CultureInfo.InvariantCulture),
                        "-video_size", settings.Width.ToString(CultureInfo.InvariantCulture) + "x" + settings.Height.ToString(CultureInfo.InvariantCulture) });
                args.AddRange(new string[] { "-i", "desktop" });
                if (audio == null)
                    args.AddRange(new string[] { "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000" });
                else
                    args.AddRange(new string[] { "-thread_queue_size", "512", "-f", audio.PcmFormat, "-ar", audio.SampleRate.ToString(CultureInfo.InvariantCulture),
                        "-ac", audio.Channels.ToString(CultureInfo.InvariantCulture), "-i", audio.PipePath });
            }
            else if (settings.SourceKind == "file")
                args.AddRange(new string[] { "-re", "-stream_loop", "-1", "-i", settings.FilePath });
            else
                args.AddRange(new string[] { "-re", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=" + fps,
                    "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000" });

            args.AddRange(new string[] { "-map", "0:v:0", "-map", settings.SourceKind == "file" ? "0:a:0?" : "1:a:0", "-sn", "-dn" });
            string width = settings.OutputWidth.ToString(CultureInfo.InvariantCulture);
            string height = settings.OutputHeight.ToString(CultureInfo.InvariantCulture);
            string videoFilter = "scale=" + width + ":" + height + ":force_original_aspect_ratio=decrease:force_divisible_by=2,pad=" + width + ":" + height + ":(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p";
            if (audio != null)
                videoFilter = "setpts=PTS-" + audio.StartUnixSeconds.ToString("F6", CultureInfo.InvariantCulture) + "/TB," + videoFilter + ",fps=" + fps;
            args.AddRange(new string[] { "-vf", videoFilter });
            if (audio == null) args.AddRange(new string[] { "-r", fps });
            args.AddRange(new string[] { "-fps_mode", audio == null ? "cfr" : "passthrough", "-c:v", encoder, "-pix_fmt", "yuv420p", "-profile:v", "high", "-g", fps, "-bf", "0" });
            if (encoder == "h264_nvenc")
                args.AddRange(new string[] { "-preset", "p4", "-tune", "ll", "-rc", "cbr", "-zerolatency", "1", "-forced-idr", "1" });
            else
                args.AddRange(new string[] { "-preset", "veryfast", "-tune", "zerolatency", "-sc_threshold", "0", "-keyint_min", fps });
            string bitrate = settings.BitrateKbps.ToString(CultureInfo.InvariantCulture) + "k";
            args.AddRange(new string[] { "-b:v", bitrate, "-maxrate", bitrate, "-bufsize", bitrate, "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-ac", "2",
                "-af", "aresample=async=1:first_pts=0", "-max_interleave_delta", "1000000", "-flush_packets", "1" });
            if (settings.Container == "mp4")
                args.AddRange(new string[] { "-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-frag_duration", "1000000", "-f", "mp4", "pipe:1" });
            else
                args.AddRange(new string[] { "-mpegts_flags", "+resend_headers", "-muxdelay", "0", "-muxpreload", "0", "-f", "mpegts", "pipe:1" });
            return JoinArguments(args);
        }

        private static ProcessStartInfo MakeStartInfo(string ffmpeg, string arguments)
        {
            return new ProcessStartInfo(ffmpeg, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(ffmpeg))
            };
        }

        // CommandLineToArgvW / Microsoft C runtime quoting, including embedded quotes and trailing backslashes.
        private static string JoinArguments(IEnumerable<string> arguments)
        {
            StringBuilder result = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length != 0) result.Append(' ');
                result.Append('"');
                int backslashes = 0;
                foreach (char character in argument ?? "")
                {
                    if (character == '\\') { backslashes++; continue; }
                    if (character == '"')
                    {
                        result.Append('\\', backslashes * 2 + 1);
                        result.Append('"');
                    }
                    else
                    {
                        result.Append('\\', backslashes);
                        result.Append(character);
                    }
                    backslashes = 0;
                }
                result.Append('\\', backslashes * 2);
                result.Append('"');
            }
            return result.ToString();
        }

        private void OnError(object sender, DataReceivedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(e.Data)) return;
            lock (errorGate)
            {
                errors.AppendLine(e.Data);
                if (errors.Length > 12000) errors.Remove(0, errors.Length - 10000);
            }
            WriteLog("FFmpeg: " + e.Data);
        }

        private void OnAudioFailed(Exception error)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            WriteLog("系统声音已中断，结束本次媒体流：" + error.Message);
            // Release COM on its capture thread and avoid waiting on a callback's own task.
            Task.Run((Action)Dispose);
        }

        private string GetErrors()
        {
            lock (errorGate) return errors.Length == 0 ? "" : Environment.NewLine + errors.ToString().Trim();
        }

        private void WriteLog(string message)
        {
            SafeLog(initialLog, message);
            Action<string> callback = Log;
            SafeLog(callback, message);
        }

        private static void SafeLog(Action<string> callback, string message)
        {
            if (callback == null) return;
            try { callback(message); } catch { }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(1000); }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (desktopAudio != null)
            {
                desktopAudio.Failed -= OnAudioFailed;
                desktopAudio.Dispose();
            }
            TryKill(process);
            try { if (output != null) output.Dispose(); } catch (IOException) { }
            cancellation.Dispose();
            try { process.Dispose(); } catch (InvalidOperationException) { }
        }

        private sealed class PrefixReadStream : Stream
        {
            private readonly byte[] prefix;
            private readonly int prefixLength;
            private readonly Stream inner;
            private int position;

            public PrefixReadStream(byte[] prefix, int count, Stream inner)
            { this.prefix = prefix; prefixLength = count; this.inner = inner; }
            public override bool CanRead { get { return inner.CanRead; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count)
            {
                ValidateBuffer(buffer, offset, count);
                if (count == 0) return 0;
                if (position < prefixLength) return ReadPrefix(buffer, offset, count);
                return inner.Read(buffer, offset, count);
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ValidateBuffer(buffer, offset, count);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0) return Task.FromResult(0);
                if (position < prefixLength) return Task.FromResult(ReadPrefix(buffer, offset, count));
                return inner.ReadAsync(buffer, offset, count, cancellationToken);
            }
            private int ReadPrefix(byte[] buffer, int offset, int count)
            {
                int copied = Math.Min(count, prefixLength - position);
                Buffer.BlockCopy(prefix, position, buffer, offset, copied);
                position += copied;
                return copied;
            }
            private static void ValidateBuffer(byte[] buffer, int offset, int count)
            {
                if (buffer == null) throw new ArgumentNullException("buffer");
                if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
