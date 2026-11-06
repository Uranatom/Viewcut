# Viewcut 0.3

Viewcut 是 Windows 11 x64 上的本地视频粗筛工具。浏览相机或无人机的 MP4／MOV，标记要保留的片段，选完一条素材后一次导出到指定目录。

## 试用

1. 打开 `artifacts\Viewcut\Viewcut.exe`。这个测试版本自带 .NET Desktop Runtime、libmpv 和 FFmpeg。
2. 在右侧「文件」浏览文件夹。可用地址栏、常用位置或磁盘入口导航，支持后退、前进、上一级、刷新和当前目录搜索。「大图标」与「列表」按钮可切换视图。双击文件夹进入，双击 MP4／MOV 后加载到播放器。外部右键视频仍可直接播放，右键文件夹则打开文件浏览器。
3. 用空格键播放／暂停，左右方向键跳转三秒。按 `I` 标记起点，按 `O` 标记终点，按 `N`／`P` 查看下一条／上一条。标记会自动保存，选完后点击「导出当前素材全部片段」开始批量导出，每段生成一个文件。首次导出时可选择保存目录。
4. 时间轴上的色块代表要保留的片段。悬停在时间轴上可查看对应画面，点击可移动播放头。点选色块后拖动左、右手柄，预览会跟随边界。滚轮缩放时间轴，按住 Shift 滚轮平移，「全览」恢复整条素材。
5. 右侧「片段」列出当前素材的时间范围，双击可跳到片段起点。拖动手柄松手后保存新的标记，点击导出时使用最新范围。「导出」页可查看路径、状态和失败原因，并重试失败的任务。排队中的片段使用提交时的范围，继续修改标记后可再次点击导出。
6. 拖动播放器与侧栏之间的竖线可调整比例。软件会记住浏览目录、查看方式、侧栏宽度和保存目录。

导出采用无损快切，起点可能提前到附近的关键帧。输出保留 MP4／MOV 容器及主视频、音频轨。标记、保存目录和任务记录位于 `%LOCALAPPDATA%\Viewcut\session.json`。旧版片段记录会自动恢复成可编辑标记。

文件浏览器的大图标优先使用 Windows 已缓存的缩略图，没有缩略图时显示视频图标。

## 资源管理器菜单

在项目目录运行以下命令。脚本为当前用户注册两个菜单入口并通知资源管理器刷新。Windows 11 中可在「显示更多选项」找到。

```powershell
pwsh -NoProfile -File .\Register-Explorer.ps1
```

- 右键 MP4／MOV，选「用 Viewcut 粗筛」。
- 右键素材文件夹图标，在菜单靠上位置选「粗筛此文件夹」。

如果已经打开的资源管理器窗口仍缺少菜单项，可在任务管理器中重启「Windows 资源管理器」，再打开素材所在目录。

运行 `pwsh -NoProfile -File .\Register-Explorer.ps1 -Remove` 可移除这些入口。菜单注册独立于 Windows 的默认应用设置。

## 构建与测试

使用 .NET 10 SDK，把 x64 媒体依赖放在 `.tools\mpv\libmpv-2.dll` 和 `.tools\ffmpeg\{ffmpeg,ffprobe}.exe`，运行 `pwsh -NoProfile -File .\Build.ps1`。SDK 可以安装在系统中，也可以放在 `.tools\dotnet`。

冒烟测试在 `tests\Viewcut.Smoke.csproj`。先构建测试项目，将三个媒体二进制文件复制到测试项目的输出目录，再运行 `Viewcut.Smoke.exe <媒体工具所在目录> <空的测试目录>`。测试会生成 H.264 MP4、HEVC MOV 样片，检查单击选择、双击播放、目录导航与搜索、两种查看方式、侧栏缩放、标记与批量导出、独立悬停预览、首尾拖动和旧版数据迁移。

本地测试用的 libmpv 为 shinchiro 20261106（9028187538），FFmpeg 为 Gyan 9.0.2 essentials。对外发布时需按所选构建处理第三方依赖的许可证和源码分发要求。 [libmpv 测试构建](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261106) · [FFmpeg 构建来源](https://www.gyan.dev/ffmpeg/builds/)
