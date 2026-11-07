# Viewcut Web 0.6.1

Viewcut 采用 GNU GPL v3.0（SPDX：GPL-3.0-only）。完整条款见随包提供的 LICENSE.txt 或仓库 LICENSE；第三方组件保留其各自许可证。

## 直接打开

- Windows 10 22H2 / Windows 11 x64：双击 `Viewcut.Web.exe`。
- macOS 15 或更新版本：解压对应 Apple Silicon (`osx-arm64`) 或 Intel (`osx-x64`) 的 ZIP，双击 `Viewcut Web.app`。可将整个应用拖入“应用程序”。

启动器内置网页资源和运行所需组件，不需要 Node、Python、.NET 安装或命令行。它只在本机回环地址启动服务，确认就绪后打开浏览器，重复启动会复用已有的同版本服务。Windows 优先使用默认 Chrome / Edge，或已安装的 Edge / Chrome；Mac 使用默认浏览器，支持 Safari、Chrome、Edge。

关闭全部 Viewcut 网页后，本地服务会自动退出；正常关闭通常约一分钟，强制结束浏览器或休眠后可能稍晚。正在使用的网页会维持服务。浏览器对后台网页的冻结策略可能影响生命周期，重新双击图标可恢复服务。端口占用时会选择其他本机端口，不会干扰占用者；不同端口的浏览器存储互相独立。

Chrome / Edge 支持授权目录直接导出。Safari 使用文件夹导入和 ZIP 下载：导入的源文件只读，目录写入、重命名、删除等操作请用 Finder；点击导出后，再点下载图标保存 ZIP。浏览器的下载设置决定保存位置。Mac 使用 Command 多选与 Command+Z / Command+Shift+Z 撤销重做。

当前 Mac 包为开发测试包，未使用 Apple Developer ID 签名与公证。首次打开可能被 Gatekeeper 拦截；请仅在确认来源后按 macOS“隐私与安全性”的提示允许打开。面向普通用户发布前应完成 Developer ID 签名、公证与实际 Safari 验收；不要通过关闭 Gatekeeper 解决。

## 开发构建

Windows 安装 .NET 10 SDK 和 Visual Studio C++ Build Tools 后：

```powershell
dotnet publish launcher/Viewcut.Web.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/WebLauncher/win-x64
```

在 Mac 上使用匹配架构的 .NET 10 SDK 与 Xcode Command Line Tools：

```sh
dotnet publish launcher/Viewcut.Web.csproj -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/WebLauncher/osx-arm64
python3 launcher/package-mac.py osx-arm64
```

Intel Mac 将 RID 改为 `osx-x64`。Windows 也可用自包含、裁剪、单文件模式交叉发布 Mac 版本；Native AOT 应在目标系统上编译。网页开发服务器 `web/server.mjs` 继续保留给开发者使用。

验证启动器：`node launcher/tests.mjs`。测试会使用本机 Chrome 和 Edge 的独立临时配置，检查实际自动打开浏览器、嵌入资源、重复启动复用与关闭后退出。macOS 自动化流程单独检查两个架构的启动文件和 WebKit；WebKit 与实际 Safari 的结果应分别说明。
