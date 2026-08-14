# DSH Desktop 桌面端说明

本目录包含 DeepSeek Harness WebUI 的 Windows 桌面封装：原生 WPF 窗口、WebView2 宿主、无终端后端启动、单实例激活、系统托盘和桌面快捷方式。

项目概览见 [根目录说明](../README.md)。以下命令都应在仓库根目录的 Windows PowerShell 中运行。

## 测试与构建

运行完整自动化测试，包括快捷方式元数据、仓库结构、后端进程和桌面行为测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
```

构建 64 位 Windows 图形程序并生成发布目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

构建结果位于：

```text
desktop\publish\DeepSeek Harness.exe
```

## 安装并启动桌面快捷方式

构建完成后，创建或更新 Windows 桌面上的 `DeepSeek Harness.lnk`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1
```

安装脚本会通过 Windows 获取真实桌面目录，只更新同名快捷方式，不删除其他快捷方式。快捷方式会写入以下信息：

- 目标：发布目录中的 `DeepSeek Harness.exe`。
- 工作目录：`desktop\publish`。
- 图标：`assets\branding\dsh-whale.ico`。
- 窗口样式：正常窗口，不显示终端。

之后直接双击桌面的 `DeepSeek Harness` 即可启动。也可以从仓库根目录直接运行：

```powershell
& '.\desktop\publish\DeepSeek Harness.exe'
```

再次启动快捷方式不会创建第二个窗口或第二个托盘图标，而是恢复已有实例。

## 启动流程

程序会并行初始化 WebView2 和本地 DSH 服务：

1. 如果 DSH 已经在 8080 端口正常运行，桌面程序会安全接入，不取得该进程的所有权。
2. 如果端口空闲，桌面程序会直接用 Node.js 启动 DSH，不弹出终端。
3. 3 秒内准备完成时直接显示 WebUI。
4. 超过 3 秒仍未完成时显示从左到右浮现的等待动画，不强制最短播放时间。
5. WebUI 首次显示时会同步 WPF 布局和 WebView2 原生窗口位置，避免离屏预加载后出现纯黑窗口。

## 窗口与托盘操作

关闭主窗口不会退出程序，而是把窗口隐藏到 Windows 通知区域。

- 双击 DeepSeek Harness 托盘图标，或选择“打开”，可立即恢复窗口且不播放启动动画。
- 选择“重启服务”，可重启由本桌面程序拥有的 DSH；接入外部 DSH 时该项不可用。
- 选择“查看日志”，会打开日志目录。
- 选择“退出”，会关闭桌面程序、移除托盘图标，并且只停止它自己启动的 Node/DSH 进程树。

重新构建、排查 8080 端口或彻底结束 DSH 前，请使用托盘“退出”。只关闭窗口不会释放发布 EXE 和后端进程。

## 日志位置

运行数据位于 `%LOCALAPPDATA%\DeepSeekHarness`：

- 桌面日志：`%LOCALAPPDATA%\DeepSeekHarness\logs\desktop.log`
- DSH 标准输出：`%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stdout.log`
- DSH 标准错误：`%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stderr.log`
- Node 编译缓存：`%LOCALAPPDATA%\DeepSeekHarness\cache\node-compile`
- WebView2 用户数据：`%LOCALAPPDATA%\DeepSeekHarness\webview2`

## 故障恢复

### 窗口黑屏

如果运行旧版后窗口仍停留在黑色背景：

1. 在系统托盘找到 DeepSeek Harness。
2. 选择“退出”，确保旧实例完全结束。
3. 重新运行 `desktop\build.ps1`。
4. 再运行快捷方式安装脚本，并双击新快捷方式。

当前版本会在窗口首次显示后刷新 WebView2 控制器位置；不需要再通过最大化或拖动窗口触发页面显示。

### 8080 端口冲突

如果 8080 端口返回的不是 DSH WebUI，程序会拒绝导航并显示冲突错误。退出或调整占用 8080 的程序，确认端口空闲后选择“重试”。

### WebView2 启动失败

安装或修复 Microsoft Edge WebView2 Runtime（Evergreen），然后选择“重试”。如果发布目录缺少以下文件，请重新构建：

- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.Wpf.dll`
- `WebView2Loader.dll`

### 找不到 Node.js 或 DSH

确认 `node.exe` 和 `dsh.cmd` 均可从 `PATH` 找到，并确认 DSH 安装位置包含：

```text
node_modules\@deepseek-ai\dsh\lib\bin.js
```

修复安装或 `PATH` 后，在错误页选择“重试”；需要完整重载环境时，从托盘退出后重新启动。

## 完全卸载快捷入口

项目不会自动删除桌面快捷方式。如需移除，只删除桌面上的 `DeepSeek Harness.lnk` 即可；这不会删除 DSH、Node.js、本仓库或 `%LOCALAPPDATA%\DeepSeekHarness` 中的本地数据。
