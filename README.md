# Viewcut 0.1

Viewcut 是 Windows 11 x64 上的本地视频粗筛工具。打开相机或无人机的 MP4／MOV，即可逐条播放同目录素材，并把选中的片段后台写入指定目录。

## 试用

1. 打开 `artifacts\Viewcut\Viewcut.exe`。这个测试版本自带 .NET Desktop Runtime、libmpv 和 FFmpeg。
2. 打开一条视频或素材文件夹。建议把保存目录选到电脑的 SSD。
3. 用空格键播放／暂停，左右方向键跳转三秒。按 `I` 标记起点，按 `O` 保存该片段，按 `N`／`P` 查看下一条／上一条。按 `X` 标记跳过并进入下一条。
4. 「导出」页显示片段状态。鼠标悬停可查看保存路径或错误信息，失败的任务可选中重试。

默认无损快切，起点可能提前到附近的关键帧。「按标记导出」会重编码片段，支持 8 位 SDR 4:2:0。对于 10 位或 HDR 素材，请使用无损快切。输出保留 MP4／MOV 容器及主视频、音频轨。选定的保存目录和任务记录位于 `%LOCALAPPDATA%\Viewcut\session.json`。

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

冒烟测试在 `tests\Viewcut.Smoke.csproj`。先构建测试项目，将三个媒体二进制文件复制到测试项目的输出目录，再运行 `Viewcut.Smoke.exe <媒体工具所在目录> <空的测试目录>`。测试会生成 H.264 MP4、HEVC MOV 样片，检查嵌入播放、定位、导出、恢复以及窗口的文件和文件夹入口。

本地测试用的 libmpv 为 shinchiro 20261106（9028187538），FFmpeg 为 Gyan 9.0.2 essentials。对外发布时需按所选构建处理第三方依赖的许可证和源码分发要求。 [libmpv 测试构建](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261106) · [FFmpeg 构建来源](https://www.gyan.dev/ffmpeg/builds/)
