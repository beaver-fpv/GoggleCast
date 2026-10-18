# 眼镜兼容说明 / Goggles compatibility

[中文主页](../README.md) · [English](#english) · [使用说明](USAGE.zh-CN.md)

核查日期：**2026-10-18**。本页讨论 **Windows 电脑 → 眼镜** 的桌面和视频投屏。眼镜通过 USB 向手机或电脑输出无人机飞行画面属于另一个方向，不能据此判断能否接收电脑投屏。

## 当前兼容状态

| 型号 | 本项目状态 | 可以确认什么 |
| --- | --- | --- |
| **DJI Goggles 3** | **已实机验证** | 用户的眼镜已显示测试图、1080p / 30 帧桌面和转码视频；佩戴者确认视频及桌面系统声音通过眼镜耳机播放时主观同步正常。 |
| **DJI Goggles 2** | **协议兼容，待实机验证** | DJI 官方明确支持 DLNA 无线投屏。可尝试当前版本，但本项目没有该型号实机，尚未确认发现、播放、声音和停止操作的完整表现。 |
| **DJI Goggles N3** | **当前不列入兼容名单** | 未找到官方支持 DLNA 无线投屏接收的依据；本项目没有实测，不能承诺可用。 |
| 其他接收端 | 实验性，未验证 | 需要公开 UPnP MediaRenderer / AVTransport 服务、能访问电脑的媒体 HTTP 服务，并接受所发送的音视频格式。 |

“协议兼容”表示有公开的协议依据，**不等同于本软件实机验证通过**。Goggles 2 与 DJI FPV Goggles V2 是不同型号，不可把表中的结论套用到 V2。也不能因为设备有 Wi-Fi、USB-C 或能播放存储卡视频，就推断它支持本项目。

## Goggles 2 的依据

1. [DJI Goggles 2 官方支持页](https://www.dji.com/support/product/goggles-2)在规格中明确写出 Wi-Fi 无线投屏支持 DLNA；FAQ 也确认可接收 DLNA 视频，并说明 3.5 mm 耳机接口可输出投屏声音。
2. GoggleCast 使用 SSDP 发现 UPnP MediaRenderer / AVTransport 服务，并通过局域网 HTTP 提供媒体。程序没有仅允许 Goggles 3 型号的限制；默认输出 H.264 / AAC。因此，Goggles 2 有使用当前软件的协议基础，实际固件和播放格式兼容仍需验证。
3. 同类软件 [GogglePlayer 的开发者 FAQ](https://goggleplayer.app/faq)声明其软件在 Goggles 2 和 3 上测试过。这是同类 DLNA 方案可行的旁证，**不是 GoggleCast 的实测结果**，也不代表本项目已实现其全部功能。

### 如何尝试 Goggles 2

1. 让电脑和眼镜处于同一局域网；也可尝试连接电脑的 Windows 移动热点。
2. 按 [DJI Goggles 2 用户手册下载页](https://www.dji.com/goggles-2/downloads)的说明开启眼镜无线投屏，保持眼镜唤醒并停留在等待页面。菜单以实际固件为准。
3. 打开当前 GoggleCast，搜索并选择接收端；不需要下载另一个型号专用版本。
4. 先用动态测试图和提示音确认播放，再尝试 720p / 30 帧桌面或兼容转码视频。音频优先从眼镜的 **3.5 mm 耳机接口**收听。
5. 默认格式为 MPEG-TS。若接收后没有画面，停止后尝试分片 MP4；本地视频先关闭原画直传。上述操作是排查建议，尚未在 Goggles 2 上确认结果。

能搜索到设备、请求已接受或状态为 `PLAYING` 都不能代替佩戴者看到画面并听到声音。若未发现设备，请先按[网络排查](TROUBLESHOOTING.zh-CN.md#找不到眼镜)检查；手动输入 IP 不会为眼镜增加缺失的 DLNA 接收能力。

## 为什么暂不列入 Goggles N3

[DJI Goggles N3 官方 FAQ](https://www.dji.com/goggles-n3/faq)确认了存储卡视频播放、USB-C 连接，以及通过手机 DJI Fly 分享飞行画面的流程，但没有提供 DLNA 无线投屏接收的说明。USB 分享飞行画面是眼镜向外输出，并非电脑画面输入眼镜。

同时查阅了[官方下载页](https://www.dji.com/goggles-n3/downloads)所列的 [2026-09-01 固件发布记录](https://terra-1-g.djicdn.com/6189933d30024fc1b331bffe4fe41837/goggles-n3/RN/20260901/DJI_Goggles_N3_Release_Notes_EN.pdf)，其中也未说明新增 DLNA 接收。未提及功能本身不是“不可能支持”的证明，因此这里只保留未确认状态。

因此，目前不能把 N3 标为 GoggleCast 支持型号。这是依据现有公开资料作出的兼容名单决定，**不是对它永远无法通过其他方案实现输入的断言**。若后续固件或官方资料明确增加投屏接收功能，可重新核查并实测。

N3 可以从存储卡播放视频，但那不是本项目的实时桌面投屏。

## 帮助补充实测结果

欢迎在 [GitHub Issues](https://github.com/beaver-fpv/GoggleCast/issues)反馈。请提供：

- 眼镜的完整型号、固件版本，GoggleCast 版本和 Windows 版本。
- 网络方式：路由器、电脑有线连接配合眼镜 Wi-Fi，或 Windows 移动热点。
- 测试图、本地转码视频、桌面画面、桌面系统声音分别是否正常；每项单独说明。
- 清晰度、编码器、传输格式和音频输出方式；佩戴者是否确认声画同步。
- 停止后能否重新开始，以及失败时的脱敏日志。

不要上传个人视频、Wi-Fi 密码或完整桌面录制。其他人报告的测试会注明来源和测试条件；只有资料依据时保留“待实测”标记。精确延迟和长期漂移没有测量时，不将“主观同步正常”写为“零延迟”。

## English

Evidence checked on **2026-10-18**. The direction here is **Windows PC → goggles**, not USB flight-video output from goggles to a phone or PC.

| Model | GoggleCast status | Evidence and limits |
| --- | --- | --- |
| **DJI Goggles 3** | **Hardware-tested** | Moving test pattern, 1080p30 desktop and transcoded video confirmed. The wearer reported normal subjective A/V sync for video and desktop system audio through goggles headphones. Precise latency and long-term drift were not measured. |
| **DJI Goggles 2** | **Protocol-compatible; hardware test pending** | [DJI documents DLNA wireless streaming and 3.5 mm casting-audio output](https://www.dji.com/support/product/goggles-2). This app uses SSDP, UPnP MediaRenderer / AVTransport and HTTP media delivery without a Goggles 3 model restriction. Compatibility is an inference; discovery, playback, audio and stop/restart have not been confirmed with this app on Goggles 2. |
| **DJI Goggles N3** | **Currently outside the compatibility list** | [DJI's FAQ](https://www.dji.com/goggles-n3/faq) documents SD-card playback and USB flight-view sharing, but no DLNA casting-reception support was found. These are different capabilities. This does not assert that no future firmware or other approach could ever support input. |
| Other DLNA receivers | Experimental; untested | Need a LAN-accessible MediaRenderer / AVTransport service and support for the app's media formats. DLNA file sharing alone is insufficient. |

[GogglePlayer's developer FAQ](https://goggleplayer.app/faq) reports testing its own DLNA software with Goggles 2 and 3. This is supporting evidence for the approach, **not a hardware test of GoggleCast**. Goggles 2 is not DJI FPV Goggles V2.

To try Goggles 2, use the current app: connect the PC and goggles to the same LAN, enable Wireless Streaming following the [DJI manual](https://www.dji.com/goggles-2/downloads), keep the goggles awake, and search for the receiver. Start with the moving test pattern and tone, then try 720p30 desktop or transcoded video. Use the goggles' 3.5 mm headphone port for audio. If MPEG-TS produces no image, stop and try fragmented MP4; disable direct MP4 playback while troubleshooting. These are test suggestions, not confirmed Goggles 2 settings.

Report the exact goggles model/firmware, app/Windows versions, network arrangement, video mode, encoder, stream format and audio output in [Issues](https://github.com/beaver-fpv/GoggleCast/issues). Confirm actual image and audio in the goggles, including stop/restart; a `PLAYING` response alone is insufficient. Redact personal paths and addresses from logs. Please report desktop, local video and A/V sync results separately.
