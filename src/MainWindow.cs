using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace GoggleCast
{
    // XAML is a standalone Grid; no generated code or PresentationBuildTasks are required.
    public sealed class MainWindow : Window
    {
        private readonly Grid root;
        private readonly ComboBox sourceKind, screenList, resolution, encoder, container, languageChoice;
        private readonly TextBox filePath, ffmpegPath, manualAddress, logText;
        private readonly TextBlock statusTitle, statusDetail, streamDetail, discoveryHint, emptyDevices, ffmpegHint, desktopAudioHint;
        private readonly Button discoverButton, addDeviceButton, startButton, stopButton;
        private readonly CheckBox directFile, desktopAudio;
        private readonly ListBox devices;
        private readonly FrameworkElement sourceControls, desktopPanel, filePanel, testHint, encodingControls, manualExpander;
        private readonly DispatcherTimer statusTimer;
        private readonly object logLock = new object();
        private readonly string logDirectory;
        private readonly string preferencesPath;
        private string savedFfmpegPath = "";
        private readonly List<LocalizedValue> localizedValues = new List<LocalizedValue>();
        private StreamWriter logWriter;
        private CancellationTokenSource discoveryCancellation, sessionCancellation;
        private MediaHttpServer server;
        private DlnaClient client;
        private Task failedStartCleanup;
        private bool searching, starting, stopping, sessionActive, polling, allowClose, closing;
        private string transportState = "等待设备状态", activeSource = "";
        private DateTime sessionStarted;
        private bool loggedNoRequests, loggedNoData;

        public MainWindow()
        {
            Title = "观幕 GoggleCast 0.1.0";
            Width = 1100;
            Height = 760;
            MinWidth = 900;
            MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14;
            string applicationDirectory = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location);
            preferencesPath = Path.Combine(applicationDirectory, "settings.xml");
            I18n.SetLanguage(ReadLanguagePreference());
            string xamlPath = Path.Combine(applicationDirectory, "MainWindow.xaml");
            if (!File.Exists(xamlPath)) xamlPath = Path.Combine(applicationDirectory, "src", "MainWindow.xaml");
            using (FileStream input = File.OpenRead(xamlPath)) root = (Grid)XamlReader.Load(input);
            Content = root;
            sourceKind = Find<ComboBox>("SourceKind"); screenList = Find<ComboBox>("ScreenList");
            resolution = Find<ComboBox>("Resolution"); encoder = Find<ComboBox>("Encoder"); container = Find<ComboBox>("Container");
            languageChoice = Find<ComboBox>("LanguageChoice");
            filePath = Find<TextBox>("FilePath"); ffmpegPath = Find<TextBox>("FfmpegPath"); manualAddress = Find<TextBox>("ManualAddress"); logText = Find<TextBox>("LogText");
            statusTitle = Find<TextBlock>("StatusTitle"); statusDetail = Find<TextBlock>("StatusDetail"); streamDetail = Find<TextBlock>("StreamDetail");
            discoveryHint = Find<TextBlock>("DiscoveryHint"); emptyDevices = Find<TextBlock>("EmptyDevices"); ffmpegHint = Find<TextBlock>("FfmpegHint"); desktopAudioHint = Find<TextBlock>("DesktopAudioHint");
            discoverButton = Find<Button>("DiscoverButton"); addDeviceButton = Find<Button>("AddDeviceButton");
            startButton = Find<Button>("StartButton"); stopButton = Find<Button>("StopButton");
            directFile = Find<CheckBox>("DirectFile"); desktopAudio = Find<CheckBox>("DesktopAudio"); devices = Find<ListBox>("Devices");
            sourceControls = Find<FrameworkElement>("SourceControls"); desktopPanel = Find<FrameworkElement>("DesktopPanel");
            filePanel = Find<FrameworkElement>("FilePanel"); testHint = Find<FrameworkElement>("TestHint");
            encodingControls = Find<FrameworkElement>("EncodingControls"); manualExpander = Find<FrameworkElement>("ManualExpander");
            CaptureLocalizedValues(root);
            logDirectory = Path.Combine(applicationDirectory, "logs");
            try
            {
                Directory.CreateDirectory(logDirectory);
                logWriter = new StreamWriter(Path.Combine(logDirectory, "GoggleCast-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log"), true, new UTF8Encoding(false));
                logWriter.AutoFlush = true;
            }
            catch (Exception ex) { logText.Text = "无法写入日志文件夹：" + ex.Message + Environment.NewLine; }
            int screenIndex = 1;
            foreach (Forms.Screen screen in Forms.Screen.AllScreens) screenList.Items.Add(new ScreenChoice(screenIndex++, screen));
            screenList.SelectedIndex = screenList.Items.Count > 0 ? 0 : -1;
            for (int i = 0; i < screenList.Items.Count; i++) if (((ScreenChoice)screenList.Items[i]).Screen.Primary) screenList.SelectedIndex = i;
            ffmpegPath.Text = File.Exists(savedFfmpegPath) ? savedFfmpegPath : FFmpegLocator.Find() ?? "";
            if (String.IsNullOrWhiteSpace(ffmpegPath.Text)) SetText(ffmpegHint, "尚未找到 FFmpeg。请展开此处并选择 ffmpeg.exe。");
            else SetText(ffmpegHint, "已找到编码器；只有点击开始后才会采集。");
            languageChoice.Items.Add(new LanguageOption("auto", "跟随系统 / System"));
            languageChoice.Items.Add(new LanguageOption("zh-CN", "简体中文"));
            languageChoice.Items.Add(new LanguageOption("zh-TW", "繁體中文"));
            languageChoice.Items.Add(new LanguageOption("en", "English"));
            languageChoice.Items.Add(new LanguageOption("ja", "日本語"));
            languageChoice.Items.Add(new LanguageOption("ko", "한국어"));
            languageChoice.Items.Add(new LanguageOption("es", "Español"));
            languageChoice.Items.Add(new LanguageOption("fr", "Français"));
            languageChoice.Items.Add(new LanguageOption("de", "Deutsch"));
            languageChoice.SelectedIndex = 0;
            foreach (LanguageOption option in languageChoice.Items) if (option.Code == I18n.SelectedLanguage) languageChoice.SelectedItem = option;
            languageChoice.SelectionChanged += delegate
            {
                LanguageOption option = languageChoice.SelectedItem as LanguageOption;
                if (option == null) return;
                I18n.SetLanguage(option.Code);
                ApplyLanguage();
                SaveLanguagePreference(option.Code);
            };
            sourceKind.SelectionChanged += delegate { UpdateSource(); };
            directFile.Checked += delegate { UpdateSource(); };
            directFile.Unchecked += delegate { UpdateSource(); };
            desktopAudio.Checked += delegate { UpdateSource(); };
            desktopAudio.Unchecked += delegate { UpdateSource(); };
            Find<Button>("SelectFileButton").Click += delegate { SelectVideo(); };
            Find<Button>("SelectFfmpegButton").Click += delegate { SelectFfmpeg(); };
            ffmpegPath.LostKeyboardFocus += delegate { SaveFfmpegPreference(); };
            Find<Button>("OpenLogsButton").Click += delegate { OpenLogs(); };
            discoverButton.Click += async delegate { await DiscoverAsync(); };
            addDeviceButton.Click += async delegate { await AddDeviceAsync(); };
            startButton.Click += async delegate { await StartAsync(); };
            stopButton.Click += async delegate { await StopAsync(false); };
            devices.SelectionChanged += delegate { UpdateButtons(); };
            Closing += OnClosing;
            Closed += delegate
            {
                if (discoveryCancellation != null) discoveryCancellation.Cancel();
                if (sessionCancellation != null) sessionCancellation.Cancel();
                if (server != null) server.Dispose();
                lock (logLock) { if (logWriter != null) { logWriter.Dispose(); logWriter = null; } }
            };
            statusTimer = new DispatcherTimer();
            statusTimer.Interval = TimeSpan.FromSeconds(2);
            statusTimer.Tick += async delegate { await PollAsync(); };
            ApplyLanguage();
            UpdateSource();
            UpdateButtons();
            Log("观幕 GoggleCast 0.1.0 已打开。尚未采集或分享屏幕。");
        }

        private T Find<T>(string name) where T : class
        {
            T element = root.FindName(name) as T;
            if (element == null) throw new InvalidOperationException("界面缺少控件：" + name);
            return element;
        }

        private void UpdateSource()
        {
            if (desktopPanel == null) return;
            desktopPanel.Visibility = sourceKind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            filePanel.Visibility = sourceKind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            testHint.Visibility = sourceKind.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            encodingControls.IsEnabled = !(sourceKind.SelectedIndex == 1 && directFile.IsChecked == true);
            SetText(desktopAudioHint, desktopAudio.IsChecked == true ? "系统声音与画面一起发送。请将耳机连接到眼镜的 USB-C 接口收听。" : "仅发送画面；声音仍由电脑播放。");
        }

        private void UpdateButtons()
        {
            bool locked = sessionActive || starting || stopping;
            sourceControls.IsEnabled = !locked;
            devices.IsEnabled = !locked && !searching;
            manualExpander.IsEnabled = !locked && !searching;
            discoverButton.IsEnabled = !locked;
            SetLocalized(discoverButton, ContentControl.ContentProperty, searching ? "取消搜索" : "搜索设备");
            addDeviceButton.IsEnabled = !locked && !searching;
            startButton.IsEnabled = !locked && !searching && devices.SelectedItem != null;
            SetLocalized(startButton, ContentControl.ContentProperty, starting ? "正在连接…" : "开始投屏");
            stopButton.IsEnabled = sessionActive && !stopping;
            SetLocalized(stopButton, ContentControl.ContentProperty, stopping ? "停止中…" : "停止");
        }

        private void SelectVideo()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = I18n.T("选择要在眼镜中播放的视频");
            dialog.Filter = I18n.T("视频文件") + "|*.mp4;*.m4v;*.mov;*.mkv;*.avi;*.ts;*.webm|" + I18n.T("所有文件") + "|*.*";
            if (dialog.ShowDialog(this) == true)
            {
                filePath.Text = dialog.FileName;
                directFile.IsChecked = false;
                filePath.ToolTip = dialog.FileName;
            }
        }

        private void SelectFfmpeg()
        {
            OpenFileDialog dialog = new OpenFileDialog(); dialog.Title = I18n.T("选择 FFmpeg 编码器");
            dialog.Filter = "FFmpeg|ffmpeg.exe|" + I18n.T("应用程序") + "|*.exe";
            if (dialog.ShowDialog(this) == true)
            {
                ffmpegPath.Text = dialog.FileName;
                SetText(ffmpegHint, "将在开始投屏时使用此编码器。");
                SaveFfmpegPreference();
            }
        }

        private async Task DiscoverAsync()
        {
            if (searching) { if (discoveryCancellation != null) discoveryCancellation.Cancel(); return; }
            searching = true;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            discoveryCancellation = cancellation;
            UpdateButtons();
            SetText(discoveryHint, "正在搜索局域网设备，约需 5 秒…");
            SetText(emptyDevices, "正在搜索，请保持眼镜的无线投屏页面打开…");
            Log("开始 SSDP 设备搜索。");
            try
            {
                List<DlnaDevice> found = await DlnaDiscovery.DiscoverAsync(4500, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                devices.Items.Clear();
                foreach (DlnaDevice device in found) devices.Items.Add(new DeviceChoice(device));
                if (devices.Items.Count > 0) devices.SelectedIndex = 0;
                emptyDevices.Visibility = devices.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SetText(emptyDevices, "未找到设备\n确认眼镜已开启无线投屏，并与电脑处于同一网络。");
                if (devices.Items.Count > 0) SetText(discoveryHint, "找到 {0} 台设备。请选择你的眼镜。", devices.Items.Count);
                else SetText(discoveryHint, "未发现设备。可重试搜索，或在下方手动添加 IP / 描述地址。");
                Log("搜索完成，找到 " + devices.Items.Count + " 台支持 AVTransport 的设备。");
            }
            catch (OperationCanceledException) { SetText(discoveryHint, "搜索已取消。"); SetText(emptyDevices, "搜索已取消，可再次点击「搜索设备」。"); Log("设备搜索已取消。"); }
            catch (Exception ex) { SetText(discoveryHint, "搜索失败：{0}", ex); SetText(emptyDevices, "搜索失败，请检查网络后重试。"); Log("搜索失败：" + ex); }
            finally
            {
                searching = false;
                if (discoveryCancellation == cancellation) discoveryCancellation = null;
                cancellation.Dispose();
                UpdateButtons();
            }
        }

        private async Task AddDeviceAsync()
        {
            string address = manualAddress.Text.Trim();
            if (address.Length == 0) { SetText(discoveryHint, "请先输入眼镜的 IP，或设备描述文件的 HTTP 地址。"); manualAddress.Focus(); return; }
            searching = true;
            CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            discoveryCancellation = cancellation;
            UpdateButtons(); SetText(discoveryHint, "正在读取设备信息…");
            try
            {
                DlnaDevice device = await DlnaDiscovery.FromAddressAsync(address, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (device == null) throw new InvalidOperationException("未读取到可用的 AVTransport 设备。");
                DeviceChoice choice = null;
                foreach (DeviceChoice existing in devices.Items) if (existing.Device.ControlUri == device.ControlUri) { choice = existing; break; }
                if (choice == null) { choice = new DeviceChoice(device); devices.Items.Add(choice); }
                devices.SelectedItem = choice; emptyDevices.Visibility = Visibility.Collapsed;
                SetText(discoveryHint, "已添加设备，请确认名称后开始投屏。");
                Log("手动添加设备：" + choice);
            }
            catch (OperationCanceledException) { SetText(discoveryHint, "读取已取消或超时。请确认 IP，也可以输入完整描述地址。"); Log("手动读取设备已取消或超时。"); }
            catch (Exception ex) { SetText(discoveryHint, "添加失败：{0}", ex); Log("添加设备失败：" + ex); }
            finally
            {
                searching = false;
                if (discoveryCancellation == cancellation) discoveryCancellation = null;
                cancellation.Dispose(); UpdateButtons();
            }
        }

        private async Task StartAsync()
        {
            if (sessionActive || starting || stopping || devices.SelectedItem == null) return;
            DeviceChoice selected = (DeviceChoice)devices.SelectedItem;
            CaptureSettings settings = new CaptureSettings();
            settings.SourceKind = sourceKind.SelectedIndex == 1 ? "file" : sourceKind.SelectedIndex == 2 ? "test" : "desktop";
            settings.FilePath = filePath.Text;
            settings.OutputWidth = resolution.SelectedIndex == 1 ? 1280 : 1920;
            settings.OutputHeight = resolution.SelectedIndex == 1 ? 720 : 1080;
            settings.Fps = 30; settings.BitrateKbps = resolution.SelectedIndex == 1 ? 4000 : 6000;
            settings.Encoder = encoder.SelectedIndex == 1 ? "cpu" : "auto";
            settings.Container = container.SelectedIndex == 1 ? "mp4" : "ts";
            settings.Audio = settings.SourceKind != "desktop" || desktopAudio.IsChecked == true;
            bool originalFile = settings.SourceKind == "file" && directFile.IsChecked == true;
            string ffmpeg = ffmpegPath.Text.Trim().Trim('"');
            if (settings.SourceKind == "file" && !File.Exists(settings.FilePath)) { SetStatus("请选择视频", "文件不存在，或尚未选择视频。", true); return; }
            if (originalFile)
            {
                string extension = Path.GetExtension(settings.FilePath).ToLowerInvariant();
                if (extension != ".mp4" && extension != ".m4v") { SetStatus("需要视频转码", "原画直传支持 MP4 / M4V。请取消原画直传后重试。", true); return; }
            }
            if (!originalFile && !File.Exists(ffmpeg)) { SetStatus("尚未找到编码器", "请展开「编码器路径」并选择 ffmpeg.exe 后重试。", true); return; }
            SaveFfmpegPreference();
            if (settings.SourceKind == "desktop")
            {
                ScreenChoice selectedScreen = screenList.SelectedItem as ScreenChoice;
                if (selectedScreen == null) { SetStatus("没有可用显示器", "请连接显示器后重新打开程序。", true); return; }
                System.Drawing.Rectangle bounds = selectedScreen.Screen.Bounds;
                settings.X = bounds.X; settings.Y = bounds.Y; settings.Width = bounds.Width; settings.Height = bounds.Height;
            }
            CancellationTokenSource cancellation = new CancellationTokenSource();
            sessionCancellation = cancellation;
            starting = true; sessionActive = true; loggedNoRequests = false; loggedNoData = false;
            activeSource = settings.SourceKind == "file" ? Path.GetFileName(settings.FilePath) : settings.SourceKind == "test" ? "测试画面" : "电脑桌面";
            UpdateButtons(); SetStatus("正在连接眼镜", "正在建立媒体服务并发送播放请求…", false);
            MediaHttpServer sessionServer = null;
            DlnaClient sessionClient = null;
            bool cleanupNeeded = false;
            try
            {
            try
            {
                IPAddress localAddress = Lan.RouteTo(selected.Device.DescriptionUri);
                if (localAddress == null || IPAddress.IsLoopback(localAddress)) throw new InvalidOperationException("未找到通向眼镜的局域网地址。请检查网络连接。");
                string mime = originalFile || settings.Container == "mp4" ? "video/mp4" : "video/mp2t";
                if (originalFile) sessionServer = new MediaHttpServer(settings.FilePath);
                else sessionServer = new MediaHttpServer(delegate(CancellationToken requestToken) { return MediaSession.StartAsync(ffmpeg, settings, requestToken, Log); }, mime);
                sessionServer.Log += Log;
                server = sessionServer;
                sessionServer.Start(localAddress, 0);
                string mediaUrl = sessionServer.UrlFor(localAddress);
                sessionClient = new DlnaClient(selected.Device);
                client = sessionClient;
                Log("投屏内容：" + activeSource + (originalFile ? "（原画直传）" : "；" + settings.OutputWidth + "×" + settings.OutputHeight + " / " + settings.Fps + "fps / " + settings.Container));
                Log("媒体地址：" + mediaUrl);
                using (CancellationTokenSource requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                {
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                    await sessionClient.SetUriAsync(mediaUrl, "GoggleCast - " + I18n.T(activeSource), mime, !originalFile, requestTimeout.Token);
                    await sessionClient.PlayAsync(requestTimeout.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                sessionStarted = DateTime.Now;
                transportState = "等待设备状态";
                SetStatus("已发送播放请求", "请在眼镜内确认画面。接收端可能需要缓冲数秒。", false);
                streamDetail.Visibility = Visibility.Visible;
                SetText(streamDetail, "媒体服务已启动，等待眼镜读取。");
                Log("播放请求已被设备接受；这不代表眼镜已经显示画面。");
                statusTimer.Start();
            }
            catch (OperationCanceledException)
            {
                if (!stopping && !closing) SetStatus(cancellation.IsCancellationRequested ? "投屏已取消" : "设备响应超时", cancellation.IsCancellationRequested ? "屏幕分享已结束。" : "请检查眼镜连接状态，再点击开始投屏。", !cancellation.IsCancellationRequested);
                cleanupNeeded = true;
                Log("连接已取消或超时。");
            }
            catch (Exception ex)
            {
                SetStatus("投屏未能开始", "{0} 可检查连接或切换传输格式后重试。", true, ex);
                Log("投屏失败：" + ex);
                cleanupNeeded = true;
            }
            if (cleanupNeeded) await CleanupFailedStartAsync(cancellation, sessionServer, sessionClient);
            }
            finally { starting = false; UpdateButtons(); }
        }

        private async Task CleanupFailedStartAsync(CancellationTokenSource cancellation, MediaHttpServer sessionServer, DlnaClient sessionClient)
        {
            // Claim only this attempt's receiver. StopAsync may already own it.
            DlnaClient ownedClient = sessionClient != null && client == sessionClient ? sessionClient : null;
            if (ownedClient != null) client = null;
            if (server == sessionServer)
            {
                server = null;
                if (sessionServer != null) { try { sessionServer.Dispose(); } catch (Exception ex) { Log("关闭失败会话的媒体服务：" + ex.Message); } }
            }
            if (sessionCancellation == cancellation) { cancellation.Cancel(); sessionCancellation = null; sessionActive = false; cancellation.Dispose(); }
            streamDetail.Visibility = Visibility.Collapsed;
            UpdateButtons();
            if (ownedClient != null)
            {
                // Some receivers fetch the URI before returning a SOAP fault.
                // Keep starting=true until this bounded cleanup completes.
                Task cleanup = StopFailedReceiverAsync(ownedClient);
                failedStartCleanup = cleanup;
                try { await cleanup; }
                finally { if (failedStartCleanup == cleanup) failedStartCleanup = null; }
            }
        }

        private async Task StopFailedReceiverAsync(DlnaClient failedClient)
        {
            try
            {
                using (CancellationTokenSource stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4))) await failedClient.StopAsync(stopTimeout.Token);
                Log("失败会话已通知设备停止播放。");
            }
            catch (Exception ex) { Log("失败后设备停止请求未完成，电脑媒体服务已关闭：" + ex.Message); }
        }

        private async Task StopAsync(bool windowClosing)
        {
            if (stopping) return;
            stopping = true; statusTimer.Stop(); UpdateButtons();
            CancellationTokenSource cancellation = sessionCancellation;
            sessionCancellation = null;
            if (cancellation != null) cancellation.Cancel();
            MediaHttpServer oldServer = server; server = null;
            DlnaClient oldClient = client; client = null;
            if (oldServer != null) { try { oldServer.Dispose(); } catch (Exception ex) { Log("关闭媒体服务：" + ex.Message); } }
            SetStatus("正在停止", "已停止发送内容，正在通知眼镜…", false);
            if (oldClient != null)
            {
                try
                {
                    using (CancellationTokenSource stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4))) await oldClient.StopAsync(stopTimeout.Token);
                }
                catch (Exception ex) { Log("设备停止请求未完成，电脑媒体服务已关闭：" + ex.Message); }
            }
            // Closing during failure cleanup joins its Stop instead of sending another.
            Task pendingFailureCleanup = failedStartCleanup;
            if (pendingFailureCleanup != null) await pendingFailureCleanup;
            sessionActive = false; stopping = false;
            if (cancellation != null) cancellation.Dispose();
            streamDetail.Visibility = Visibility.Collapsed;
            SetStatus("已停止投屏", "媒体服务已关闭，当前没有分享屏幕。", false);
            Log("投屏已停止。"); UpdateButtons();
        }

        private async Task PollAsync()
        {
            if (polling || !sessionActive || stopping || client == null || server == null || sessionCancellation == null) return;
            polling = true;
            DlnaClient observedClient = client; MediaHttpServer observedServer = server;
            CancellationToken token = sessionCancellation.Token;
            try
            {
                using (CancellationTokenSource queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    queryTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    try { transportState = await observedClient.GetTransportStateAsync(queryTimeout.Token); }
                    catch (OperationCanceledException) { if (!token.IsCancellationRequested) transportState = "状态查询超时"; }
                    catch (Exception ex) { transportState = "状态暂不可用"; Log("查询设备状态：" + ex.Message); }
                }
                if (token.IsCancellationRequested || server != observedServer) return;
                double sentMb = observedServer.BytesSent / 1048576.0;
                SetText(streamDetail, "设备：{0}   ·   已发送 {1} MB   ·   连接 {2} / 请求 {3}", transportState, sentMb.ToString("0.0", CultureInfo.InvariantCulture), observedServer.ActiveClients, observedServer.TotalRequests);
                if (observedServer.TotalRequests == 0 && (DateTime.Now - sessionStarted).TotalSeconds > 15 && !loggedNoRequests)
                {
                    loggedNoRequests = true;
                    SetStatus("眼镜尚未读取内容", "检查 Windows 防火墙的专用网络权限，或停止后尝试另一传输格式。", true);
                    Log("15 秒内没有媒体请求。请检查防火墙、设备网络以及媒体格式。");
                }
                else if (observedServer.TotalRequests > 0 && observedServer.BytesSent == 0 && (DateTime.Now - sessionStarted).TotalSeconds > 15)
                {
                    SetStatus("设备已连接，尚无视频数据", "请展开日志检查编码器错误；也可停止后尝试软件编码或另一传输格式。", true);
                    if (!loggedNoData) { loggedNoData = true; Log("已收到媒体请求但尚未发送视频数据。请检查编码器日志。"); }
                }
                else if (observedServer.BytesSent > 0)
                {
                    loggedNoRequests = false; loggedNoData = false;
                    if (String.Equals(transportState, "PLAYING", StringComparison.OrdinalIgnoreCase))
                        SetStatus("设备报告正在播放", "已发送媒体数据。请在眼镜内确认实际画面。", false);
                    else if (String.Equals(transportState, "STOPPED", StringComparison.OrdinalIgnoreCase))
                        SetStatus("设备报告已停止播放", "视频可能已结束或被设备停止。点击停止后可重新选择内容。", false);
                    else
                        SetStatus("媒体数据已发送", "请在眼镜内确认画面；设备报告的状态见下方。", false);
                }
            }
            finally { polling = false; }
        }

        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (allowClose) return;
            if (discoveryCancellation != null) discoveryCancellation.Cancel();
            if (!sessionActive && !starting && !stopping) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            await StopAsync(true);
            allowClose = true;
            Close();
        }

        private void SetStatus(string title, string detail, bool error, params object[] arguments)
        {
            SetText(statusTitle, title);
            statusTitle.Foreground = new SolidColorBrush(error ? Color.FromRgb(160, 53, 27) : Color.FromRgb(26, 41, 64));
            SetText(statusDetail, detail, arguments);
        }

        private void Log(string message)
        {
            string entry = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine;
            lock (logLock) { if (logWriter != null) { try { logWriter.Write(entry); } catch { } } }
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(AppendLog), entry); return; }
            AppendLog(entry);
        }

        private void AppendLog(string entry)
        {
            logText.AppendText(entry);
            if (logText.Text.Length > 30000) logText.Text = logText.Text.Substring(logText.Text.Length - 24000);
            logText.ScrollToEnd();
        }

        private void OpenLogs()
        {
            try { Directory.CreateDirectory(logDirectory); Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("无法打开日志目录", "{0}", true, ex); }
        }

        private string ReadLanguagePreference()
        {
            try
            {
                if (!File.Exists(preferencesPath)) return "auto";
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                using (XmlReader reader = XmlReader.Create(preferencesPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
                XmlNode ffmpeg = document.SelectSingleNode("/settings/ffmpegPath");
                string candidate = ffmpeg == null ? "" : ffmpeg.InnerText.Trim().Trim('"');
                if (File.Exists(candidate)) savedFfmpegPath = Path.GetFullPath(candidate);
                XmlNode language = document.SelectSingleNode("/settings/language");
                return language == null ? "auto" : language.InnerText.Trim();
            }
            catch { return "auto"; }
        }

        private void SaveLanguagePreference(string language)
        {
            try
            {
                WritePreferences(language, savedFfmpegPath);
            }
            catch (Exception ex)
            {
                Log("保存语言设置失败：" + ex);
                SetStatus("语言已切换，但未能保存设置", "请检查程序文件夹写入权限；重新打开后可能恢复默认语言。", true);
            }
        }

        private void SaveFfmpegPreference()
        {
            string candidate = ffmpegPath.Text.Trim().Trim('"');
            if (!File.Exists(candidate)) return;
            try
            {
                candidate = Path.GetFullPath(candidate);
                if (String.Equals(candidate, savedFfmpegPath, StringComparison.OrdinalIgnoreCase)) return;
                WritePreferences(I18n.SelectedLanguage, candidate);
                savedFfmpegPath = candidate;
            }
            catch (Exception ex)
            {
                Log("保存编码器设置失败：" + ex);
                SetText(ffmpegHint, "操作未完成。请展开日志查看详细信息。");
            }
        }

        private void WritePreferences(string language, string ffmpeg)
        {
            using (XmlWriter writer = XmlWriter.Create(preferencesPath, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
            {
                writer.WriteStartDocument(); writer.WriteStartElement("settings");
                writer.WriteElementString("language", language);
                if (!String.IsNullOrEmpty(ffmpeg)) writer.WriteElementString("ffmpegPath", ffmpeg);
                writer.WriteEndElement(); writer.WriteEndDocument();
            }
        }

        private void CaptureLocalizedValues(DependencyObject element)
        {
            TextBlock text = element as TextBlock;
            if (text != null && !String.IsNullOrEmpty(text.Text)) localizedValues.Add(new LocalizedValue(element, TextBlock.TextProperty, text.Text, new object[0]));
            ContentControl content = element as ContentControl;
            if (content != null && content.Content is string) localizedValues.Add(new LocalizedValue(element, ContentControl.ContentProperty, (string)content.Content, new object[0]));
            HeaderedContentControl header = element as HeaderedContentControl;
            if (header != null && header.Header is string) localizedValues.Add(new LocalizedValue(element, HeaderedContentControl.HeaderProperty, (string)header.Header, new object[0]));
            object automationName = element.ReadLocalValue(System.Windows.Automation.AutomationProperties.NameProperty);
            if (automationName is string && ((string)automationName).Length > 0) localizedValues.Add(new LocalizedValue(element, System.Windows.Automation.AutomationProperties.NameProperty, (string)automationName, new object[0]));
            FrameworkElement frameworkElement = element as FrameworkElement;
            if (frameworkElement != null && frameworkElement.ToolTip is string) localizedValues.Add(new LocalizedValue(element, FrameworkElement.ToolTipProperty, (string)frameworkElement.ToolTip, new object[0]));
            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                DependencyObject dependency = child as DependencyObject;
                if (dependency != null) CaptureLocalizedValues(dependency);
            }
        }

        private void ApplyLanguage()
        {
            Title = I18n.T("观幕 GoggleCast") + " 0.1.0";
            string font = I18n.Language == "ja" ? "Yu Gothic UI" : I18n.Language == "ko" ? "Malgun Gothic" : "Microsoft YaHei UI";
            TextElementSetFont(root, new FontFamily(font));
            foreach (LocalizedValue value in localizedValues) RenderLocalizedValue(value);
            if (screenList != null) foreach (ScreenChoice screen in screenList.Items) screen.RefreshLanguage();
            if (devices != null) foreach (DeviceChoice device in devices.Items) device.RefreshLanguage();
            if (languageChoice != null) foreach (LanguageOption option in languageChoice.Items) option.RefreshLanguage();
        }

        private static void TextElementSetFont(DependencyObject target, FontFamily font)
        {
            target.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, font);
        }

        private void SetText(TextBlock target, string text, params object[] arguments)
        {
            SetLocalized(target, TextBlock.TextProperty, text, arguments);
        }

        private void SetLocalized(DependencyObject target, DependencyProperty property, string text, params object[] arguments)
        {
            foreach (LocalizedValue current in localizedValues)
            {
                if (current.Target == target && current.Property == property)
                {
                    current.Text = text; current.Arguments = arguments;
                    RenderLocalizedValue(current);
                    return;
                }
            }
            LocalizedValue added = new LocalizedValue(target, property, text, arguments);
            localizedValues.Add(added);
            RenderLocalizedValue(added);
        }

        private static void RenderLocalizedValue(LocalizedValue value)
        {
            object[] arguments = new object[value.Arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                Exception error = value.Arguments[i] as Exception;
                string text = value.Arguments[i] as string;
                arguments[i] = error != null ? ErrorText.Translate(error) : text != null ? I18n.T(text) : value.Arguments[i];
            }
            value.Target.SetValue(value.Property, I18n.T(value.Text, arguments));
        }

        private sealed class LocalizedValue
        {
            public readonly DependencyObject Target;
            public readonly DependencyProperty Property;
            public string Text;
            public object[] Arguments;
            public LocalizedValue(DependencyObject target, DependencyProperty property, string text, object[] arguments)
            {
                Target = target; Property = property; Text = text; Arguments = arguments ?? new object[0];
            }
        }

        private sealed class LanguageOption : INotifyPropertyChanged
        {
            public readonly string Code;
            private readonly string name;
            public LanguageOption(string code, string displayName) { Code = code; name = displayName; }
            public string Label { get { return Code == "auto" ? I18n.T("跟随系统") : name; } }
            public event PropertyChangedEventHandler PropertyChanged;
            public void RefreshLanguage() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Label")); }
            public override string ToString() { return Label; }
        }

        private sealed class ScreenChoice : INotifyPropertyChanged
        {
            public readonly Forms.Screen Screen;
            private readonly int number;
            public ScreenChoice(int index, Forms.Screen screen) { number = index; Screen = screen; }
            public string Label { get { return I18n.T("显示器 {0}", number) + (Screen.Primary ? " · " + I18n.T("主屏") : "") + "  (" + Screen.Bounds.Width + " × " + Screen.Bounds.Height + ")"; } }
            public event PropertyChangedEventHandler PropertyChanged;
            public void RefreshLanguage() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Label")); }
            public override string ToString() { return Label; }
        }

        private sealed class DeviceChoice : INotifyPropertyChanged
        {
            public readonly DlnaDevice Device;
            public DeviceChoice(DlnaDevice device) { Device = device; }
            public string Label
            {
                get
                {
                string name = String.IsNullOrWhiteSpace(Device.Name) ? I18n.T("未命名投屏设备") : Device.Name;
                string model = String.IsNullOrWhiteSpace(Device.Model) ? "" : " · " + Device.Model;
                string host = Device.DescriptionUri == null ? "" : Device.DescriptionUri.Host;
                return name + model + Environment.NewLine + host;
                }
            }
            public event PropertyChangedEventHandler PropertyChanged;
            public void RefreshLanguage() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Label")); }
            public override string ToString() { return Label; }
        }
    }
}
