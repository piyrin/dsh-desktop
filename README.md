# DSH Desktop

A lightweight native Windows desktop shell for DeepSeek Harness, with WebView2, silent backend startup, system-tray residency, and conditional cold-start animation.

DSH Desktop is compatible with a separately installed DeepSeek Harness WebUI on 64-bit Windows. The recommended GitHub repository slug is `dsh-desktop`.

## Features

- Native .NET Framework 4.8 WPF window with an embedded Microsoft WebView2 view.
- Direct Node.js backend launch with no Command Prompt or PowerShell window.
- Single-instance activation: a second launch restores the existing window.
- Close-to-tray behavior with Open, Restart Service, View Logs, and Exit commands.
- Ownership-aware cleanup that does not stop an independently started DSH service.
- Conditional startup presentation: no animation when readiness completes within 3 seconds; a restrained waiting animation after that threshold.
- Actionable errors for missing dependencies, startup timeout, early exit, and non-DSH services on port 8080.

## Startup target and measured result

Normal cached cold start has a **5–6 second local acceptance target**, not a cross-machine performance guarantee. In the verified stopped-state run on 2026-08-14, the exact-title HTTP endpoint responded in **4793.3 ms** and the titled native window appeared in **4876.1 ms**. Those measurements verify endpoint and window readiness only; exact WebUI DOM interactivity was not separately instrumented.

## Prerequisites

- 64-bit Windows with .NET Framework 4.8 build tools.
- `node.exe` and `dsh.cmd` on `PATH`, with the DSH package installed beside `dsh.cmd`.
- Microsoft Edge WebView2 Runtime (Evergreen).
- Network access during the first build if the pinned WebView2 SDK package is not already cached.

DeepSeek Harness, Node.js, model configuration, and the WebView2 Runtime are not installed by this repository.

## Quick start

Open Windows PowerShell in the repository root, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
& '.\desktop\publish\DeepSeek Harness.exe'
```

The build publishes the executable and required runtime files under `desktop\publish\`.

## Test and build

Run the full automated suite, including the desktop-shortcut COM test and repository-layout checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
```

Build and publish the x64 Windows GUI application:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

Build output: `desktop\publish\DeepSeek Harness.exe`.

## Install the desktop shortcut

Build first, then create or update only `DeepSeek Harness.lnk` in the Windows Desktop directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1
```

The shortcut targets the published executable, uses `assets\branding\dsh-whale.ico`, and launches without a terminal. The installer does not remove unrelated shortcuts.

## Window and tray behavior

Closing the main window hides it; the desktop process and DSH remain available in the notification area. Double-click the tray icon or choose **Open** to restore the existing window without replaying the startup animation. **Restart Service** is enabled only for a backend started by DSH Desktop. **View Logs** opens the log directory. **Exit** is designed to remove the tray icon and stop only the owned DSH process tree.

Use **Exit**, not the window close button, before rebuilding or when you intend to stop the owned backend.

## Logs and local state

Runtime state is stored beneath `%LOCALAPPDATA%\DeepSeekHarness`:

- `%LOCALAPPDATA%\DeepSeekHarness\logs\desktop.log`
- `%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stdout.log`
- `%LOCALAPPDATA%\DeepSeekHarness\logs\dsh-stderr.log`
- `%LOCALAPPDATA%\DeepSeekHarness\cache\node-compile\`
- `%LOCALAPPDATA%\DeepSeekHarness\webview2\`

## Repository layout

```text
dsh-desktop/
|-- assets/branding/        # Source PNG and ICO branding
|-- desktop/                # WPF source, tests, build, installer, and runtime
|-- docs/
|   |-- vision-setup.md     # Optional vision MCP integration
|   `-- superpowers/        # Approved design and implementation plan
|-- samples/vision/         # Ignored local test images; only .gitkeep is tracked
|-- tools/make-icon.ps1     # Regenerates branding from the bundled SVG
|-- dsh-vision-mcp.cjs      # Vision MCP path retained for existing profiles
|-- start-dsh-web.cmd       # Legacy shortcut compatibility launcher
`-- open-dsh-web.cmd        # Legacy shortcut compatibility launcher
```

Generated `desktop\packages\`, `desktop\publish\`, `desktop\obj\`, and `desktop\test-output\` content is ignored by Git.

## Vision integration

Optional image-description integration is documented in the [vision setup guide](docs/vision-setup.md). The root `dsh-vision-mcp.cjs` path remains unchanged because existing DSH profiles may reference that exact location. Local vision samples belong in [`samples/vision/`](samples/vision/) and are not published by default.

## Screenshots

Repository owner: add only reviewed, public release screenshots to a dedicated documentation folder, then replace this instruction with captions and links. Do not publish private local captures, account details, absolute user paths, API keys, or the Task 6 review screenshots.

## Known limitations and manual checks

- Windows x64 only; DSH Desktop does not install or configure its upstream dependencies.
- Port `8080` is currently fixed, and an unrelated responder is treated as a conflict.
- Exact WebUI DOM-interactive timing remains a manual measurement item.
- A real notification-area menu selection, including an actual tray **Exit** click, remains a manual acceptance item.
- End-to-end inspection at 100% and 125% display scaling remains manual.
- The real user Desktop shortcut is not installed automatically by tests or builds.
- Root web launchers are retained for compatibility with existing shortcuts and may be removed only after users migrate.

## Upstream and affiliation

DeepSeek Harness remains a separate upstream dependency and is not bundled here. DSH Desktop is an independent compatibility shell and is not affiliated with, endorsed by, or an official project of DeepSeek.
