# 参与开发

欢迎提交可复现的问题、翻译改进和代码。请先在 Issues 中说明较大功能的用途、设备和预期行为，方便讨论兼容性。

## 开发环境

- Windows 10/11，64 位。
- Windows 自带的 .NET Framework C# 编译器，以及 WPF 程序集；应用目标为 .NET Framework 4.8，源码保持 C# 5 语法。
- 使用 **Windows PowerShell 5.1** 执行构建和测试。无需安装 .NET SDK、Visual Studio 或 NuGet 包。
- 只有编码和音视频测试需要另行准备 FFmpeg；构建、协议模拟测试和界面启动检查不需要 FFmpeg，也不需要眼镜。

在项目目录打开 Windows PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\GoggleCast.exe
```

默认输出到 `dist`，可以使用 `-Output '其他目录'`。`MainWindow.xaml` 必须与 `GoggleCast.exe` 放在一起。

如需 FFmpeg，可在软件中选择 `ffmpeg.exe`，或仅在当前 PowerShell 会话指定：

```powershell
$env:GOGGLECAST_FFMPEG = 'D:\Tools\ffmpeg\bin\ffmpeg.exe'
```

以上路径只是示例。请使用自己的实际安装位置。不要将 FFmpeg 二进制、个人路径、用户设置、设备地址或测试录屏提交到仓库。FFmpeg 的分发许可独立于本项目，详见 `THIRD-PARTY-NOTICES.txt`。

## 源码结构

| 文件 | 职责 |
| --- | --- |
| `src/MainWindow.cs`、`src/MainWindow.xaml` | WPF 界面、投屏状态、设置与操作 |
| `src/Localization*.cs` | 八种界面的静态和动态文本 |
| `src/Dlna.cs` | SSDP 发现、设备描述和 AVTransport 控制 |
| `src/MediaHttpServer.cs` | 接收设备拉流的 HTTP 服务 |
| `src/Media.cs` | FFmpeg 启动、编码与媒体会话生命周期 |
| `src/DesktopAudio.cs` | Windows 系统播放声音的 WASAPI 回环采集 |
| `src/Program.cs` | 启动入口和命令行检查 |
| `tests/` | 协议、界面流程、声音与实机测试工具 |

## 验证改动

每次提交至少构建一次。协议改动还应运行以下模拟测试；它仅访问 `127.0.0.1`/`127.0.0.2` 上的临时测试服务，不控制真实眼镜：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\CheckProtocol.ps1
```

检查 WPF 入口和 XAML 是否正常加载，可以生成一张界面截图。这个模式不会主动发现设备或开始投屏：

```powershell
$application = (Resolve-Path .\dist\GoggleCast.exe).Path
$screenshot = Join-Path (Get-Location) 'ui-smoke.png'
$arguments = @('--ui-smoke', ('"' + $screenshot + '"'), '1100', '760')
$process = Start-Process -FilePath $application -ArgumentList $arguments -PassThru -Wait
if ($process.ExitCode -ne 0) { throw '界面启动检查失败' }
```

需要 FFmpeg 的检查请在准备好编码器后按改动范围运行：

```powershell
# 合成测试图、媒体 HTTP 和编码检查；不采集真实桌面。
$process = Start-Process -FilePath .\dist\GoggleCast.exe -ArgumentList @('--self-test', 'self-test') -PassThru -Wait
if ($process.ExitCode -ne 0) { throw '媒体自检失败' }

# 真实 WPF 操作配合本地模拟接收器，检查开始、停止、失败和资源回收。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\CheckUiFlow.ps1
```

`CheckDesktopAudio.ps1` 会读取默认播放设备的系统声音；加 `-Desktop` 还会短暂采集桌面区域，数据在内存中检查后丢弃。它们属于本地按需检查，不在 CI 自动运行。实机检查会发现或控制设备，必须先阅读脚本、选择自己的测试设备，并在清楚将共享哪些桌面和声音时运行。不要直接把个人实机脚本接入自动工作流。

## Pull Request

请在说明中写清楚问题的触发条件、修改后的行为，以及实际做过哪些验证。设备连接和媒体播放改动，请区分模拟测试、编码数据检查、真实眼镜显示，以及佩戴者确认的声画同步。接口返回成功或传出字节不能证明眼镜已正确显示。

新增界面文本请同时更新八种语言，并检查即时切换、重启后设置、较长翻译和最小窗口宽度。修复连接失败时，请检查停止、取消和关闭窗口能否回收 HTTP 连接、FFmpeg 进程和音频采集。

不要在日志、截图或 Issue 中包含 Wi-Fi 密码、个人桌面、完整本机路径或不需要公开的设备标识。最小复现日志通常比完整录屏更容易排查。

## 自动构建与发布

GitHub Actions 在 Windows 上执行系统编译器构建、本地协议模拟和 WPF 启动截图检查，上传便携压缩包、离线一键安装器、SHA-256 校验文件及检查证据。安装器构建使用固定 Inno Setup 6.7.3 编译器，并核对下载哈希和 Authenticode 签名；CI 下载并校验固定 FFmpeg 归档，将运行时编入离线安装器。CI 不运行生成的安装器或 FFmpeg、不采集系统声音或桌面、不发现或控制真实眼镜。安装器开发与静默验收参数见 [installer/README.md](installer/README.md)。

工作流使用 GitHub 官方 Action，并固定完整提交 SHA：[`actions/checkout v7.0.1`](https://github.com/actions/checkout/releases/tag/v7.0.1)、[`actions/upload-artifact v7.0.2`](https://github.com/actions/upload-artifact/releases/tag/v7.0.2)。更新时请先核对官方标签对应的提交和运行时要求。

`v*` 标签同样触发构建，但不会自动创建 Release。维护者核对构建结果、压缩包内容、校验值和实机验证说明后，再将资产发布到 GitHub Releases。Actions 中间产物只表示该提交通过列出的软件检查，不表示所有硬件、网络和固件均已兼容。
