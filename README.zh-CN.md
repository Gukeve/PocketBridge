# PocketBridge

[English](README.md) · [Русский](README.ru.md)

PocketBridge 是一款独立的开源 Windows 应用，通过 ADB 和官方 scrcpy 运行时管理 Android 设备。本项目与 Genymobile 无关联，未经其赞助或认可。

## 功能

- 在同一设备列表中支持 USB 和经典 ADB TCP/IP；
- 所有设备命令都显式绑定所选序列号，并支持多个独立 scrcpy 会话；
- Android 按键、屏幕截图和确认后重启；
- 通过文件选择器或拖放安装 APK；
- `/storage/emulated/0` 范围内的 ADB 文件上传、下载、新建文件夹、重命名和删除；
- 俄语、英语和简体中文界面；
- 自动安装运行时，无需手动选择 `adb.exe` 或 `scrcpy.exe`。

## 运行时

公开发布的 PocketBridge ZIP 不捆绑第三方 native 二进制文件。应用或 `prepare-runtime.ps1` 会直接下载并校验固定版本：Genymobile 官方 GitHub Releases 中的 scrcpy Windows x64 4.1，以及 Google 官方地址中的 Android SDK Platform-Tools 37.0.0。组件分别保存在 `runtime/scrcpy` 和 `runtime/platform-tools`。旧版 `tools` 目录仅作为兼容后备。

## 显示模式

**连接**会启动官方 `scrcpy-server` 4.1 协议的内置客户端。ADB 隧道、视频/控制 socket、FFmpeg H.264 解码与 WPF 渲染都在 PocketBridge 进程内完成，并支持鼠标、滚动、文本和键盘输入。官方 scrcpy 独立窗口仅作为明确的备用操作保留；PocketBridge 不使用 `SetParent`。详见 [docs/EMBEDDED_DISPLAY.md](docs/EMBEDDED_DISPLAY.md)。

## 构建

安装当前的 .NET 8 SDK，然后从源码运行：

```powershell
git clone https://github.com/Gukeve/PocketBridge.git
cd PocketBridge
git switch next
.\run.ps1
```

也可以直接运行：

```powershell
dotnet run --project .\PocketBridge.App\PocketBridge.App.csproj -c Release
```

`global.json` 要求 .NET 8 SDK `8.0.100` 或更高的兼容 .NET 8
feature/patch 版本；应用目标框架仍为 `net8.0-windows`。

```powershell
dotnet restore .\PocketBridge.sln --locked-mode
dotnet build .\PocketBridge.sln -c Release --no-restore
dotnet run --project .\PocketBridge.Tests\PocketBridge.Tests.csproj -c Release --no-build
.\publish-local.ps1
```

可运行程序位于 `publish\PocketBridge.App.exe`。这是依赖框架的 Windows
x64 构建，需要 .NET 8 Desktop Runtime；`publish/` 不会提交到 Git。

## 首次启动

未连接手机时 PocketBridge 也应正常启动并显示空状态。如果缺少 native
运行时，请使用应用内安装操作；PocketBridge 会下载并验证官方 scrcpy 和
Android Platform-Tools。随后启用 USB 调试、连接设备，并在 Android
设备上接受 ADB 授权。未授权和离线设备会显示不同的说明。

第三方许可信息见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
