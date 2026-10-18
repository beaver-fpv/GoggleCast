using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using GoggleCast;

// Drives the real WPF controls and real encoder. The only receiver is a local mock.
internal static class UiFlowCheck
{
    private static readonly List<string> results = new List<string>();
    private static readonly string output = AppDomain.CurrentDomain.BaseDirectory;
    private static MainWindow window;
    private static Grid root;
    private static Application application;
    private static LocalRenderer renderer;
    private static int resultCode = 1;
    private static bool closed;

    [STAThread]
    private static int Main()
    {
        try
        {
            application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            application.DispatcherUnhandledException += (sender, args) =>
            {
                Note("UNHANDLED: " + args.Exception);
                args.Handled = true;
                Finish(1);
            };
            window = new MainWindow();
            root = (Grid)window.Content;
            window.Left = -20000;
            window.Top = -20000;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.ShowInTaskbar = false;
            window.Closed += delegate { closed = true; };
            window.Loaded += async delegate
            {
                try { await Run(); Finish(0); }
                catch (Exception error) { Note("FAIL: " + error); Snapshot("failure"); Finish(1); }
            };
            application.Run(window);
        }
        catch (Exception error) { Note("FAIL bootstrap: " + error); }
        File.WriteAllLines(Path.Combine(output, "ui-flow-results.txt"), results, new UTF8Encoding(false));
        return resultCode;
    }

    private static async Task Run()
    {
        await CheckEncoderPreferences();
        List<IPAddress> local = Lan.GetAddresses();
        if (local.Count == 0) throw new InvalidOperationException("A local IPv4 adapter is needed because the production UI rejects loopback receivers.");
        renderer = new LocalRenderer(local[0], output);
        renderer.Start();
        Note("Receiver: locally simulated device at " + renderer.BaseUri + "; no real receiver is contacted.");
        ComboBox language = root.FindName("LanguageChoice") as ComboBox;
        if (language != null)
        {
            language.SelectedIndex = 3;
            Check(Element<Button>("StartButton").Content.ToString().IndexOf("投屏", StringComparison.Ordinal) < 0, "Language selection translates action controls");
            Note("Flow language: " + language.SelectedItem);
        }
        Check(!Element<Button>("StartButton").IsEnabled, "Start is disabled before selecting a receiver");
        Check(!Element<Button>("StopButton").IsEnabled, "Stop is disabled while idle");
        Element<ComboBox>("SourceKind").SelectedIndex = 2;
        Element<ComboBox>("Resolution").SelectedIndex = 1;
        Element<ComboBox>("Encoder").SelectedIndex = 0;
        Element<ComboBox>("Container").SelectedIndex = 0;
        Check(Element<FrameworkElement>("TestHint").Visibility == Visibility.Visible, "Test source selection updates the visible controls");
        Element<TextBox>("ManualAddress").Text = renderer.BaseUri + "device.xml";
        Click("AddDeviceButton");
        await Until(() => Element<ListBox>("Devices").Items.Count == 1 && Element<Button>("StartButton").IsEnabled, "manual device selection", 12000);
        Check(Element<ListBox>("Devices").SelectedItem.ToString().Contains("LOCAL UI TEST"), "Device description is loaded through the real manual-add handler");
        Snapshot("01-receiver-added");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            int stopBeforeConnect = renderer.StopCount;
            Click("StartButton");
            Click("StopButton");
            await Until(() => Element<Button>("StartButton").IsEnabled && !Element<Button>("StopButton").IsEnabled, "immediate stop while connecting", 10000);
            AssertIdle("immediate connecting stop " + (attempt + 1));
            Check(ChildEncoderCount() == 0, "Immediate stop leaves no encoder child");
            Check(renderer.StopCount == stopBeforeConnect + 1, "Immediate cancellation sends exactly one remote Stop");
        }
        Check(ReadPreference("ffmpegPath") == Path.GetFullPath(Element<TextBox>("FfmpegPath").Text.Trim().Trim('"')) &&
              ReadPreference("language") == "en", "Start saves the working encoder without losing the language preference");
        renderer.DelayNextPlayMilliseconds = 1000;
        int priorPlayEntry = renderer.PlayEnteredCount;
        Click("StartButton");
        await Until(() => renderer.PlayEnteredCount > priorPlayEntry, "pending Play action", 5000);
        Click("StopButton");
        await Until(() => Element<Button>("StartButton").IsEnabled && !Element<Button>("StopButton").IsEnabled, "stop during pending Play", 10000);
        await Task.Delay(1100);
        AssertIdle("pending Play cancellation");
        Check(!Element<TextBox>("LogText").Text.Contains("ObjectDisposedException"), "Connecting cancellations do not log ObjectDisposedException");
        Snapshot("01b-connection-cancelled");

        // Input validation must leave the selected receiver and idle controls usable.
        Element<ComboBox>("SourceKind").SelectedIndex = 1;
        Element<TextBox>("FilePath").Text = Path.Combine(output, "file-that-does-not-exist.mp4");
        int previousSet = renderer.SetCount;
        Click("StartButton");
        await Task.Delay(100);
        Check(renderer.SetCount == previousSet, "Missing video is rejected before issuing receiver commands");
        AssertIdle("missing-file validation");
        Note("Missing-file UI: " + Status());
        Snapshot("02-missing-file-error");
        Element<ComboBox>("SourceKind").SelectedIndex = 2;

        Click("StartButton");
        Check(!Element<FrameworkElement>("SourceControls").IsEnabled, "Source controls lock during connection");
        await Until(() => renderer.Pulls.Count >= 1 && renderer.Pulls[0].Bytes > 300000 && Element<Button>("StopButton").IsEnabled, "real media on first playback", 20000);
        await Until(() => renderer.QueryCount > 0 && !String.IsNullOrWhiteSpace(Element<TextBlock>("StreamDetail").Text) && Element<TextBlock>("StreamDetail").Text.Contains("MB"), "playback status refresh", 6000);
        Check(!Element<Button>("StartButton").IsEnabled, "Start stays disabled during playback");
        Check(!Element<ListBox>("Devices").IsEnabled, "Receiver selection stays locked during playback");
        Check(renderer.Pulls[0].ContentType.StartsWith("video/mp2t", StringComparison.OrdinalIgnoreCase), "Receiver receives MPEG-TS over the real media HTTP endpoint");
        Check(ChildEncoderCount() >= 1, "Playback owns a running FFmpeg child process");
        Note("First playback: " + Status() + "; bytes=" + renderer.Pulls[0].Bytes);
        Snapshot("03-playing");
        string firstUri = renderer.MediaUri;
        Click("StopButton");
        await Until(() => Element<Button>("StartButton").IsEnabled && !Element<Button>("StopButton").IsEnabled, "stop controls", 10000);
        await Until(() => ChildEncoderCount() == 0, "FFmpeg cleanup after stop", 7000);
        AssertIdle("first stop");
        Check(renderer.StopCount >= 1, "Stop sends the receiver a real AVTransport Stop action");
        await AssertListenerClosed(firstUri);
        Snapshot("04-stopped");

        // A real SOAP fault must appear in the UI and release both service and controls.
        int stopBeforeRejectedPlay = renderer.StopCount;
        renderer.FailNextPlay = true;
        Click("StartButton");
        await Until(() => renderer.FailedPlayCount == 1 && Element<Button>("StartButton").IsEnabled, "HTTP500 failure recovery", 12000);
        AssertIdle("HTTP500 recovery");
        string diagnostic = Element<TextBlock>("StatusDetail").Text + Element<TextBox>("LogText").Text;
        Check(diagnostic.Contains("500") && diagnostic.Contains("701"), "HTTP500 and UPnP701 details are available in the UI or its connection log");
        Check(!String.IsNullOrWhiteSpace(Element<TextBlock>("StatusTitle").Text) && !String.IsNullOrWhiteSpace(Element<TextBlock>("StatusDetail").Text), "Playback rejection shows a nonempty user-facing status");
        Check(ChildEncoderCount() == 0, "Rejected Play leaves no FFmpeg child");
        Check(renderer.StopCount == stopBeforeRejectedPlay + 1, "Rejected Play sends exactly one cleanup Stop");
        Note("HTTP500 UI: " + Status());
        await AssertListenerClosed(renderer.MediaUri);
        Snapshot("05-http500-recovered");

        // G3 can fetch and play the URI before rejecting SetAVTransportURI.
        int stopBeforeRejectedSet = renderer.StopCount;
        int pullsBeforeRejectedSet = renderer.Pulls.Count;
        renderer.FailNextSetAfterPull = true;
        renderer.DelayNextStopMilliseconds = 900;
        Click("StartButton");
        await Until(() => renderer.FailedSetCount == 1 && renderer.StopCount == stopBeforeRejectedSet + 1, "SetURI rejection after actual media fetch", 15000);
        Check(renderer.Pulls.Count == pullsBeforeRejectedSet + 1 && renderer.Pulls[pullsBeforeRejectedSet].Bytes > 300000, "Failed SetURI had already fetched real encoded media");
        Check(!Element<Button>("StartButton").IsEnabled && !Element<Button>("StopButton").IsEnabled && !Element<FrameworkElement>("SourceControls").IsEnabled, "Failed-start cleanup keeps controls locked while remote Stop is pending");
        string rejectedSetStatus = Status();
        await AssertListenerClosed(renderer.MediaUri);
        await Until(() => Element<Button>("StartButton").IsEnabled, "remote cleanup after rejected SetURI", 6000);
        await Until(() => ChildEncoderCount() == 0, "failed SetURI encoder cleanup", 7000);
        AssertIdle("SetURI rejection after media fetch");
        Check(renderer.StopCount == stopBeforeRejectedSet + 1, "Failed SetURI sends exactly one remote Stop");
        Check(Status() == rejectedSetStatus, "Failure cleanup preserves the original error status");
        Check(Element<TextBox>("LogText").Text.Contains("501"), "UPnP501 from rejected SetURI remains in the diagnostic log");
        Snapshot("05b-seturi-prefetch-recovered");

        // Recover through another real click, then close the window while it is streaming.
        int restartPullIndex = renderer.Pulls.Count;
        Click("StartButton");
        await Until(() => renderer.Pulls.Count > restartPullIndex && renderer.Pulls[restartPullIndex].Bytes > 300000 && Element<Button>("StopButton").IsEnabled, "restart following a SOAP fault", 20000);
        Check(renderer.MediaUri != firstUri, "Restart creates a fresh unguessable media URL");
        Note("Restart playback bytes=" + renderer.Pulls[restartPullIndex].Bytes);
        Snapshot("06-restarted");
        string finalUri = renderer.MediaUri;
        int stopBeforeActiveClose = renderer.StopCount;
        window.Close();
        await Until(() => closed, "window close during playback", 10000);
        await Until(() => ChildEncoderCount() == 0, "FFmpeg cleanup after closing", 7000);
        await AssertListenerClosed(finalUri);
        Check(renderer.StopCount == stopBeforeActiveClose + 1, "Closing during playback sends exactly one Stop and releases the stream");
        foreach (LocalRenderer.Pull pull in renderer.Pulls)
        {
            Check(pull.Bytes > 300000, "Sustained media bytes for " + Path.GetFileName(pull.Path));
            if (pull.Error != null) throw new IOException("Mock receiver failed: " + pull.Error);
        }
        Check(!Element<TextBox>("LogText").Text.Contains("ObjectDisposedException"), "Playback lifecycle does not log ObjectDisposedException");

        // A separate real window exercises Close immediately after Start has yielded on network I/O.
        closed = false;
        window = new MainWindow(); root = (Grid)window.Content;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false;
        window.Closed += delegate { closed = true; }; window.Show();
        if (language != null) Check(Element<ComboBox>("LanguageChoice").SelectedIndex == 3, "Language preference survives reopening the window");
        Element<ComboBox>("SourceKind").SelectedIndex = 2;
        Element<TextBox>("ManualAddress").Text = renderer.BaseUri + "device.xml";
        Click("AddDeviceButton");
        await Until(() => Element<Button>("StartButton").IsEnabled, "receiver for immediate-close test", 10000);
        int stopBeforeImmediateClose = renderer.StopCount;
        Click("StartButton"); window.Close();
        await Until(() => closed, "immediate close while connecting", 10000);
        await Task.Delay(300);
        Check(ChildEncoderCount() == 0, "Immediate close leaves no encoder child");
        Check(!Element<TextBox>("LogText").Text.Contains("ObjectDisposedException"), "Immediate close does not log ObjectDisposedException");
        Check(renderer.StopCount == stopBeforeImmediateClose + 1, "Immediate close sends exactly one Stop");
        File.WriteAllText(Path.Combine(output, "07-immediate-close-ui-log.txt"), Element<TextBox>("LogText").Text, new UTF8Encoding(false));

        // Closing while failed-start Stop is outstanding must join that same request.
        closed = false;
        window = new MainWindow(); root = (Grid)window.Content;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false;
        window.Closed += delegate { closed = true; }; window.Show();
        Element<ComboBox>("SourceKind").SelectedIndex = 2;
        Element<ComboBox>("Resolution").SelectedIndex = 1;
        Element<TextBox>("ManualAddress").Text = renderer.BaseUri + "device.xml";
        Click("AddDeviceButton");
        await Until(() => Element<Button>("StartButton").IsEnabled, "receiver for failed-start close", 10000);
        int priorFailedSet = renderer.FailedSetCount;
        int stopBeforeFailedClose = renderer.StopCount;
        renderer.FailNextSetAfterPull = true;
        renderer.DelayNextStopMilliseconds = 900;
        Click("StartButton");
        await Until(() => renderer.FailedSetCount > priorFailedSet && renderer.StopCount == stopBeforeFailedClose + 1, "pending failed-start Stop before close", 15000);
        string failedCloseUri = renderer.MediaUri;
        window.Close();
        await Until(() => closed, "close joins failed-start cleanup", 6000);
        await Until(() => ChildEncoderCount() == 0, "failed-start close encoder cleanup", 7000);
        Check(renderer.StopCount == stopBeforeFailedClose + 1, "Close during failed-start cleanup does not send a duplicate Stop");
        Check(!Element<TextBox>("LogText").Text.Contains("ObjectDisposedException"), "Failed-start cleanup and Close do not dispose cancellation twice");
        await AssertListenerClosed(failedCloseUri);
        File.WriteAllText(Path.Combine(output, "08-failed-start-close-ui-log.txt"), Element<TextBox>("LogText").Text, new UTF8Encoding(false));
        Note("PASS: actual WPF events, language and encoder preference persistence/fallback, device XML, SetAVTransportURI/Play/GetTransportInfo/Stop, sustained FFmpeg TS, stop/restart, immediate Stop/Close during connection, HTTP500 recovery, SetURI501 after live GET, bounded remote failure cleanup, closing while cleanup is pending, no duplicate Stop or child encoder leaks.");
    }

    private static async Task CheckEncoderPreferences()
    {
        string fallback = FFmpegLocator.Find();
        if (String.IsNullOrEmpty(fallback)) throw new FileNotFoundException("Set GOGGLECAST_FFMPEG or add FFmpeg to PATH before running the UI flow.");
        string customDirectory = Path.Combine(output, "preference-test-encoder");
        Directory.CreateDirectory(customDirectory);
        string custom = Path.Combine(customDirectory, "ffmpeg.exe");
        // This placeholder is used only to test path selection; it is never executed.
        File.WriteAllText(custom, "settings-only test fixture", Encoding.ASCII);
        TextBox input = Element<TextBox>("FfmpegPath");
        input.Text = "\"" + custom + "\"";
        input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, input, Element<ComboBox>("LanguageChoice"))
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Check(ReadPreference("ffmpegPath") == custom, "A manually entered quoted encoder path is normalized and saved on focus loss");
        Element<ComboBox>("LanguageChoice").SelectedIndex = 8;
        Check(ReadPreference("language") == "de" && ReadPreference("ffmpegPath") == custom, "Changing language retains the saved encoder path");
        Element<ComboBox>("LanguageChoice").SelectedIndex = 3;
        await ReopenPreferenceWindow();
        Check(Element<TextBox>("FfmpegPath").Text == custom && Element<ComboBox>("LanguageChoice").SelectedIndex == 3,
            "Both encoder path and language survive reopening the window");
        File.Delete(custom);
        await ReopenPreferenceWindow();
        Check(Element<TextBox>("FfmpegPath").Text == fallback && Element<ComboBox>("LanguageChoice").SelectedIndex == 3,
            "A missing saved encoder falls back to normal discovery while retaining language");
    }

    private static async Task ReopenPreferenceWindow()
    {
        closed = false; window.Close();
        await Until(() => closed, "close for preference restart", 5000);
        closed = false;
        window = new MainWindow(); root = (Grid)window.Content;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false;
        window.Closed += delegate { closed = true; }; window.Show();
        await Task.Yield();
    }

    private static string ReadPreference(string name)
    {
        XmlDocument settings = new XmlDocument(); settings.XmlResolver = null;
        using (XmlReader reader = XmlReader.Create(Path.Combine(output, "settings.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) settings.Load(reader);
        XmlNode node = settings.SelectSingleNode("/settings/" + name);
        return node == null ? "" : node.InnerText;
    }

    private static void AssertIdle(string label)
    {
        Check(Element<Button>("StartButton").IsEnabled && !Element<Button>("StopButton").IsEnabled &&
              Element<FrameworkElement>("SourceControls").IsEnabled && Element<ListBox>("Devices").IsEnabled, "Idle controls restored after " + label);
    }
    private static T Element<T>(string name) where T : class { return (T)root.FindName(name); }
    private static void Click(string name)
    {
        Button button = Element<Button>(name);
        if (!button.IsEnabled) throw new InvalidOperationException("Disabled button: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private static async Task Until(Func<bool> predicate, string label, int milliseconds)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!predicate())
        {
            if (elapsed.ElapsedMilliseconds > milliseconds) throw new TimeoutException(label + "; " + Status());
            await Task.Delay(80);
        }
    }
    private static string Status() { return Element<TextBlock>("StatusTitle").Text + " | " + Element<TextBlock>("StatusDetail").Text; }
    private static int ChildEncoderCount()
    {
        int count = 0;
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.ToInt64() == -1) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            ProcessEntry process = new ProcessEntry(); process.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
            uint parent = (uint)Process.GetCurrentProcess().Id;
            if (Process32First(snapshot, ref process))
                do { if (process.ParentId == parent && String.Equals(process.FileName, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase)) count++; } while (Process32Next(snapshot, ref process));
        }
        finally { CloseHandle(snapshot); }
        return count;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, Id;
        internal IntPtr Heap;
        internal uint Module, Threads, ParentId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string FileName;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static Task AssertListenerClosed(string address)
    {
        Uri uri = new Uri(address);
        foreach (IPEndPoint listener in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            if (listener.Port == uri.Port && (listener.Address.ToString() == uri.Host || listener.Address.Equals(IPAddress.Any)))
                throw new InvalidOperationException("Media listener remained open: " + address);
        Note("PASS: media listener closed on port " + uri.Port);
        return Task.FromResult(0);
    }
    private static void Snapshot(string name)
    {
        try
        {
            window.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (Stream destination = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(destination);
            File.WriteAllText(Path.Combine(output, name + "-ui-log.txt"), Element<TextBox>("LogText").Text, new UTF8Encoding(false));
        }
        catch (Exception error) { Note("Snapshot failed: " + error.Message); }
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Note("PASS: " + label); }
    private static void Note(string message) { string line = DateTime.UtcNow.ToString("o") + " " + message; results.Add(line); Console.WriteLine(line); }
    private static void Finish(int code)
    {
        resultCode = code;
        if (renderer != null) renderer.Dispose();
        if (window != null && !closed) window.Close();
        application.Shutdown(code);
    }
}

internal sealed class LocalRenderer : IDisposable
{
    internal sealed class Pull
    {
        internal long Bytes;
        internal string Path, ContentType = "", Error;
        internal CancellationTokenSource Cancel = new CancellationTokenSource();
    }
    private readonly TcpListener listener;
    private readonly IPAddress localAddress;
    private readonly string output;
    private readonly object gate = new object();
    private readonly List<Pull> pulls = new List<Pull>();
    private int disposed;
    internal string BaseUri, MediaUri;
    internal volatile bool FailNextPlay, FailNextSetAfterPull;
    internal int SetCount, PlayCount, FailedPlayCount, FailedSetCount, StopCount, QueryCount, PlayEnteredCount, DelayNextPlayMilliseconds, DelayNextStopMilliseconds;
    private volatile string state = "STOPPED";
    internal List<Pull> Pulls { get { lock (gate) return new List<Pull>(pulls); } }
    internal LocalRenderer(IPAddress address, string output) { localAddress = address; this.output = output; listener = new TcpListener(address, 0); }
    internal void Start()
    {
        listener.Start(); BaseUri = "http://" + localAddress + ":" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
        RunBackground(async delegate
        {
            try { while (Volatile.Read(ref disposed) == 0) { TcpClient client = await listener.AcceptTcpClientAsync(); RunBackground(() => Handle(client)); } }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        });
    }
    private async Task Handle(TcpClient client)
    {
        try { await HandleConnection(client); }
        // Cancelling a real connection deliberately resets pending mock HTTP reads.
        catch (IOException) { }
        catch (SocketException) { }
    }
    private async Task HandleConnection(TcpClient client)
    {
        using (client)
        {
            if (!((IPEndPoint)client.Client.RemoteEndPoint).Address.Equals(localAddress)) return;
            using (NetworkStream stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                StringBuilder header = new StringBuilder(); int character;
                while ((character = stream.ReadByte()) >= 0)
                {
                    header.Append((char)character);
                    if (header.Length > 32768) return;
                    if (header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
                }
                string headers = header.ToString(); int length = 0;
                foreach (string line in headers.Split(new string[] { "\r\n" }, StringSplitOptions.None))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = Int32.Parse(line.Substring(15).Trim());
                if (length > 1048576) return;
                byte[] incoming = new byte[length]; int position = 0;
                while (position < length) { int read = await stream.ReadAsync(incoming, position, length - position); if (read == 0) return; position += read; }
                string body = Encoding.UTF8.GetString(incoming), response, status = "200 OK", faultCode = "701";
                bool startPull = false;
                if (headers.StartsWith("GET /device.xml", StringComparison.Ordinal))
                    response = "<root><device><friendlyName>LOCAL UI TEST receiver</friendlyName><modelName>Simulated renderer</modelName><UDN>uuid:local-ui-test</UDN><serviceList><service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>/control</controlURL></service></serviceList></device></root>";
                else
                {
                    XmlDocument document = new XmlDocument(); document.LoadXml(body);
                    XmlNode action = document.SelectSingleNode("//*[local-name()='Body']/*");
                    string name = action.LocalName;
                    if (name == "SetAVTransportURI")
                    {
                        MediaUri = action.SelectSingleNode("CurrentURI").InnerText; Interlocked.Increment(ref SetCount);
                        if (FailNextSetAfterPull)
                        {
                            FailNextSetAfterPull = false; state = "PLAYING";
                            Pull prefetched = BeginPull();
                            Stopwatch waitForMedia = Stopwatch.StartNew();
                            while (prefetched.Bytes <= 300000 && prefetched.Error == null && waitForMedia.ElapsedMilliseconds < 6000) await Task.Delay(30);
                            Interlocked.Increment(ref FailedSetCount); status = "500 Internal Server Error"; faultCode = "501";
                        }
                    }
                    if (name == "GetTransportInfo") Interlocked.Increment(ref QueryCount);
                    if (name == "Play")
                    {
                        Interlocked.Increment(ref PlayEnteredCount);
                        int stopVersion = StopCount;
                        int delay = Interlocked.Exchange(ref DelayNextPlayMilliseconds, 0);
                        if (delay > 0) await Task.Delay(delay);
                        if (stopVersion != StopCount) { state = "STOPPED"; }
                        else if (FailNextPlay)
                        {
                            FailNextPlay = false; Interlocked.Increment(ref FailedPlayCount); status = "500 Internal Server Error";
                        }
                        else { Interlocked.Increment(ref PlayCount); state = "PLAYING"; startPull = true; }
                    }
                    if (name == "Stop")
                    {
                        Interlocked.Increment(ref StopCount); state = "STOPPED"; foreach (Pull pull in Pulls) pull.Cancel.Cancel();
                        int delay = Interlocked.Exchange(ref DelayNextStopMilliseconds, 0);
                        if (delay > 0) await Task.Delay(delay);
                    }
                    if (status.StartsWith("500"))
                        response = "<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><s:Fault><faultstring>UPnPError</faultstring><detail><UPnPError><errorCode>" + faultCode + "</errorCode><errorDescription>Simulated transition unavailable</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>";
                    else
                        response = "<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><u:" + name + "Response xmlns:u='urn:schemas-upnp-org:service:AVTransport:1'>" +
                            (name == "GetTransportInfo" ? "<CurrentTransportState>" + state + "</CurrentTransportState><CurrentTransportStatus>OK</CurrentTransportStatus><CurrentSpeed>1</CurrentSpeed>" : "") + "</u:" + name + "Response></s:Body></s:Envelope>";
                }
                byte[] content = Encoding.UTF8.GetBytes(response);
                byte[] outgoing = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: text/xml; charset=utf-8\r\nContent-Length: " + content.Length + "\r\nConnection: close\r\n\r\n");
                try { await stream.WriteAsync(outgoing, 0, outgoing.Length); await stream.WriteAsync(content, 0, content.Length); }
                catch (IOException) { return; }
                if (startPull) BeginPull();
            }
        }
    }
    private Pull BeginPull()
    {
        Pull pull = new Pull(); lock (gate) { pull.Path = System.IO.Path.Combine(output, "received-" + (pulls.Count + 1) + ".ts"); pulls.Add(pull); }
        string address = MediaUri;
        RunBackground(() => ReadMedia(pull, address));
        return pull;
    }
    private async Task ReadMedia(Pull pull, string address)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address); request.Proxy = null; request.Timeout = 15000; request.ReadWriteTimeout = 10000;
        using (pull.Cancel.Token.Register(request.Abort))
        {
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (Stream source = response.GetResponseStream())
                using (Stream evidence = File.Create(pull.Path))
                {
                    pull.ContentType = response.ContentType; byte[] buffer = new byte[32768];
                    while (!pull.Cancel.IsCancellationRequested)
                    {
                        int read = await source.ReadAsync(buffer, 0, buffer.Length, pull.Cancel.Token);
                        if (read == 0) break;
                        if (evidence.Length < 3000000) evidence.Write(buffer, 0, read);
                        Interlocked.Add(ref pull.Bytes, read);
                    }
                }
            }
            catch (Exception error)
            {
                // Closing the production media service can terminate this read before its later Stop SOAP request arrives.
                if (pull.Bytes == 0 && !pull.Cancel.IsCancellationRequested && Volatile.Read(ref disposed) == 0) pull.Error = error.ToString();
            }
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        listener.Stop(); foreach (Pull pull in Pulls) pull.Cancel.Cancel();
    }
    private void RunBackground(Func<Task> work)
    {
        Task task = Task.Run(work);
        task.ContinueWith(failed => Console.Error.WriteLine("Mock background error: " + failed.Exception), TaskContinuationOptions.OnlyOnFaulted);
    }
}
