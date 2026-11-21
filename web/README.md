# Viewcut Web 0.6.2

项目采用 [GNU GPL v3.0](../LICENSE)（SPDX：`GPL-3.0-only`）。静态网页包包含 LICENSE 和 COPYRIGHT；第三方组件声明保留在 third-party-notices.txt。

Windows / macOS 浏览器的本地视频粗筛工具，面向 Chrome、Edge 和 macOS Safari。没有账号、上传接口或外部 CDN；视频、缩略图与导出都在用户设备上处理。页面文件可部署到任意 HTTPS 静态托管服务。

## 双击启动

普通用户直接双击 `Viewcut.Web.exe`（Windows），或解压后双击 `Viewcut Web.app`（Mac，分别提供 Apple Silicon / Intel 版本）。启动器带软件图标，内置网页和所需运行组件，会自动启动本地服务、打开浏览器，重复启动复用服务，关闭网页后自动退出。不需要先运行 CMD、启动服务器或安装 Node / .NET。详见 [启动器说明](../launcher/README.md)。

Mac 使用系统字体，支持 Command 多选、Command+Z 撤销、Command+Shift+Z 重做。Safari 通过文件夹选择器导入目录，并将全部选中片段打包成 ZIP 下载；下载后解压即可得到用户命名的视频。其源文件是只读的，不能在网页里直接重命名、移动或删除。Chrome / Edge 继续支持目录授权及直接写入保存目录。Safari 不支持的文件操作会禁用并说明原因。

Mac 包目前未经过 Developer ID 签名与公证，属于开发测试包；首次打开可能需要按系统安全提示允许。当前验证范围见仓库中的 macOS 自动化结果，WebKit 与实际 Safari 的验证分别记录。

## 开发者本机运行

安装 Node.js 22 或更新版本，在仓库根目录执行：

```powershell
node web/prepare-media.mjs
node web/server.mjs
```

用 Chrome 或 Edge 打开终端显示的 `http://127.0.0.1:4173`。也可运行 `web/Start-Web.ps1`。服务器仅监听本机回环地址，只提供静态页面，不读取素材、不接收视频或导出内容。关闭终端即可停止。不要通过双击 `index.html` 的 `file://` 地址运行：模块与目录权限需要 HTTPS 或 localhost 安全上下文。

静态部署需要包含生成的 ffmpeg-core.js、ffmpeg-core.wasm 和许可证。若部署端配置 CSP，请在 script-src 允许 wasm-unsafe-eval，并使用 application/wasm MIME 类型提供核心文件。

## 操作

- 在右侧用文件夹图标授权访问素材目录，或用视频图标选择一个或多个 MP4、MOV、M4V、MKV、WebM、AVI、TS 或 M2TS 视频。单击选中文件，双击加载视频；双击文件夹进入，面包屑、后退、前进与上一级用于导航。
- 顶栏只有选择保存目录、打开保存目录与偏好设置。浏览器中的“打开保存目录”会在右侧文件页显示目录。
- Space 播放／暂停，I 标记起点，O 标记终点；O 只创建片段。点击时间轴右上角导出图标，才会批量导出当前视频的全部片段。
- 鼠标悬停时间轴显示独立画面预览；拖动片段两端可调整范围并跟随预览。滚轮缩放，Shift + 滚轮平移。
- 片段右键重命名／删除，Ctrl 点击多选，Delete 或 Backspace 删除。自定义名同时用于时间轴和导出；空名恢复默认名。重名会提示，导出不会主动覆盖已有文件。
- Ctrl+Z 撤销；Ctrl+Y / Ctrl+Shift+Z 重做。覆盖片段新增、删除、命名及边界调整；一轮拖动或批量删除各算一次操作。文件操作和已导出文件不纳入片段撤销。
- 拖动竖／横分隔条调整三个分区；分隔条也可聚焦后用方向键调整。偏好设置中切换中英文、深浅主题或跟随系统。
- 文件页支持大图标缩略图／列表、当前视频高亮、搜索、多选、Ctrl / Shift 连选、空白处框选、全选、反选，以及右键复制、剪切、粘贴、重命名、新建文件夹、属性和永久删除。复制粘贴使用 Viewcut 内部文件剪贴板，无法替代 Windows Shell 剪贴板。
- 文件快捷键：Ctrl+A / C / X / V，F2，Delete，F5，Ctrl+Shift+N；Alt+方向键导航，Alt+Enter 属性。地址栏之外的 Backspace 用于上一级；时间轴与片段页中的 Delete / Backspace 只删片段标记。

片段标记和设置保存在此浏览器的本地存储中，最近 64 条素材的草稿会保留。撤销历史只在当前页面生命周期保留，每条素材最多 100 步。目录句柄保存在 IndexedDB；重新打开时，浏览器可能要求重新授权。Chrome 与 Edge 的本地存储互相独立，也不与桌面版自动同步。

## 无损导出与边界

`mp4.js` 读取 MP4 / MOV 的 moov 索引和样本表，使用 64 位源偏移定位数据；导出由 Worker 执行，通过 File System Access API 分块写入目标目录，不把整段视频读入内存，也不重新编码。大图标缩略图按可见项排队生成，最多缓存 64 张。

与桌面版快速无损剪切一样，起点会向前对齐到可用关键帧，因此输出可能包含标记起点之前的少量画面。保留视频／音频样本描述与编码数据，重新生成时间表、编辑列表和块偏移；输出会使用用户标记的结束时间。非音视频的时间码、遥测等附加轨道不导出。

常见 MP4、MOV 和 M4V 使用快速索引导出。MKV、WebM、AVI、TS、M2TS 和部分特殊 MP4/MOV 使用按需加载的 FFmpeg WebAssembly，媒体数据始终留在本机。导出保留原容器并复制音视频。浏览器无法直接解码时，会先生成最高 720p 的本地 H.264/AAC 预览。预览与每个 WebAssembly 输出最多约 256 MiB；较长或大型素材建议用桌面版。导出使用原始视频数据，兼容预览的画质不会影响导出。

浏览器文件访问受授权范围限制，无法提供 Windows 全磁盘树、原生文件右键扩展、回收站、资源管理器右键入口或相同的系统文件关联。文件删除会再次确认并明确说明永久删除。复制／重命名文件夹通过复制后删除完成，失败时原始项目保留；中断的复制可能留下已复制部分。导出取消会中止当前文件并清理该文件，之前已完成的片段会保留。应保持页面打开，避免导出时关闭浏览器。

## 验证

```powershell
node --test web/tests/model.test.mjs web/tests/media.test.mjs
node web/tests/browsers.mjs
node web/tests/formats.mjs
```

媒体测试需要 `.tools/ffmpeg/ffmpeg.exe` 和 ffprobe，或用 `VIEWCUT_TEST_TOOLS` 指向完整 FFmpeg。浏览器测试需要 Playwright（用 `VIEWCUT_PLAYWRIGHT` 指定 `playwright/index.mjs`），直接使用本机安装的 Chrome 与 Edge，不下载浏览器，也不使用日常浏览器配置。浏览器原生目录选择器由测试替代，返回真实 OPFS 目录句柄；之后的读写、复制、删除、Worker 导出均走真实浏览器 API。

测试覆盖关键帧、B 帧、AAC 同步、HEVC MOV、fast-start、逐包数据一致、取消、64 位大文件偏移，以及两浏览器的片段操作、文件操作和 100 / 125 / 150 / 175 / 200% 显示缩放。可通过 `VIEWCUT_LARGE_FIXTURE` 指定大视频，额外测试直接播放与导出。测试结果与截图在 `artifacts/web-browsers/`，导出样片经独立 FFmpeg 完整解码验证。
