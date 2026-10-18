# FFmpeg 许可证、来源与对应源码

GoggleCast 的离线安装包附带一个独立的 Windows x64 `ffmpeg.exe`，版本为 **`n8.1.2-29-g703dcc25b9-20260721`**，由 BtbN/FFmpeg-Builds 构建，按 **GNU GPL 第 3 版或更新版本**提供。GoggleCast 的原创代码采用 MIT 许可证；这不会替代或限制 FFmpeg 的许可证。

**正常安装和投屏无需下载本页的源码包。** 安装包已包含运行所需的 FFmpeg。源码下载供研究、修改和重新构建使用。

## 下载与校验

下面的运行包和小源码包随 [GoggleCast v0.1.0](https://github.com/beaver-fpv/GoggleCast/releases/tag/v0.1.0) 提供；完整依赖源码位于 Open Video Craft 的公开发行页面，可免费直接下载。

| 内容 | 下载 | 大小（字节） |
| --- | --- | ---: |
| GoggleCast 使用的 FFmpeg 运行包 | [本项目镜像：GoggleCast-FFmpeg-8.1.2-runtime.zip](https://github.com/beaver-fpv/GoggleCast/releases/download/v0.1.0/GoggleCast-FFmpeg-8.1.2-runtime.zip) | 56,566,942 |
| FFmpeg 源码、固定版本构建脚本和来源记录 | [本项目镜像](https://github.com/beaver-fpv/GoggleCast/releases/download/v0.1.0/Open-Video-Craft-1.0.5-Windows-FFmpeg-Build-Sources.tar.gz) · [上游副本](https://github.com/Reubencfernandes/Open-Video-Craft/releases/download/v1.0.5/Open-Video-Craft-1.0.5-Windows-FFmpeg-Build-Sources.tar.gz) | 16,877,286 |
| 静态链接依赖的源码缓存，与上一项一起使用 | [Open-Video-Craft-1.0.5-Windows-FFmpeg-Dependency-Sources.tar](https://github.com/Reubencfernandes/Open-Video-Craft/releases/download/v1.0.5/Open-Video-Craft-1.0.5-Windows-FFmpeg-Dependency-Sources.tar) | 2,002,585,600 |

SHA-256：

```text
e2b15864a6f1e9aa59757dacfcfe3a11837e37a587d3ffdaff98cbc48cd97b66  GoggleCast-FFmpeg-8.1.2-runtime.zip
b054dd6abc7a7ac812310d8a45ce128a7ed6d689b1f9b60cf3490242af080ee1  Open-Video-Craft-1.0.5-Windows-FFmpeg-Build-Sources.tar.gz
d2f2e0185eb18a7f55059c29a11cb1a75f6b94d78396cd44dbdd474583f3679a  Open-Video-Craft-1.0.5-Windows-FFmpeg-Dependency-Sources.tar
070be6f5202e71a5e0bec88312230eebf2708f9b9ee3694596babf20902dddd2  ffmpeg.exe
```

在 PowerShell 中运行 `Get-FileHash -Algorithm SHA256 <下载文件>`，可与上面的完整哈希比较。运行包包含 `bin/ffmpeg.exe`、GPLv3 许可证全文 `LICENSE` 和准确的版本、来源说明 `README.txt`。

## 具体来源

- 构建项目：[BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds)。
- 构建脚本提交：[8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665](https://github.com/BtbN/FFmpeg-Builds/tree/8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665)。
- FFmpeg 源码提交：[703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d](https://github.com/FFmpeg/FFmpeg/tree/703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d)，属于 `release/8.1` 分支。
- 构建变体：`win64-gpl`，使用 `8.1` add-in；原构建日期为 `2026-07-21`。
- 原始 BtbN 发行名：`autobuild-2026-07-21-13-38`，原二进制压缩包名为 `ffmpeg-n8.1.2-29-g703dcc25b9-win64-gpl-8.1.zip`。
- 原二进制压缩包记录的 SHA-256：`ebf57e8b1a10b176b88c3cbc66e68a4aed472cf47520b0fbf003e892fb3be642`。

原始 BtbN 二进制下载地址在 2026-10-18 核查时返回 HTTP 404。本项目从 Open Video Craft v1.0.5 的公开 Windows portable 包中纯解包取出 FFmpeg，核对其完整 SHA-256 后重新打成上面的运行包；没有修改 `ffmpeg.exe`。本项目不打包、安装或执行 Open Video Craft 的其他软件。

Open Video Craft 的固定版本记录可查看其[二进制与源码清单](https://github.com/Reubencfernandes/Open-Video-Craft/blob/770268c842dc015b7b1e0f4864ee7806bc051342/.github/ffmpeg-windows-sources.txt)、[Windows FFmpeg 准备脚本](https://github.com/Reubencfernandes/Open-Video-Craft/blob/770268c842dc015b7b1e0f4864ee7806bc051342/scripts/prepare-windows-ffmpeg.mjs)和[构建说明](https://github.com/Reubencfernandes/Open-Video-Craft/blob/770268c842dc015b7b1e0f4864ee7806bc051342/FFMPEG_WINDOWS_BUILD_NOTES.md)。其 portable 包 SHA-256 公开记录为 `d65c9f2670ffd9995a4a1ecb94dd41c03180de4346ae6fe6cbf2894617c3d3c9`；本次只读取了提取 FFmpeg 所需的数据范围，没有宣称校验过整个 portable 包。

## 已核查内容与源码包范围

2026-10-18 的本地核查结果：

- 已完整读取并计算 `ffmpeg.exe` 的 SHA-256，与上游固定清单一致；版本输出为 `n8.1.2-29-g703dcc25b9-20260721`，许可证输出为 GPLv3 或更新版本。
- 已完整下载并校验 16,877,286 字节的构建源码归档；其 `RELEASE_METADATA.txt` 指向相同的 FFmpeg 提交、构建脚本提交和可执行文件哈希。
- 已校验小源码包内的两个原始归档：BtbN 脚本归档为 `697d798f9031b5c5ead281761a79c87590e90677af4b28c3fa76f552dc16aedc`，FFmpeg 源码归档为 `1f67a24e928d263c74a4e468cbe63f7ccf116b54fdd39eccc80f9c9c537e1f34`。
- 已完整校验本项目重新打包的 FFmpeg 运行 ZIP。
- 对约 2 GB 的依赖源码归档，仅验证了公开下载可用、HTTP 范围读取成功，并检查了开头的 tar 条目；没有完整下载或在本地重算其整体 SHA-256。上方该文件的大小、哈希来自其发布者的公开发行元数据。

小源码包包含 `upstream-archives/` 下的固定版 FFmpeg 与 BtbN 归档，以及 `SOURCE_SHA256SUMS.txt`、`RELEASE_METADATA.txt` 和来源说明。大源码包提供 `downloads/` 源码缓存，包含构建脚本所需的依赖源码输入。两包需要配合使用；仅 FFmpeg 主仓库源码不包含所有静态链接依赖。

Open Video Craft 的 portable 包遗留了描述旧 Gyan 6.1.1 的 `ffmpeg.exe.README`，与被替换的 BtbN 可执行文件不同。本项目排除了该错误说明，使用本页和运行包中准确的来源记录。

## 重新构建时固定版本

使用小源码包中的 BtbN 脚本，在 Linux/Docker 环境交叉编译 Windows x64。将大源码包中的 `downloads/` 恢复到固定版 BtbN 工作目录的 `.cache/downloads/` 下，并使用 `win64 gpl 8.1` 配置。

上游 `addins/8.1.sh` 默认跟随会变化的 `release/8.1` 分支。重新构建这里的版本时，必须在 FFmpeg 的 configure 步骤之前明确切换到 **`703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d`**，不能直接采用当前分支最新版。原构建的 `--extra-version` 日期为 `20260721`；可从运行文件的 `ffmpeg -version` 和 `ffmpeg -buildconf` 读取编译配置。

本项目没有执行完整工具链重建，也没有验证位元级可复现性。固定源码提交和构建输入用于说明该运行文件的对应来源。

## 许可证与下载方式

完整许可证见运行包的 `LICENSE`，以及 [GNU GPLv3 官方文本](https://www.gnu.org/licenses/gpl-3.0.html)。[第 6(d) 节](https://www.gnu.org/licenses/gpl-3.0.html#section6)说明网络分发目标码时的源码获取方式：源码可在另一服务器提供，但下载入口旁须清楚说明获取位置，并继续确保其可获得。这里将运行包、构建源码镜像和依赖源码入口相邻列出，均无需额外费用；用户无需为了运行程序而同时下载源码。

FFmpeg 的原有版权、无担保条款及依赖项目许可证仍适用。更改或再分发源码时，请保留各源码包中的相应通知。

English: the separate FFmpeg executable is GPL-3.0-or-later. Its exact executable and build-source hashes were locally verified. The two linked source archives provide the pinned FFmpeg/build inputs and dependency source cache; normal use does not require downloading them. The large dependency archive was checked for download availability, not fully downloaded or locally hashed. GoggleCast's MIT license does not replace FFmpeg's license.
