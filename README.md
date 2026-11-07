# Viewcut 0.6.1

简体中文 · [English](README.en.md)

Viewcut 是面向 Windows 10 22H2／Windows 11 x64 的本地视频粗筛工具。Windows 10 实机验收尚待完成，见 [兼容性说明](docs/windows-compatibility.md)。浏览相机或无人机的 MP4／MOV，标记要保留的片段，选完一条素材后一次导出到指定目录。

## 最新版下载

| 版本 | 下载 |
| --- | --- |
| Windows 桌面版 · Win10 22H2 / Win11 x64 | [下载安装包](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Setup-x64.exe) |
| 网页版启动器 · Windows x64 | [下载单文件 EXE](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-Windows.exe) |
| 网页版启动器 · Mac Apple Silicon | [下载 Mac 应用](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-macOS-AppleSilicon.zip) |
| 网页版启动器 · Mac Intel | [下载 Mac 应用](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-macOS-Intel.zip) |

[全部发布文件与更新说明](https://github.com/Uranatom/Viewcut/releases/latest) · [自行部署的静态网页包](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-Static.zip)

## 桌面版 Quick Start

1. 下载并运行 Windows 安装包，选择安装目录和是否创建桌面快捷方式，然后打开 Viewcut。
2. **将 MP4 / MOV 直接拖到播放器区域**；也可在右侧文件页中双击视频。
3. 用 Space 播放／暂停，按 **I** 标记起点、按 **O** 标记终点；重复操作可保留多个片段。O 只做标记，不会自动导出。
4. 选择保存目录，点击时间轴右侧的**导出图标**，一次导出当前视频的全部片段。按钮名称和快捷键可通过鼠标悬停查看。

## 网页版 Quick Start

1. Windows 下载 `Viewcut-Web-Windows.exe`；Mac 下载对应架构的 ZIP 并解压，得到 `Viewcut Web.app`。
2. **双击图标，浏览器界面自动打开**。不用启动 CMD，不用安装 Node 或 .NET，也不用手动输入网址。
3. 将视频拖到播放器区域，或在右侧打开文件夹／视频。支持 Chrome、Edge 和 Mac Safari。
4. 按 I / O 选择一个或多个片段，再点击导出。**Chrome / Edge** 可直接保存到授权目录；**Safari** 会准备一个 ZIP，点击下载后解压得到全部片段。
5. Mac 用 **Command+点选**多选，**Command+Z** 撤销，**Command+Shift+Z** 重做。主题和语言在偏好设置中切换。

视频始终在本机处理，不上传。Mac 启动包目前为未公证测试版，首次打开可能需要按系统安全提示允许；Mac 端的浏览器与验收范围见[启动器说明](launcher/README.md)。Windows 10 实机验收状态见[兼容性说明](docs/windows-compatibility.md)。

## 详细操作

1. 运行 `artifacts\Setup\Viewcut-0.6.1-Setup-x64.exe`，选择安装位置和快捷方式，再从开始菜单打开 Viewcut。也可以直接运行 `artifacts\Viewcut\Viewcut.exe`。
2. 在右侧「文件」浏览文件夹。可用地址栏、常用位置或磁盘入口导航，支持后退、前进、上一级、刷新和当前目录搜索。「大图标」与「列表」按钮可切换视图。双击文件夹进入，双击 MP4／MOV 后加载到播放器。外部右键视频仍可直接播放，右键文件夹则打开文件浏览器。
3. 用空格键播放／暂停，左右方向键跳转三秒。按 `I` 标记起点，按 `O` 标记终点，按 `N`／`P` 查看下一条／上一条。标记会自动保存，选完后点击「导出当前素材全部片段」开始批量导出，每段生成一个文件。首次导出时可选择保存目录。
4. 时间轴上的色块代表要保留的片段。悬停在时间轴上可查看对应画面，点击可移动播放头。点选色块后拖动左、右手柄，预览会跟随边界。滚轮缩放时间轴，按住 Shift 滚轮平移，「全览」恢复整条素材。
5. 右侧「片段」列出当前素材的时间范围，双击可跳到片段起点。拖动手柄松手后保存新的标记，点击导出时使用最新范围。「导出」页可查看路径、状态和失败原因，并重试失败的任务。排队中的片段使用提交时的范围，继续修改标记后可再次点击导出。
6. 拖动侧栏左边的竖线可调整宽度，拖动时间轴上方的横线可调整播放器和时间轴的高度。软件会记住分区比例、浏览目录、查看方式和保存目录。侧栏会标出当前正在处理的视频。
7. 右键时间轴上的片段可重命名。名称同时用于时间轴、片段列表和导出文件，留空可恢复默认名称。同名时会提示换名。
8. 右键片段选择「删除」，或选中后按 Delete／Backspace，可删除片段标记。按住 Ctrl 点击时间轴上的多个片段，或在侧栏「片段」列表多选后，可使用「删除选中片段」或键盘批量删除。已提交的导出任务和导出的视频会保留。
9. 操作按钮统一以图标显示，鼠标悬停可查看名称和快捷键；片段和导出数量显示在角标中。点击顶部与保存目录按钮并列的「偏好设置」，可即时切换简体中文／English，以及深色／浅色／跟随系统主题。设置自动保存，切换不会重新加载视频或更改片段、导出任务。

导出采用无损快切，起点可能提前到附近的关键帧。输出保留 MP4／MOV 容器及主视频、音频轨。标记、保存目录和任务记录位于 `%LOCALAPPDATA%\Viewcut\session.json`。旧版片段记录会自动恢复成可编辑标记。

文件浏览器的大图标优先使用 Windows 已缓存的缩略图，没有缩略图时显示视频图标。

## 撤销与重做

时间轴工具栏提供撤销、重做图标。Ctrl+Z 撤销，Ctrl+Y 或 Ctrl+Shift+Z 重做。支持新增片段、单个或多选删除、重命名及首尾拖动；多选删除和一次完整拖动各计为一次操作。发生新编辑后清空重做分支。

历史按素材分别记录，本次运行中保留最近 64 条素材、每条最多 100 次操作。关闭软件后撤销历史清空，已保存的片段标记保留。撤销仅修改片段草稿，已提交的导出任务和已导出视频保持原样。编辑地址、搜索或文件名时，Ctrl+Z 留给文本编辑。

## 语言与主题

语言覆盖工具栏、文件页、片段列表、时间轴、Viewcut 自有右键菜单、重命名弹窗、状态和错误提示。文件名和用户自定义片段名保持原样。Windows 原生右键菜单、复制进度和系统对话框沿用 Windows 的显示语言。

浅色主题使用浅灰背景、白色工作面、深色文字和蓝色强调色，并适配标题栏、文件列表、按钮、时间轴与片段手柄。视频区域保留黑色背景。选择「跟随系统」后，会响应 Windows 应用主题切换。中英文与深浅色组合均检查 100%、125%、150%、175%、200% 缩放下的宽窄窗口布局。

## 侧栏文件管理

文件页支持鼠标拖动框选、Ctrl 点选、Shift 连选、Ctrl+A 全选和反向选择，大图标与列表视图均可使用。右键选中文件会打开 Windows 原生文件菜单，包含系统关联的打开方式及已安装的文件扩展菜单；空白处或「…」按钮可打开常用操作菜单。

| 操作 | 快捷键 |
| --- | --- |
| 复制／剪切／粘贴文件及文件夹 | Ctrl+C／Ctrl+X／Ctrl+V |
| 复制文件完整路径 | Ctrl+Shift+C |
| 删除到回收站 | Delete |
| 永久删除，需再次确认 | Shift+Delete |
| 原位重命名／多文件批量编号命名 | F2 |
| 新建文件夹 | Ctrl+Shift+N |
| 属性 | Alt+Enter |
| 刷新 | F5 |
| 地址／搜索 | Ctrl+L／Ctrl+F |
| 后退／前进／上一级 | Alt+Left／Alt+Right／Alt+Up 或 Backspace |

可与 Windows 资源管理器互相复制粘贴、拖入拖出文件，也可拖到侧栏显示的子文件夹。默认拖动遵循 Windows 的同盘移动、跨盘复制习惯，Ctrl 强制复制，Shift 强制移动。文件冲突、权限、进度和取消使用 Windows Shell 对话框；目录变化会自动刷新，用户主动切换目录优先于刷新。

文件页的 Delete 操作针对磁盘文件；「片段」页和时间轴中的 Delete 只移除片段标记。SD 卡、网络位置等不支持回收站时，Windows 会在永久删除前提示。

## 体积优化

0.4.0 自带精简 FFmpeg 和 ffprobe，专用于 MP4／MOV 的无损复制剪切及关键帧检查。两个工具合计约 11 MB，之前约 201 MB；完整 libmpv 播放器保持不变，不影响视频查看和悬停预览。安装包仍自带 .NET Desktop Runtime，可离线安装运行。

## 资源管理器菜单

安装向导可为当前用户添加右键菜单。直接使用便携版本时，也可在项目目录运行以下命令。Windows 11 中可在「显示更多选项」找到。

```powershell
pwsh -NoProfile -File .\Register-Explorer.ps1
```

- 右键 MP4／MOV，选「用 Viewcut 粗筛」。
- 右键素材文件夹图标，在菜单靠上位置选「粗筛此文件夹」。

如果已经打开的资源管理器窗口仍缺少菜单项，可在任务管理器中重启「Windows 资源管理器」，再打开素材所在目录。

运行 `pwsh -NoProfile -File .\Register-Explorer.ps1 -Remove` 可移除这些入口。菜单注册独立于 Windows 的默认应用设置。

## 卸载

从 Windows 设置的「应用 → 已安装的应用」或开始菜单的「卸载 Viewcut」启动卸载。卸载会清理安装文件、快捷方式和安装时添加的右键菜单。可以同时清除本地片段标记、队列和设置。源视频和已导出的视频会保留。

## 构建与测试

使用 .NET 10 SDK，把 x64 播放依赖放在 `.tools\mpv\libmpv-2.dll`，精简媒体工具及许可证放在 `.tools\ffmpeg-lite\bin`，运行 `pwsh -NoProfile -File .\Build.ps1`。也可用 `-MediaTools` 指定另一套 FFmpeg 目录。SDK 可以安装在系统中，也可以放在 `.tools\dotnet`。

精简媒体工具的官方源码版本、校验值、编译方法见 [media/README.md](media/README.md)，脚本为 `media/Build-FFmpegLite.sh`。请使用完整 FFmpeg 构建生成测试素材，并通过 `VIEWCUT_EXPORT_TOOLS` 指向精简工具验证导出；测试输出目录的 FFmpeg／ffprobe 同样使用精简版本，以覆盖 UI 导出路径。

使用 Inno Setup 6.7 编译安装包，运行 `pwsh -NoProfile -File .\installer\Build-Setup.ps1`。也可通过 `-Iscc` 指定编译器路径。生成的安装包自带运行时和媒体依赖，安装时可离线使用。

冒烟测试在 `tests\Viewcut.Smoke.csproj`。先构建测试项目，将三个媒体二进制文件复制到测试项目的输出目录，再运行 `Viewcut.Smoke.exe <媒体工具所在目录> <空的测试目录>`。测试会生成 H.264 MP4、HEVC MOV 样片，检查单击选择、双击播放、目录导航与搜索、两种查看方式、侧栏缩放、标记与批量导出、独立悬停预览、首尾拖动和旧版数据迁移。

本地使用的 libmpv 为 shinchiro 20261106（9028187538）。发布包的 FFmpeg 为官方 release/8.1 分支 a35157abb9 的精简编译；完整 Gyan 9.0.2 essentials 只用于测试素材生成和独立解码验证。对外发布时需按所选构建处理第三方依赖的许可证和源码分发要求。 [libmpv 测试构建](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261106) · [FFmpeg 构建来源](https://www.gyan.dev/ffmpeg/builds/)
