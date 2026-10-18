观幕 GoggleCast 0.1.0 — Windows → 支持 DLNA 的眼镜

这是本地运行的 Windows 投屏工具。它通过同一局域网内的 DLNA / UPnP
发送电脑桌面或视频文件，不需要手机作为中转，也不需要安装虚拟显示驱动。
项目与下载：https://github.com/beaver-fpv/GoggleCast

眼镜兼容性（2026-10-18 核查）
Goggles 3：已实机验证画面及眼镜耳机主观声画同步。
Goggles 2：DJI 官方支持 DLNA，协议上可尝试；本项目尚未实机验证。
Goggles N3：未找到官方 DLNA 投屏接收依据，当前不列入兼容名单。
型号依据和反馈方法：https://github.com/beaver-fpv/GoggleCast/blob/main/docs/COMPATIBILITY.md

安装与卸载
推荐运行 GoggleCast-0.1.0-Setup.exe，选择安装位置，点击「立即安装」。
安装包已经包含固定版本 FFmpeg 编码器，安装过程无需联网下载组件，
会自动准备编码器，并创建桌面、开始菜单与卸载入口。
日常投屏只需电脑和眼镜的局域网，不要求互联网连接。
更新时安装到原目录即可；用户设置和日志会保留。
可在 Windows「已安装的应用」或开始菜单中卸载；用户设置和日志会保留。
便携版不包含 FFmpeg，请把整个文件夹解压到可写目录，双击 GoggleCast.exe。
便携版需要自行选择编码器，或放入 runtime\ffmpeg.exe。

快速开始
1. 让电脑和眼镜连接同一局域网；眼镜连接电脑的移动热点也可以。
   使用家中路由器时，电脑可以连接网线。
2. 在 Goggles 3 中进入「更多 → 无线投屏」，保持该页面打开。
   Goggles 2 按该型号手册开启无线投屏；当前版本可尝试，实际结果待验证。
3. 打开 GoggleCast，选择「电脑桌面」或「视频文件」。
4. 点击「搜索设备」，选择眼镜，然后点击「开始投屏」。
5. 在眼镜内检查实际画面和声音。完成后点击「停止」。

语言
右上角可选择简体中文、繁体中文、English、日本語、한국어、Español、
Français、Deutsch，或跟随系统。语言和编码器路径保存在程序旁的 settings.xml。
界面、操作提示和常见错误会切换语言。FFmpeg、设备和 Windows 返回的
原始技术日志保留其原文，便于诊断。

画面与声音
- 电脑桌面：采集所选显示器，默认 1080p / 30fps，同时发送系统声音。
  声音来自 Windows 默认播放设备，不采集麦克风；可取消「发送桌面系统声音」。
- 视频文件：默认兼容转码，等比缩放并保留音轨。MP4 / M4V 可手动开启
  「原画直传」，但能否播放取决于眼镜对该文件尺寸、编码等的支持。
- Goggles 3 没有内置扬声器；听声音需要 USB-C 数字 DAC 耳机或相应转接头。
- Goggles 2 的投屏音频使用其 3.5 mm 耳机接口，本软件在该型号的音频尚未实测。
- 转码将视频转换为 H.264 / AAC；保留可读取的第一条音轨。
  转码模式会循环播放文件，点击停止结束；原画直传播放至文件末尾。
- 测试画面：发送移动测试图和提示音，用于排查网络与解码问题。
- 可在 MPEG-TS 和 fragmented MP4 两种传输格式之间切换。
- 自动编码优先探测 NVIDIA NVENC，不可用时回退至 CPU。

声音同步
桌面系统声音与桌面画面使用共同时间起点，本地文件保留原有音视频时间线，
都放在同一条媒体流中发送，由眼镜按时间戳播放。请从眼镜连接的耳机收听。
这不意味着电脑画面到眼镜没有缓冲延迟；同步指声音与画面之间的相对关系。
电脑本机的声音不会被自动延迟，也不会被本程序自动静音。若从电脑耳机收听，
可能先听到声音，再看到眼镜画面；此版本没有电脑耳机的延迟补偿功能。
投屏期间更换默认播放设备时，请停止投屏后重新开始。

使用范围与验证边界
本版本已做本地协议、持续编码、时间戳、多语言和停止清理测试。
2026-10-18 已在用户的 Goggles 3 上验证测试图、1080p 桌面和兼容转码视频；
用户分别确认视频文件、桌面与系统声音通过眼镜耳机播放时主观声画同步正常。
桌面实测为 1080p / 30fps、NVENC、MPEG-TS、48 kHz 双声道系统声音。
未测量精确端到端延迟，也未据此宣称毫秒级声画误差或长时间无漂移。
其他固件、设备及音频输出路径仍需实际确认，不能直接外推为零延迟。
设备接受播放请求、接收数据或报告 PLAYING，不等于已经验证眼镜画面。
无线投屏接收端可能缓冲数秒；它与 USB 图传到电脑是相反的方向。

找不到设备或没有画面
- 检查是否处于同一网络，并关闭路由器的访客网络 / 客户端隔离。
- 保持眼镜的无线投屏页面打开。VPN 可能影响局域网路由。
- 若 Windows 弹出防火墙提示，请仅在你信任的专用网络允许本程序。
  本程序不会自动修改防火墙或网络设置。
- 可输入眼镜 IP 做定向搜索；若已知设备描述文件的完整 HTTP 地址，也可输入。
- 眼镜重连 Wi-Fi 或重新开启投屏后，IP 和服务端口可能改变，请重新搜索。
- 已连接但无画面：停止后切换传输格式，或取消原画直传 / 改用 CPU 编码。
- 在「日志」中查看原始信息。日志保存在程序旁 logs 文件夹。

编码器依赖
原画直传不需要 FFmpeg。桌面、测试画面和转码需要支持 gdigrab、libx264、
AAC 的 FFmpeg。离线安装版内置 FFmpeg 8.1.2 并自动准备，无需单独下载；
便携版需在界面中选择已有的 ffmpeg.exe，选择的有效路径会保存。
程序也会查找 runtime\ffmpeg.exe、程序旁的 ffmpeg.exe、GOGGLECAST_FFMPEG
环境变量和 PATH。若移走编码器，请重新选择路径。

系统与文件
Windows 10 / 11 x64，.NET Framework 4.8。无需安装 SDK。
程序和 MainWindow.xaml 必须放在一起；settings.xml 保存配置，logs 保存日志。
开始前不会采集屏幕；停止或关闭程序会关闭本次媒体服务和编码进程。
程序只提供本次选中的媒体，不提供目录浏览或其他本地文件。
仅支持 IPv4 局域网。没有账号登录、云服务或公网推流功能。

------------------------------------------------------------
GoggleCast 0.1.0 — Windows to DLNA-capable goggles

Project and downloads: https://github.com/beaver-fpv/GoggleCast

Compatibility checked on 2026-10-18: Goggles 3 is hardware-tested. Goggles 2
officially supports DLNA and can be tried, but is untested with this app.
Goggles N3 is currently outside the compatibility list: no official DLNA
casting-reception documentation was found. See docs/COMPATIBILITY.md online.

Recommended: run GoggleCast-0.1.0-Setup.exe, choose a folder and click Install.
The offline installer contains a pinned FFmpeg 8.1.2 runtime. Installation does
not download components. Desktop/Start menu shortcuts and an uninstaller are
created automatically. The source archive was SHA-256-verified at build time.
Install updates into the same folder. Settings and logs survive upgrades and
uninstall. Later casting needs a local network, without an internet requirement.
The portable ZIP does not include FFmpeg: keep its files together in a writable
folder and select your encoder, or put it in runtime\ffmpeg.exe.
Connect the PC and goggles to the same LAN; a Windows mobile hotspot also works.
Search again after reconnecting, as the goggles' IP and service port may change.
On Goggles 3 open More > Wireless Streaming. For Goggles 2, enable wireless
streaming following its manual; this app's results on that model are unverified.
Choose a screen or local video, search for the receiver,
and start casting. Check the actual image in the goggles; click Stop to finish.

Use the language selector for Chinese (Simplified/Traditional), English,
Japanese, Korean, Spanish, French or German. Language and encoder-path settings
are saved next to the application. Device, Windows and FFmpeg diagnostics retain
their original text.

Video uses compatible transcoding by default, retaining its first audio track.
It loops until stopped. Optional direct MP4/M4V playback depends on receiver support.
Desktop sharing includes default-device system audio using WASAPI loopback and
a shared audio/video clock. It does not capture the microphone. Desktop audio
can be turned off. NVIDIA encoding falls back to CPU when unavailable.
Goggles 3 has no built-in speaker; audio requires USB-C digital DAC headphones
or a compatible adapter connected to the goggles.
Goggles 2 uses its 3.5 mm headphone port for casting audio; untested with this app.
FFmpeg is required for desktop/test/transcoded streams. Offline Setup includes
it automatically. The portable application searches runtime/app, the
GOGGLECAST_FFMPEG environment variable, PATH, or your saved selected path.

Use headphones connected to the goggles so the receiver plays audio and video
on the same timeline. Sound played directly by the PC is not delayed or muted;
this version does not compensate PC headphones for the goggles' video buffer.
Stop and restart casting after changing the default Windows playback device.

On 2026-10-18 the user's Goggles 3 displayed the test pattern, 1080p desktop and
transcoded video. The user separately confirmed that transcoded video/audio
and desktop/system audio appeared synchronized through goggles headphones.
The desktop test used 1080p30, NVENC, MPEG-TS and 48 kHz stereo loopback audio.
Precise end-to-end latency, millisecond-level AV error and long-term drift
were not measured. Other devices or firmware remain unverified.
Receiver buffering may take seconds.

If no receiver appears, check LAN connectivity, Wireless Streaming, VPN and
guest-network/client isolation. Allow the app on a trusted private network if
Windows Firewall asks. The app does not change firewall rules itself. Try the
other stream format or turn off direct playback if the receiver cannot decode it.
Use the expandable log for diagnostics; logs are stored beside the program.

Windows 10/11 x64 and .NET Framework 4.8 are required. No SDK, phone, account or
display driver is needed. IPv4 LAN only. This is an independent project, not an
official DJI product. See THIRD-PARTY-NOTICES.txt.
