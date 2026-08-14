# DSH Desktop

DSH Desktop 是一个轻量的 Windows 桌面封装器，用原生 WPF 窗口承载 DeepSeek Harness（DSH）已有的 WebUI。它不会修改 DSH 的前端，也不包含视觉模型、图片识别 MCP 或其他独立扩展。

推荐的 GitHub 仓库名是 `dsh-desktop`。

## 功能

- 使用 .NET Framework 4.8、WPF 和 Microsoft WebView2 提供原生桌面窗口。
- 直接启动 Node.js 后端，不弹出命令提示符或 PowerShell 窗口。
- 只允许一个桌面实例；再次启动会恢复已有窗口。
- 关闭窗口后驻留系统托盘，可从托盘打开、重启服务、查看日志或完全退出。
- 只停止由桌面程序自己启动的 DSH，不会误关用户独立启动的服务。
- 启动在 3 秒内完成时直接显示 WebUI；超过 3 秒才显示从左到右浮现的等待动画。
- 对缺少 Node.js、缺少 DSH、8080 端口冲突、启动超时和 WebView2 故障给出可操作的错误提示。

## 运行条件

- 64 位 Windows。
- 已安装 .NET Framework 4.8。
- 已安装 Microsoft Edge WebView2 Runtime（Evergreen）。
- `node.exe` 和 `dsh.cmd` 可从 `PATH` 找到，且 DSH 包完整安装在 `dsh.cmd` 对应位置。
- 第一次构建时，如果本地尚无固定版本的 WebView2 SDK 包，需要联网下载依赖。

本仓库不会安装 DeepSeek Harness、Node.js、模型配置或 WebView2 Runtime。

## 快速开始

在仓库根目录打开 Windows PowerShell，依次运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1
```

完成后，双击桌面的 `DeepSeek Harness` 快捷方式即可启动。发布目录中的可执行文件是：

```text
desktop\publish\DeepSeek Harness.exe
```

## 启动速度

正常缓存后的冷启动验收目标是本机约 5～6 秒，这不是对所有电脑的性能保证。等待时间不超过 3 秒时不会播放启动动画；更慢时才显示等待界面，后端和 WebView2 会继续并行初始化。

## 窗口与托盘

- 点击窗口右上角关闭按钮只会隐藏窗口，程序和 DSH 仍在后台运行。
- 双击托盘图标，或在托盘菜单选择“打开”，会立即恢复已有窗口且不播放动画。
- “重启服务”只在当前 DSH 由桌面程序启动时可用。
- “查看日志”会打开本地日志目录。
- “退出”会移除托盘图标，并停止桌面程序拥有的 DSH 进程树。

需要重新构建、彻底结束 DSH 或处理 8080 端口问题时，请从托盘选择“退出”，不要只关闭窗口。

## 日志与本地数据

运行数据位于 `%LOCALAPPDATA%\DeepSeekHarness`：

- `%LOCALAPPDATA%\DeepSeekHarness\logs\desktop.log`
- `%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stdout.log`
- `%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stderr.log`
- `%LOCALAPPDATA%\DeepSeekHarness\cache\node-compile\`
- `%LOCALAPPDATA%\DeepSeekHarness\webview2\`

## 项目结构

```text
dsh-desktop/
|-- assets/branding/                  # 应用图标源文件
|-- desktop/
|   |-- assets/                       # 清单与 WebUI 图标
|   |-- runtime/                      # DSH 后端启动脚本
|   |-- src/                          # WPF、WebView2、托盘与后端管理源码
|   |-- tests/                        # 自动化与结构测试
|   |-- build.ps1                     # 构建并生成发布目录
|   |-- install-desktop-shortcut.ps1  # 创建或更新桌面快捷方式
|   |-- test.ps1                      # 运行完整测试
|   `-- README.md                     # 桌面端详细使用说明
|-- tools/make-icon.ps1               # 重新生成应用图标
|-- .gitignore
`-- README.md
```

`desktop\packages\`、`desktop\publish\`、`desktop\obj\` 和 `desktop\test-output\` 都是本地生成目录，不进入 Git。

## 故障排查

- 窗口一直黑色：先从托盘选择“退出”，再重新构建并启动最新版。本项目已在首次显示 WebUI 时主动同步 WebView2 布局，避免离屏预加载后的黑屏。
- 8080 端口被其他程序占用：退出占用者后，在错误页选择“重试”。
- WebView2 无法启动：安装或修复 Microsoft Edge WebView2 Runtime，再选择“重试”。
- 找不到 Node.js 或 DSH：确认 `node.exe`、`dsh.cmd` 在 `PATH` 中，并检查 DSH 安装目录是否包含 `node_modules\@deepseek-ai\dsh\lib\bin.js`。

更详细的启动、托盘和恢复说明见 [桌面端说明](desktop/README.md)。

## 已知限制

- 仅支持 64 位 Windows。
- DSH 地址目前固定为 `http://127.0.0.1:8080/`。
- 仓库只负责把现有 DSH WebUI 封装为桌面应用，不提供视觉识别或其他 MCP 扩展。

## 上游与归属

DeepSeek Harness 是独立的上游依赖，本仓库不捆绑其源码。DSH Desktop 是第三方兼容桌面壳，不代表 DeepSeek 或 DSH 上游官方项目，也不表示获得其认可。
