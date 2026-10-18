# Windows 离线一键安装包

下载 Release 中的 `GoggleCast-0.1.0-Setup.exe`，打开后可修改安装目录，点击“立即安装”。安装器为当前 Windows 用户安装，不要求管理员权限；自动创建桌面和开始菜单快捷方式，并注册系统卸载入口。安装界面支持简体中文、英文，应用界面保留八种语言。

安装器已经包含 FFmpeg 9.0.2，安装全过程无需联网，也不需要另装编码器或解压工具。发行构建阶段先校验上游固定版本归档的完整 SHA-256，再从中取出必需文件并编入安装器。用户安装时使用 Inno Setup 自带的文件校验、解压和回滚机制。

安装后的 `runtime` 文件夹包含 `ffmpeg.exe`、上游 `FFmpeg-LICENSE.txt`、`FFmpeg-README.txt` 和固定来源信息 `runtime-source.json`。FFmpeg 使用独立许可证，详见项目 `THIRD-PARTY-NOTICES.txt`；轻量便携包仍不含 FFmpeg。程序所需 .NET Framework 4.8 由系统提供，安装器会检查缺失并提示微软下载地址，不在安装时下载系统组件。

安装器默认使用当前用户的应用目录，也可以选 D 盘等有写入权限的位置。升级时识别上次安装位置，覆盖程序与随包运行时并保留用户 `settings.xml`。重新运行安装器也可以修复缺失或损坏的随包运行时，无需联网。

卸载可以使用 Windows“已安装的应用”、开始菜单的卸载入口，或安装目录中的 `unins000.exe`。卸载移除由安装器安装的文件与快捷方式，保留应用自行生成的设置和日志，因此目录可能保留。卸载后如需删除这些个人文件，可以自行删除残留目录。

## 构建

需要 Windows、Windows PowerShell 5.1、系统 `tar.exe` 和 [Inno Setup 6.7 或更新版本](https://jrsoftware.org/isdl.php)。提前准备 `installer/runtime.json` 指定的 FFmpeg 归档；构建脚本不会自动下载依赖。系统 `tar.exe` 只在构建时用于取出归档中指定文件，不是用户安装的依赖。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -Iscc 'D:\Tools\Inno Setup 6\ISCC.exe' -RuntimeArchive 'D:\Downloads\ffmpeg-9.0.2-essentials_build.7z'
```

编译结果：`artifacts\GoggleCast-0.1.0-Setup.exe` 和对应 `.sha256`。`-RuntimeArchive` 必须指向固定归档；可使用 `-AppDirectory` 指定已构建程序目录、`-SkipBuild` 避免重复构建、`-Output` 指定产物目录。构建所用临时解压文件在编译后清理。FFmpeg 固定元数据在 `installer/runtime.json`；更新版本时须核对上游归档的完整哈希及内部文件布局，禁止改为未固定校验值的 `latest` 地址。

```powershell
# 静默安装到自己的测试目录；/LOG 便于核对升级和卸载。
.\artifacts\GoggleCast-0.1.0-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="D:\Test\GoggleCast" /LOG="D:\Test\gogglecast-setup.log"

# 静默卸载；保留应用自行创建的设置、日志。
D:\Test\GoggleCast\unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

GUI 可用 `/LANG=chinesesimplified` 或 `/LANG=english` 测试。静默模式失败时请检查非零退出码和日志，不能仅以窗口消失判断成功。安装后应核对运行时来源、启动应用和卸载入口；正式发布前还应测试同目录升级、取消、完全离线安装、快捷方式和保留设置。

官方接口参考：[Inno Setup 文件安装与回滚](https://jrsoftware.org/ishelp/topic_filessection.htm)、[安装命令行参数](https://jrsoftware.org/ishelp/topic_setupcmdline.htm)。