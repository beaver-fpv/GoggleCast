using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using GoggleCast;

// Captured system audio and encoded screen frames never leave memory in this test.
internal static class DesktopAudioAcceptance
{
    private static string output;
    private static readonly List<string> results = new List<string>();

    [STAThread]
    private static int Main(string[] args)
    {
        output = args[0]; Directory.CreateDirectory(output);
        try
        {
            CheckLoopback().GetAwaiter().GetResult();
            if (args.Length > 1 && args[1] == "desktop")
                CheckDesktop(args.Length > 2 ? args[2] : "ts", args.Length > 3 ? args[3] : "auto").GetAwaiter().GetResult();
            Note("PASS: no captured audio/video files were written; only diagnostic metadata was saved.");
            File.WriteAllLines(Path.Combine(output, "acceptance-results.txt"), results);
            return 0;
        }
        catch (Exception error)
        {
            Note("FAIL: " + error);
            File.WriteAllLines(Path.Combine(output, "acceptance-results.txt"), results);
            return 1;
        }
    }

    private static async Task CheckLoopback()
    {
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            DesktopAudio audio = await DesktopAudio.StartAsync(cancellation.Token, Note);
            Stopwatch clock = Stopwatch.StartNew(); long bytes = 0;
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", audio.PipePath.Substring(9), PipeDirection.In, PipeOptions.Asynchronous))
                {
                    pipe.Connect(3000); byte[] buffer = new byte[65536];
                    while (clock.Elapsed.TotalSeconds < 4)
                    {
                        int count = await pipe.ReadAsync(buffer, 0, buffer.Length);
                        if (count == 0) throw new IOException("Loopback ended unexpectedly.");
                        bytes += count;
                    }
                    cancellation.Cancel(); audio.Dispose();
                }
            }
            finally { cancellation.Cancel(); audio.Dispose(); }
            if (audio.Failure != null) throw audio.Failure;
            double pcmSeconds = audio.WrittenFrames / (double)audio.SampleRate;
            if (pcmSeconds < 3.6 || pcmSeconds > clock.Elapsed.TotalSeconds + 0.25)
                throw new Exception("PCM sample clock is not advancing near real time: " + pcmSeconds);
            CheckStopped(audio);
            Note("Loopback: elapsed=" + clock.Elapsed.TotalSeconds.ToString("F3") + "s PCM=" + pcmSeconds.ToString("F3") +
                "s bytes=" + bytes + " captured=" + audio.CapturedFrames + " silence=" + audio.SilenceFrames + " trimmed=" + audio.TrimmedFrames);
        }
        DesktopAudio waiting = await DesktopAudio.StartAsync(CancellationToken.None, Note);
        Stopwatch disposal = Stopwatch.StartNew(); waiting.Dispose(); CheckStopped(waiting);
        if (disposal.ElapsedMilliseconds > 2000) throw new Exception("A pipe without a reader did not cancel promptly.");
        Note("PASS: default-device loopback, PCM cadence, cancellation and unconnected pipe disposal.");
    }

    private static async Task CheckDesktop(string container, string encoder)
    {
        string ffmpeg = FFmpegLocator.Find();
        MemoryStream memory = new MemoryStream();
        MediaSession session = await MediaSession.StartAsync(ffmpeg, new CaptureSettings
        {
            SourceKind = "desktop", Audio = true, X = 0, Y = 0, Width = 320, Height = 180,
            OutputWidth = 640, OutputHeight = 360, Container = container, Encoder = encoder
        }, CancellationToken.None, Note);
        DesktopAudio audio = (DesktopAudio)typeof(MediaSession).GetField("desktopAudio", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        int processId = ((Process)typeof(MediaSession).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session)).Id;
        try
        {
            Stopwatch clock = Stopwatch.StartNew(); byte[] buffer = new byte[32768];
            while (clock.Elapsed.TotalSeconds < 4)
            {
                int count = await session.Output.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0) throw new IOException("Desktop media ended unexpectedly.");
                memory.Write(buffer, 0, count);
            }
        }
        finally { session.Dispose(); }
        CheckStopped(audio);
        try { using (Process process = Process.GetProcessById(processId)) if (!process.HasExited) throw new Exception("FFmpeg child remains after disposal."); }
        catch (ArgumentException) { }
        string probeArgs = "-v error " + (container == "mp4" ? "-read_intervals %+3 -f mp4" : "-f mpegts") +
            " -i pipe:0 -show_packets -count_frames -show_entries packet=codec_type,pts_time,duration_time:stream=codec_name,codec_type,width,height,pix_fmt,nb_read_frames,start_time,sample_rate,channels -of json";
        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe"), probeArgs)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string json;
        using (Process probe = Process.Start(info))
        {
            Task<string> response = probe.StandardOutput.ReadToEndAsync();
            Task<string> error = probe.StandardError.ReadToEndAsync();
            byte[] encoded = memory.ToArray();
            int length = container == "ts" ? encoded.Length / 188 * 188 : encoded.Length;
            await probe.StandardInput.BaseStream.WriteAsync(encoded, 0, length);
            probe.StandardInput.Close(); probe.WaitForExit(); json = await response;
            string message = await error; if (message.Length > 0) Note("ffprobe: " + message);
            Array.Clear(encoded, 0, encoded.Length);
            if (probe.ExitCode != 0) throw new Exception("In-memory media decode failed.");
        }
        memory.Dispose();
        File.WriteAllText(Path.Combine(output, "timestamps-" + container + "-" + encoder + ".json"), json);
        JavaScriptSerializer serializer = new JavaScriptSerializer(); serializer.MaxJsonLength = 2000000;
        Dictionary<string, object> document = (Dictionary<string, object>)serializer.DeserializeObject(json);
        bool video = false, sound = false;
        foreach (object raw in (object[])document["streams"])
        {
            Dictionary<string, object> stream = (Dictionary<string, object>)raw;
            if ((string)stream["codec_type"] == "video") video = (string)stream["codec_name"] == "h264" && Convert.ToInt32(stream["nb_read_frames"]) > 50;
            if ((string)stream["codec_type"] == "audio") sound = (string)stream["codec_name"] == "aac" && Convert.ToInt32(stream["nb_read_frames"]) > 50;
        }
        if (!video || !sound) throw new Exception("Expected sustained H264 and AAC streams.");
        if (container == "ts")
        {
            double lastVideo = 0, lastAudio = 0;
            foreach (object raw in (object[])document["packets"])
            {
                Dictionary<string, object> packet = (Dictionary<string, object>)raw;
                if (!packet.ContainsKey("pts_time")) continue;
                double pts = Double.Parse((string)packet["pts_time"], CultureInfo.InvariantCulture);
                if ((string)packet["codec_type"] == "video") lastVideo = pts;
                else lastAudio = pts;
            }
            if (Math.Abs(lastVideo - lastAudio) > 0.25) throw new Exception("Unexpected end-of-capture A/V timestamp gap.");
            Note("End PTS: video=" + lastVideo + " audio=" + lastAudio + " difference=" + Math.Abs(lastVideo - lastAudio));
        }
        Note("PASS: desktop + WASAPI -> " + encoder + "/" + container + "; H264/AAC decoded in memory; capture and encoder stopped.");
    }

    private static void CheckStopped(DesktopAudio audio)
    {
        Thread thread = (Thread)typeof(DesktopAudio).GetField("captureThread", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(audio);
        Task writer = (Task)typeof(DesktopAudio).GetField("writer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(audio);
        if (thread.IsAlive || !writer.IsCompleted) throw new Exception("Audio capture thread or pipe writer remains active.");
    }
    private static void Note(string text) { results.Add(text); Console.WriteLine(text); }
}
