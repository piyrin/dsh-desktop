# DSH Desktop 桌面端开发说明

本目录实现 DeepSeek Harness WebUI 的 Windows 桌面宿主，包括 WPF 主窗口、WebView2、无终端后端启动、单实例激活、系统托盘和桌面快捷方式。

项目定位与使用入口见 [根目录说明](../README.md)。以下命令均从仓库根目录运行。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
```

测试覆盖启动策略、窗口呈现、WebView2 预加载、单实例激活、后端所有权、托盘生命周期、快捷方式元数据和仓库结构。

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

构建脚本使用 .NET Framework 4.8 编译 64 位 Windows 图形程序，并把运行所需文件发布到 `desktop\publish`。主程序为：

```text
desktop\publish\DeepSeek Harness.exe
```

## 安装桌面快捷方式

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1
```

脚本会在 Windows 当前桌面目录创建或更新 `DeepSeek Harness.lnk`，并写入正确的程序路径、工作目录、图标和窗口样式。它只处理同名快捷方式，不会删除其他桌面项目。

## 启动与呈现

1. 主进程创建托盘图标，并并行初始化 WebView2 与本机 DSH 服务。
2. WebView2 预加载期间，主窗口保持透明、无边框、不激活且不进入任务栏。
3. 3 秒内完成时直接显示 DSH WebUI，不播放启动动画。
4. 超过 3 秒仍未完成时显示无边框的鲸鱼启动动画；准备完成后立即切换到 WebUI。
5. 重复启动或从托盘恢复时直接显示已有界面，不再次播放动画。

当前桌面宿主使用 `http://127.0.0.1:8080/` 作为本地 DSH 地址。若该地址已有可识别的 DSH，程序会接入现有服务；否则由桌面程序启动并管理 DSH。

## 窗口与进程生命周期

- 关闭主窗口只会隐藏窗口，托盘与后端继续运行。
- “重启服务”仅对本程序拥有的 DSH 可用。
- “退出”先移除托盘图标，再停止本程序拥有的后端，最后关闭桌面进程。
- 接入外部 DSH 时，本程序不会停止外部进程。

## 上游版本更新

桌面宿主在每次启动和重试时都会重新发现 `PATH` 中的 `dsh.cmd`。更新全局安装的 DSH 时：

1. 从托盘完全退出桌面程序。
2. 在 PowerShell 中运行：

```powershell
npm install --global @deepseek-ai/dsh@latest
```

3. 关闭并重新打开 PowerShell，再核对版本：

```powershell
dsh --version
```

4. 重新启动桌面快捷方式。

只更新 DSH 通常不需要重建桌面宿主。若上游破坏性变更影响命令入口、启动参数或 WebUI 识别规则，则需要更新本目录的适配代码并重新执行测试和构建。

## 目录说明

```text
desktop/
|-- assets/       # 应用清单与界面资源
|-- runtime/      # Node.js 后端启动脚本
|-- src/          # WPF、WebView2、托盘与后端管理源码
|-- tests/        # C# 与 PowerShell 自动化测试
|-- build.ps1
|-- install-desktop-shortcut.ps1
|-- test.ps1
`-- README.md
```

运行时日志和缓存位于 `%LOCALAPPDATA%\DeepSeekHarness`；构建生成的 `packages`、`obj`、`test-output` 与 `publish` 目录均由脚本管理。
