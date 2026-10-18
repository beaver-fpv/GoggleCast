# 更新记录 / Changelog

## 文档更新 — 2026-10-18 / Documentation update

- 新增眼镜兼容表及官方来源：Goggles 3 已实测；Goggles 2 官方支持 DLNA，协议兼容但本项目尚未实测；N3 当前不列入兼容名单。
- 补充 Goggles 2 的连接指引、3.5 mm 耳机说明和社区实测反馈要求。
- 仅更新说明，未改动程序或替换 v0.1.0 安装包；不将资料核查标为新增实机验证。
- Added sourced compatibility guidance: Goggles 3 hardware-tested, Goggles 2 protocol-compatible but untested with this app, and N3 currently outside the compatibility list. Documentation only; the v0.1.0 app and release binaries are unchanged.

## 0.1.0 — 2026-10-18

首次公开发布，面向 Windows 10 / 11 x64 和 DJI Goggles 3。

- 通过局域网 DLNA / UPnP 将电脑桌面、视频文件或移动测试图发送到眼镜。
- 桌面默认 1080p / 30fps，可选择 720p；支持多显示器选择。
- 桌面系统声音通过 WASAPI 回环采集，与画面共用时间线并一同发送。
- 视频默认转换为 H.264 / AAC，等比缩放；MP4 / M4V 可手动尝试原画直传。
- 支持 MPEG-TS、fragmented MP4、NVIDIA NVENC 和 CPU 编码。
- 自动搜索接收器，也可按 IP 或设备描述地址定向添加。
- 提供简体中文、繁體中文、English、日本語、한국어、Español、Français、Deutsch。
- 保存语言与编码器路径；离线安装版内置固定 FFmpeg，便携版可自行选择编码器。
- 新增原创眼镜与投屏图标，统一用于程序、安装包和快捷方式。
- 连接中可取消，停止或关闭时清理媒体服务及编码进程；失败后尝试有界远端停止。
- 提供 Windows 自动构建、协议模拟检查、界面启动检查与下载校验。

实机检查中，Goggles 3 显示了测试图、1080p 桌面与兼容转码视频；佩戴者分别确认桌面系统声音和视频音轨通过眼镜耳机收听时主观声画同步正常。没有测量精确端到端延迟或长期漂移。无线投屏仍有接收端缓冲。

### English

Initial public release: Windows desktop and local video casting to DJI Goggles 3 over DLNA, with desktop system audio, compatible transcoding, eight UI languages, an original app icon, a complete offline installer, optional portable packaging and Windows build checks. Offline Setup bundles FFmpeg with its license and corresponding-source directions; the lightweight portable ZIP uses a separately configured encoder. Perceptual audiovisual synchronization was confirmed on one Goggles 3 setup; precise latency and long-term drift were not measured.
