# DSH Desktop

DSH Desktop 是一个面向 Windows 的 DeepSeek Harness 桌面壳。它在后台启动或接入本机 DSH Web 服务，并通过原生 WPF 窗口与 WebView2 展示已有 WebUI，让日常使用不再依赖终端窗口和浏览器标签页。

> 本项目是第三方桌面伴生应用，不是 DeepSeek 官方项目，也不包含或修改 DeepSeek Harness 源码。

## 功能

- 原生 Windows 窗口承载 DSH WebUI，不弹出命令提示符或 PowerShell 窗口。
- 单实例运行；重复启动会直接恢复已有窗口。
- 关闭窗口后驻留系统托盘，可打开窗口、重启服务、查看日志或完全退出。
- 只管理由桌面程序自己启动的 DSH 进程，不会终止用户独立运行的 DSH。
- 启动在 3 秒内完成时直接显示 WebUI；超过 3 秒才显示无边框启动动画。
- WebView2 和 DSH 后端并行初始化，正常缓存后的冷启动目标约为 5～6 秒。

## 与 DeepSeek Harness 的关系

[DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 是本项目所承载的上游核心。DSH Desktop 只负责桌面窗口、后端启动、单实例和托盘生命周期，实际对话、工具、会话与 WebUI 均来自本机安装的 DSH。

DSH 官方架构中的“插件”是挂载进 Cordis 插件树、向 DSH 共享上下文贡献服务、事件或可逆副作用的组件。DSH Desktop 不会被 DSH 加载，也不加入该插件树，因此它不是 DSH 插件，不能作为插件直接安装；更准确的定位是独立的 Windows 桌面宿主。插件机制详见 [DeepSeek Harness 架构说明](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/architecture.zh.md)。

## 运行条件

- 64 位 Windows。
- .NET Framework 4.8。
- Microsoft Edge WebView2 Runtime。
- 已通过 npm 全局安装并配置 DeepSeek Harness，`node.exe` 与 `dsh.cmd` 可从 `PATH` 找到。

官方 README 使用 `npx` 直接运行 DSH，但本桌面壳需要可被独立发现的全局 `dsh.cmd`。首次使用请运行：

```powershell
npm install --global @deepseek-ai/dsh@latest
dsh --version
```

本项目不会安装 Node.js、DeepSeek Harness 或模型配置。上游配置与使用方式请参考 [DeepSeek Harness 官方说明](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md)。

## 快速开始

在仓库根目录打开 Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1
```

完成后双击桌面的 `DeepSeek Harness` 快捷方式。也可以直接运行：

```powershell
& '.\desktop\publish\DeepSeek Harness.exe'
```

## 使用

- 关闭主窗口：隐藏到系统托盘，DSH 继续运行。
- 双击托盘图标或选择“打开”：立即恢复窗口，不播放启动动画。
- 选择“重启服务”：重启由本程序启动的 DSH。
- 选择“查看日志”：打开 `%LOCALAPPDATA%\DeepSeekHarness\logs`。
- 选择“退出”：移除托盘图标，并停止本程序拥有的 DSH 进程树。

## 更新 DeepSeek Harness

DSH 与桌面壳分别更新，桌面壳不会自动修改本机的 DSH 安装。更新时按以下顺序操作：

1. 从托盘选择“退出”，完全关闭 DSH Desktop 及其拥有的后端。
2. 打开 PowerShell 并运行：

```powershell
npm install --global @deepseek-ai/dsh@latest
```

3. 关闭并重新打开 PowerShell，让新的命令环境生效，然后核对版本：

```powershell
dsh --version
```

4. 重新双击桌面快捷方式。

桌面程序会在每次启动时重新发现 `dsh.cmd`，因此通常不需要重新构建程序或创建快捷方式。

DeepSeek Harness 目前处于开发者预览阶段，官方说明未来可能出现破坏兼容性的变更。如果新版调整了命令入口、Web 启动参数或页面识别方式，本项目也需要同步适配，然后重新运行测试与构建脚本。

## 开发与构建

桌面端使用 .NET Framework 4.8、WPF、WebView2 和 PowerShell 构建，不依赖 Visual Studio 工程文件。

```powershell
# 完整测试
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1

# 生成发布目录
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

构建产物位于 `desktop\publish`。第一次构建若缺少固定版本的 WebView2 SDK，构建脚本会下载对应 NuGet 包。

## 项目结构

```text
dsh-desktop/
|-- assets/branding/                  # 应用图标
|-- desktop/
|   |-- assets/                       # 应用清单与界面资源
|   |-- runtime/                      # DSH 后端启动脚本
|   |-- src/                          # 桌面端源码
|   |-- tests/                        # 自动化测试
|   |-- build.ps1                     # 构建与发布
|   |-- install-desktop-shortcut.ps1  # 创建桌面快捷方式
|   |-- test.ps1                      # 完整测试入口
|   `-- README.md                     # 桌面端开发说明
|-- tools/                            # 图标生成工具
|-- .gitignore
`-- README.md
```

`desktop\packages`、`desktop\obj`、`desktop\test-output` 与 `desktop\publish` 均为本地生成目录，不进入版本库。

## 项目状态

当前版本面向 64 位 Windows，并使用本机 DSH 的 Web 运行模式。DeepSeek Harness 上游仍处于开发预览阶段，上游接口变化可能需要本项目同步适配。
