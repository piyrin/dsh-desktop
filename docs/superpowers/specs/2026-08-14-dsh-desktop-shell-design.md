# DeepSeek Harness Windows Desktop Shell Design

Date: 2026-08-14
Status: Approved conversational design, awaiting written-spec review

## Goal

Wrap the existing DeepSeek Harness WebUI in a lightweight Windows desktop application that launches without a terminal, owns its window and taskbar identity, remains available from the system tray, and reaches an interactive UI within 5-6 seconds on a normal cold launch.

## Confirmed user experience

- A desktop shortcut launches `DeepSeek Harness.exe`; no Command Prompt or PowerShell window is shown.
- The application uses an independent native window containing WebView2, rather than an Edge `--app` window.
- Closing the native window hides it to the system tray. It does not stop DSH.
- Opening the application from the tray always restores the already-running window directly and never plays the startup animation.
- Repeatedly launching the desktop shortcut while the application is already running activates the existing window instead of creating a second window or tray icon.
- The tray menu contains: Open, Restart Service, View Logs, and Exit.
- Exit stops only the DSH process tree owned by this desktop application, then removes the tray icon.

## Technology choice

Use a .NET Framework 4.8 WPF application with Microsoft WebView2.

This fits the current machine because the .NET Framework compiler and reference assemblies are installed, and the Microsoft Edge WebView2 Runtime is already present. It avoids Electron's bundled Chromium size while providing a real executable, native window behavior, a tray icon, and a WPF animation layer.

The build will bundle the WebView2 SDK assemblies and native loader required by the application. The installed Evergreen WebView2 Runtime supplies the browser engine.

## Components

### Desktop shell

Owns the main WPF window, custom application icon and title, window visibility, navigation, and the WebView2 control. It initializes WebView2 in parallel with the backend startup.

### Backend supervisor

Starts the installed Node executable directly with the DSH CLI entry module and the existing web-profile arguments. It does not invoke the current `.cmd` or PowerShell launchers.

The supervisor redirects standard output and error to rotating local log files, polls the local HTTP endpoint for readiness, detects premature process exit, and tracks whether the process belongs to this application. An owned backend is placed in a Windows job object so that a deliberate tray Exit can terminate the complete owned process tree without affecting an independently started DSH instance.

### Single-instance coordinator

Uses a named mutex to elect one primary desktop process and a named pipe to send an activation request from later launches. A second launch exits after asking the primary process to restore and focus its window.

### Tray controller

Keeps the application alive when the main window is hidden. Open restores the window without animation. Restart Service restarts an owned backend and uses the cold-start readiness flow. View Logs opens the log directory. Exit performs an orderly application shutdown.

### Startup presentation

The application initially waits up to 3 seconds for the backend while WebView2 initializes in parallel.

- If the backend becomes ready within 3 seconds, the main WebUI opens directly and the animated splash is never shown.
- If the backend is still unavailable after 3 seconds, the shell shows a dark, borderless startup presentation inspired by the supplied reference: a blue whale mark and the words `DeepSeek Harness` appear through a left-to-right moving opacity mask with a restrained blue bloom.
- After the initial reveal, a subtle moving highlight continues without restarting the animation.
- When the backend and WebView2 are both ready, the splash crossfades into the WebUI.
- Restoring from the tray bypasses this component completely.

The animation communicates genuine waiting and never imposes a minimum display duration that would delay a ready application.

## Startup performance

The current `.cmd` and PowerShell chain measured approximately 9.17 seconds to a usable local HTTP response in the initial cold-start measurement.

The desktop supervisor will apply three optimizations:

1. Invoke Node directly, avoiding the extra command-shell and PowerShell launch chain.
2. Set a persistent `NODE_COMPILE_CACHE` directory under the application's local data directory and flush the cache after DSH completes booting.
3. Initialize WebView2 concurrently with DSH rather than starting the browser only after the server begins listening.

Controlled measurements of the direct-Node path reached the HTTP endpoint in approximately 3.06 seconds while filling the module cache and 2.04 seconds with a cache hit. These measurements support the 5-6 second acceptance target while leaving headroom for the native shell and post-reboot filesystem variability.

## Data and process flow

1. The desktop executable starts and checks the single-instance mutex.
2. If another instance owns the mutex, the new process sends an Activate message and exits.
3. The primary instance checks `http://127.0.0.1:8080/`.
4. If a compatible server is already responding, the shell attaches without claiming process ownership.
5. Otherwise, the supervisor starts DSH directly through Node, captures its logs, and assigns the process to the application job object.
6. WebView2 initialization and backend readiness polling run concurrently.
7. The WebUI is navigated only after the readiness endpoint responds successfully.
8. Closing the window hides it; the backend and desktop process continue running in the tray.
9. Tray Exit closes the window, disposes WebView2 and the tray icon, and terminates only the owned backend job.

## Readiness and failure handling

- Readiness requires a successful HTTP response from the configured local URL whose HTML contains the shipped `<title>DeepSeek Harness</title>` marker, not merely an open TCP port.
- The default backend startup timeout is 30 seconds.
- If DSH exits before readiness, the shell displays an error state with Retry, View Logs, and Exit actions.
- If the timeout expires, the same error state identifies the timeout instead of leaving the animation running indefinitely.
- If port 8080 responds without the shipped `DeepSeek Harness` title marker, the shell classifies it as a non-DSH port conflict and does not navigate WebView2 to the unknown service.
- Restart Service is disabled for an externally owned DSH process; the application will not terminate processes it did not start.
- Missing Node, DSH entry files, WebView2 Runtime, or WebView2 loader dependencies produce specific actionable errors.

## Local files and state

Runtime state is stored beneath `%LOCALAPPDATA%\DeepSeekHarness`:

- `logs\desktop.log`
- `logs\dsh-stdout.log`
- `logs\dsh-stderr.log`
- `cache\node-compile\`
- `webview2\` user-data directory

The workspace contains the source, build script, application assets, and published executable. Existing DSH profile files and vision configuration remain authoritative and are not copied into the desktop application.

## Verification strategy

### Automated checks

- Backend command construction selects the installed Node and DSH paths correctly.
- Readiness classification distinguishes ready, timeout, early exit, port conflict, and external compatible server states.
- Single-instance activation message restores a hidden window.
- Owned-process cleanup never targets an unowned PID.
- Startup-animation decision returns false for readiness at or before 3 seconds and true only after the threshold.

### Manual integration checks

- Launch from the executable and desktop shortcut with no visible terminal.
- Verify a cold launch reaches an interactive WebUI within 5-6 seconds under normal cached conditions.
- Verify a slower launch shows the left-to-right reveal and transitions cleanly when ready.
- Close the window and confirm DSH remains available from the tray.
- Restore from the tray and confirm no animation is shown.
- Launch the shortcut again and confirm the existing window activates with no duplicate tray icon.
- Exercise every tray command.
- Exit from the tray and confirm the owned DSH tree and tray icon disappear.
- Verify logs and error actions for a forced port conflict and a deliberately invalid DSH path.

## Non-goals

- Reimplementing or restyling the DSH WebUI itself.
- Bundling a second Chromium engine.
- Installing DSH, Node, or the user's model configuration.
- Adding automatic launch at Windows sign-in in this iteration.
- Changing the vision MCP or inline-vision behavior.

## Acceptance criteria

- No terminal window appears during launch, normal use, restart, or exit.
- The application has its own executable, icon, native window, taskbar entry, and tray icon.
- Normal cached cold launch is interactive within 5-6 seconds.
- Startup animation appears only when readiness exceeds 3 seconds.
- Tray restore and duplicate-shortcut activation never play the startup animation.
- Closing the window leaves DSH running in the tray.
- Tray Exit removes the tray icon and stops only the backend owned by the desktop application.
- Failure states are finite, actionable, and backed by readable logs.
