# DSH Desktop — DeepSeek Harness shell

This Windows desktop shell starts the installed DeepSeek Harness (DSH) service without a terminal, hosts its WebUI in a native WebView2 window, and keeps it available from the notification area.

See the [project overview](../README.md) for repository-wide setup and the optional [vision integration guide](../docs/vision-setup.md) for image-description support.

## Requirements

- 64-bit Windows with .NET Framework 4.8 build tools.
- `node.exe` and `dsh.cmd` on `PATH`, with the DSH package installed beside `dsh.cmd`.
- Microsoft Edge WebView2 Runtime (Evergreen).
- Network access during the first build so the pinned WebView2 SDK package can be restored.

Run all commands below from `E:\dsh` in Windows PowerShell.

## Test and build

Run the complete automated test suite:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
```

Build the x64 Windows GUI executable and publish its runtime files:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

The executable is `E:\dsh\desktop\publish\DeepSeek Harness.exe`.

## Install and launch the desktop shortcut

Build first, then create or update `DeepSeek Harness.lnk` in the Windows Desktop directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1
```

The installer resolves the real Desktop directory through Windows, uses `E:\dsh\assets\branding\dsh-whale.ico`, updates only `DeepSeek Harness.lnk`, and leaves unrelated shortcuts unchanged. Double-click the resulting shortcut to launch without a terminal. For a direct launch:

```powershell
& '.\desktop\publish\DeepSeek Harness.exe'
```

Launching the shortcut again activates the existing desktop process instead of starting a second window or tray icon.

## Window and tray operation

Closing the window leaves DSH running. The window is hidden and the application remains in the Windows notification area.

- Double-click the DeepSeek Harness tray icon, or choose **Open**, to restore the existing window without the startup animation.
- Choose **Restart Service** to restart a DSH backend owned by this desktop shell. The command is disabled when the shell attached to an independently started DSH service.
- Choose **View Logs** to open the log directory.
- Choose **Exit** to close the desktop shell, remove its tray icon, and stop only the Node/DSH process tree that it owns.

Use **Exit** before rebuilding, troubleshooting port 8080, or ending DSH completely. Closing the window is not an exit.

## Logs

Runtime state is beneath `%LOCALAPPDATA%\DeepSeekHarness`. The log files are:

- `%LOCALAPPDATA%\DeepSeekHarness\logs\desktop.log`
- `%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stdout.log`
- `%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stderr.log`

The WebView2 user data is in `%LOCALAPPDATA%\DeepSeekHarness\webview2`, and the Node compile cache is in `%LOCALAPPDATA%\DeepSeekHarness\cache\node-compile`.

## Recovery

If the application reports that port 8080 is already in use, it has detected a responder that is not the DSH WebUI and will not navigate to it. Exit or reconfigure the other application, confirm port 8080 is free, and select **Retry**. Use **View Logs** if the conflict is not obvious.

If WebView2 cannot start, install or repair the Microsoft Edge WebView2 Runtime (Evergreen), then select **Retry**. Rebuild if `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.Wpf.dll`, or `WebView2Loader.dll` is missing from `desktop\publish`.

If Node or the DSH entry is missing, ensure both `node.exe` and `dsh.cmd` are on `PATH` and that `node_modules\@deepseek-ai\dsh\lib\bin.js` exists beside the DSH command wrapper. Restart the desktop shell after correcting `PATH`; use **View Logs** for the discovered path or exact error.

When recovery is not appropriate, use the error screen's **Exit** action. It performs the same owned-process cleanup as tray **Exit**.
