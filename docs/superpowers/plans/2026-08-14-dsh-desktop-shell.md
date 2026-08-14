# DeepSeek Harness Windows Desktop Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a terminal-free Windows desktop executable that hosts the existing DSH WebUI in WebView2, keeps DSH alive in the tray, reaches normal cached cold-start readiness within 5-6 seconds, and never animates a tray restore.

**Architecture:** A code-only .NET Framework 4.8 WPF shell owns WebView2, the tray icon, single-instance activation, and an explicitly owned DSH process tree. The shell starts Node directly through a small ESM bootstrap that enables and flushes Node's compile cache, while WebView2 initializes in parallel. Pure policy and classification logic is separated from Windows integration so it can be tested with a dependency-free C# test runner.

**Tech Stack:** .NET Framework 4.8, C# 5-compatible source, WPF, Windows Forms `NotifyIcon`, Microsoft.Web.WebView2 1.0.4078.44, Node.js 24, PowerShell build scripts.

## Global Constraints

- Target Windows x64 and .NET Framework 4.8; do not require the .NET SDK.
- Pin Microsoft.Web.WebView2 to stable version `1.0.4078.44` and use the installed Evergreen WebView2 Runtime.
- Do not invoke `start-dsh-web.cmd`, `dsh.cmd`, or `dsh.ps1` at runtime; resolve Node and the DSH `lib/bin.js` entry, then start Node directly.
- Use `http://127.0.0.1:8080/`, a 30-second backend timeout, and `<title>DeepSeek Harness</title>` as the DSH readiness marker.
- Show the animated splash only when a cold/restart flow is still not ready after 3 seconds.
- Tray restore and duplicate-shortcut activation must never show the startup animation.
- Closing the window hides it to the tray; only explicit tray Exit stops an owned backend.
- Never stop or restart a compatible DSH process that the desktop shell did not start.
- Store logs, WebView2 data, and Node compile cache beneath `%LOCALAPPDATA%\DeepSeekHarness`.
- Do not add launch-at-sign-in, alter the DSH WebUI, or modify vision MCP behavior.
- Use **DSH Desktop** as the project name and `dsh-desktop` as the recommended GitHub repository slug.
- The workspace is not currently a Git repository. Run commit steps only if `git rev-parse --is-inside-work-tree` succeeds.

---

## File map

Create the following focused units beneath `desktop/`:

- `src/AppPaths.cs`: deterministic local-data, log, cache, WebView2, and runtime paths.
- `src/StartupPolicy.cs`: open reasons and the only splash-animation decision.
- `src/CommandLocator.cs`: PATH lookup without executing shell wrappers.
- `src/DshInstallation.cs`: resolved Node executable and DSH CLI entry.
- `src/BackendLaunchSpec.cs`: direct-Node `ProcessStartInfo` inputs and environment.
- `runtime/dsh-desktop-bootstrap.mjs`: imports the DSH entry and flushes Node compile cache after boot.
- `src/DshReadiness.cs`: HTTP response classification and bounded readiness polling.
- `src/AppLogger.cs`: bounded append-only desktop and backend log handling.
- `src/JobObject.cs`: kill-on-close Windows job object for the owned backend tree.
- `src/BackendSupervisor.cs`: attach/start/restart/stop state machine and process ownership.
- `src/SingleInstanceCoordinator.cs`: mutex plus named-pipe Activate signal.
- `src/AppAssets.cs`: application colors, icon loading, and embedded whale geometry.
- `src/SplashOverlay.cs`: left-to-right reveal and continuous restrained shimmer.
- `src/MainWindow.cs`: WebView2 host plus splash/error overlays.
- `src/TrayController.cs`: Open, Restart Service, View Logs, and Exit menu.
- `src/DesktopApplication.cs`: orchestrates startup, readiness, window/tray lifecycle, and error states.
- `src/Program.cs`: STA entry point and secondary-instance handoff.
- `tests/TestRunner.cs`: dependency-free test registration, assertions, and exit code.
- `tests/StartupPolicyTests.cs`, `DshInstallationTests.cs`, `DshReadinessTests.cs`, `BackendOwnershipTests.cs`, `ActivationProtocolTests.cs`: pure unit coverage.
- `tests/NoWindowHelper.cs`: tiny console helper used only to verify hidden redirected process launch.
- `assets/favicon.svg`: byte-for-byte copy of the installed DSH frontend favicon used as vector splash geometry.
- `assets/app.manifest`: per-monitor DPI-aware Windows manifest.
- `build.ps1`: restore the pinned WebView2 package and compile/publish x64 app files.
- `test.ps1`: compile and run pure tests using the in-box .NET Framework compiler.
- `install-desktop-shortcut.ps1`: create/update the user desktop shortcut to the published executable.
- `desktop/README.md`: desktop build, launch, tray, log, and recovery instructions.
- `README.md`: GitHub-facing project overview, setup, directory tree, screenshots section, limitations, and publishing notes.

---

### Task 1: Test harness, paths, and startup animation policy

**Files:**
- Create: `desktop/src/AppPaths.cs`
- Create: `desktop/src/StartupPolicy.cs`
- Create: `desktop/tests/TestRunner.cs`
- Create: `desktop/tests/StartupPolicyTests.cs`
- Create: `desktop/test.ps1`

**Interfaces:**
- Produces: `AppPaths.Create(string localAppData, string applicationDirectory) : AppPaths`
- Produces: `StartupPolicy.ShouldShowAnimatedSplash(OpenReason reason, TimeSpan elapsed, bool backendReady) : bool`
- Produces: `OpenReason.ColdLaunch`, `OpenReason.ServiceRestart`, `OpenReason.TrayRestore`, and `OpenReason.SecondaryActivation`

- [ ] **Step 1: Write the failing startup-policy tests**

```csharp
internal static class StartupPolicyTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("cold launch stays unanimated at three seconds", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.ColdLaunch, TimeSpan.FromSeconds(3), false));
        });
        runner.Add("cold launch animates after three seconds", delegate {
            AssertEx.True(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.ColdLaunch, TimeSpan.FromMilliseconds(3001), false));
        });
        runner.Add("ready backend never animates", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.ColdLaunch, TimeSpan.FromSeconds(8), true));
        });
        runner.Add("tray restore never animates", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.TrayRestore, TimeSpan.FromSeconds(20), false));
        });
        runner.Add("secondary activation never animates", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.SecondaryActivation, TimeSpan.FromSeconds(20), false));
        });
    }
}
```

- [ ] **Step 2: Add the dependency-free runner and run the red test**

`TestRunner.cs` must expose `Add(string, Action)`, `Run(string filter) : int`, and `AssertEx.True`, `False`, and `Equal<T>`. `TestRunner.Main(string[] args)` accepts `--filter <text>` and runs every registered test whose name contains the text case-insensitively; no filter runs all tests. `test.ps1 -Filter DshReadiness` forwards `--filter DshReadiness` after compiling all unit-test files plus only the pure `src/*.cs` listed explicitly, using `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. `NoWindowHelper.cs` is excluded from the unit-test executable because it has its own `Main`.

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Expected: compilation fails because `StartupPolicy` and `OpenReason` do not exist.

- [ ] **Step 3: Implement paths and the minimal policy**

```csharp
internal enum OpenReason
{
    ColdLaunch,
    ServiceRestart,
    TrayRestore,
    SecondaryActivation
}

internal static class StartupPolicy
{
    internal static readonly TimeSpan AnimationThreshold = TimeSpan.FromSeconds(3);

    internal static bool ShouldShowAnimatedSplash(
        OpenReason reason, TimeSpan elapsed, bool backendReady)
    {
        if (backendReady) return false;
        if (reason == OpenReason.TrayRestore || reason == OpenReason.SecondaryActivation) return false;
        return elapsed > AnimationThreshold;
    }
}
```

`AppPaths.Create` must derive `Root`, `Logs`, `NodeCompileCache`, `WebView2Data`, `DesktopLog`, `DshStdoutLog`, `DshStderrLog`, and `BootstrapScript` without touching the filesystem in its constructor.

- [ ] **Step 4: Run the tests and verify green**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Expected: all Task 1 tests pass and the process exits `0`.

- [ ] **Step 5: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src/AppPaths.cs desktop/src/StartupPolicy.cs desktop/tests desktop/test.ps1
  git commit -m "test: define desktop startup policy"
}
```

---

### Task 2: Resolve DSH and construct the direct-Node launch

**Files:**
- Create: `desktop/src/CommandLocator.cs`
- Create: `desktop/src/DshInstallation.cs`
- Create: `desktop/src/BackendLaunchSpec.cs`
- Create: `desktop/runtime/dsh-desktop-bootstrap.mjs`
- Create: `desktop/tests/DshInstallationTests.cs`
- Modify: `desktop/tests/TestRunner.cs`
- Modify: `desktop/test.ps1`

**Interfaces:**
- Consumes: `AppPaths`
- Produces: `CommandLocator.Find(string commandName, string pathValue) : string`
- Produces: `DshInstallation.Discover(string pathValue) : DshInstallation`
- Produces: `BackendLaunchSpec.Create(DshInstallation installation, AppPaths paths, int port) : BackendLaunchSpec`
- `BackendLaunchSpec` exposes `FileName`, `Arguments`, `WorkingDirectory`, and `IDictionary<string,string> Environment`

- [ ] **Step 1: Write failing discovery and launch-spec tests**

Use a temporary fake PATH with `node.exe`, `dsh.cmd`, and `node_modules\@deepseek-ai\dsh\lib\bin.js`. Assert that discovery returns the fake Node and derives the JS entry from the directory containing `dsh.cmd`. Assert launch-spec filename equals Node, arguments contain only the quoted bootstrap path, and environment contains exact values for `DSH_DESKTOP_ENTRY`, `DSH_DESKTOP_PORT=8080`, and `NODE_COMPILE_CACHE`.

```csharp
runner.Add("launch spec bypasses command wrappers", delegate {
    BackendLaunchSpec spec = BackendLaunchSpec.Create(fakeInstallation, paths, 8080);
    AssertEx.Equal(fakeInstallation.NodeExe, spec.FileName);
    AssertEx.False(spec.Arguments.Contains("dsh.cmd"));
    AssertEx.Equal("8080", spec.Environment["DSH_DESKTOP_PORT"]);
});
```

- [ ] **Step 2: Run the focused red test**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1 -Filter DshInstallation`

Expected: compilation fails because discovery and launch types do not exist.

- [ ] **Step 3: Implement PATH lookup, discovery, launch spec, and bootstrap**

The bootstrap content must be exactly behaviorally equivalent to:

```javascript
import { flushCompileCache } from "node:module";
import { pathToFileURL } from "node:url";

const entry = process.env.DSH_DESKTOP_ENTRY;
const port = process.env.DSH_DESKTOP_PORT || "8080";
if (!entry) throw new Error("DSH_DESKTOP_ENTRY is not set");

process.argv = [process.execPath, entry, "--profile", "web", "--port", port];
await import(pathToFileURL(entry).href);
flushCompileCache();
```

`DshInstallation.Discover` must throw messages naming the missing item when Node, `dsh.cmd`, or the derived `lib/bin.js` is absent. It may locate wrappers but must never execute them.

- [ ] **Step 4: Run all pure tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Expected: all tests pass.

- [ ] **Step 5: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src desktop/runtime desktop/tests desktop/test.ps1
  git commit -m "feat: launch dsh directly through node"
}
```

---

### Task 3: DSH HTTP readiness classification and polling

**Files:**
- Create: `desktop/src/DshReadiness.cs`
- Create: `desktop/tests/DshReadinessTests.cs`
- Modify: `desktop/tests/TestRunner.cs`
- Modify: `desktop/test.ps1`

**Interfaces:**
- Produces: `ReadinessState.NotReady`, `ReadinessState.Ready`, and `ReadinessState.PortConflict`
- Produces: `DshReadiness.Classify(int? statusCode, string body, Exception error) : ReadinessState`
- Produces: `DshReadiness.WaitAsync(Uri uri, TimeSpan timeout, CancellationToken token) : Task<ReadinessResult>`
- `ReadinessResult` exposes `State`, `Elapsed`, and `Detail`

- [ ] **Step 1: Write the response-classification tests**

```csharp
runner.Add("connection refusal is not ready", delegate {
    AssertEx.Equal(ReadinessState.NotReady,
        DshReadiness.Classify(null, null, new HttpRequestException("refused")));
});
runner.Add("shipped title marker is ready", delegate {
    AssertEx.Equal(ReadinessState.Ready,
        DshReadiness.Classify(200, "<title>DeepSeek Harness</title>", null));
});
runner.Add("unrelated html is a port conflict", delegate {
    AssertEx.Equal(ReadinessState.PortConflict,
        DshReadiness.Classify(200, "<title>Other App</title>", null));
});
runner.Add("non-success http response is a port conflict", delegate {
    AssertEx.Equal(ReadinessState.PortConflict,
        DshReadiness.Classify(404, "missing", null));
});
```

- [ ] **Step 2: Run the red tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1 -Filter DshReadiness`

Expected: compilation fails because readiness types do not exist.

- [ ] **Step 3: Implement classification and bounded polling**

Use a shared `HttpClient` with a 700ms per-request timeout and 75ms delay between attempts. Return immediately on Ready or PortConflict. On overall timeout, return NotReady with detail `DSH did not become ready within 30 seconds.` Do not treat a bare TCP listener as ready.

- [ ] **Step 4: Run all tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Expected: all tests pass.

- [ ] **Step 5: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src/DshReadiness.cs desktop/tests desktop/test.ps1
  git commit -m "feat: verify dsh http readiness"
}
```

---

### Task 4: Owned backend supervision, logging, and job cleanup

**Files:**
- Create: `desktop/src/AppLogger.cs`
- Create: `desktop/src/JobObject.cs`
- Create: `desktop/src/BackendSupervisor.cs`
- Create: `desktop/tests/BackendOwnershipTests.cs`
- Create: `desktop/tests/NoWindowHelper.cs`
- Modify: `desktop/tests/TestRunner.cs`
- Modify: `desktop/test.ps1`

**Interfaces:**
- Consumes: `BackendLaunchSpec`, `DshReadiness`, and `AppPaths`
- Produces: `BackendOwnership.None`, `BackendOwnership.Owned`, and `BackendOwnership.External`
- Produces: `BackendSupervisor.EnsureReadyAsync(CancellationToken) : Task<ReadinessResult>`
- Produces: `BackendSupervisor.RestartAsync(CancellationToken) : Task<ReadinessResult>`
- Produces: `BackendSupervisor.StopOwnedAsync() : Task`
- Produces: `BackendSupervisor.Ownership : BackendOwnership`

- [ ] **Step 1: Write failing ownership guard tests**

```csharp
runner.Add("external backend cannot be stopped", delegate {
    AssertEx.False(BackendOwnershipPolicy.MayStop(BackendOwnership.External));
});
runner.Add("external backend cannot be restarted", delegate {
    AssertEx.False(BackendOwnershipPolicy.MayRestart(BackendOwnership.External));
});
runner.Add("owned backend can be stopped and restarted", delegate {
    AssertEx.True(BackendOwnershipPolicy.MayStop(BackendOwnership.Owned));
    AssertEx.True(BackendOwnershipPolicy.MayRestart(BackendOwnership.Owned));
});
```

- [ ] **Step 2: Run the focused red test**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1 -Filter BackendOwnership`

Expected: compilation fails because ownership types do not exist.

- [ ] **Step 3: Implement bounded logging and ownership policy**

`AppLogger` must rotate each log once when it exceeds 5 MiB: delete the prior `.1`, move the current file to `.1`, then create a new file. Prefix desktop log lines with an ISO-8601 local timestamp. Backend stdout and stderr stay in separate files.

- [ ] **Step 4: Implement job object and supervisor**

`JobObject` must set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, assign only the newly started Node process, and expose `Dispose`. `BackendSupervisor` follows this exact decision order:

1. Probe port 8080.
2. Ready means attach as External and do not start Node.
3. PortConflict means fail without starting Node.
4. NotReady means start Node with `UseShellExecute=false`, `CreateNoWindow=true`, redirected stdout/stderr, and the launch-spec environment.
5. Assign Node to the job, mark Owned, begin async log capture, and poll readiness.
6. Early process exit returns its exit code and stderr tail.
7. Timeout returns an actionable failure while leaving the owned process available for an explicit Retry/Restart decision.

`StopOwnedAsync` must re-check `BackendOwnershipPolicy.MayStop`, close the job object, wait up to 5 seconds for exit, then clear the owned process reference. It must be a no-op for External.

- [ ] **Step 5: Run all tests and a no-window process smoke check**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Compile `NoWindowHelper.cs` as `desktop\test-output\NoWindowHelper.exe`; its complete body is:

```csharp
using System;
internal static class NoWindowHelper
{
    private static int Main()
    {
        Console.WriteLine("hidden-helper-ready");
        return 0;
    }
}
```

Start it with `UseShellExecute=false`, `CreateNoWindow=true`, and redirected stdout through a small `BackendSupervisor` test seam that accepts a supplied `BackendLaunchSpec`. Verify `MainWindowHandle` is zero, exit code is zero, and captured output is exactly `hidden-helper-ready`.

Expected: unit tests pass; smoke helper exits zero with no visible console.

- [ ] **Step 6: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src desktop/tests desktop/test.ps1
  git commit -m "feat: supervise the owned dsh backend"
}
```

---

### Task 5: Single-instance activation

**Files:**
- Create: `desktop/src/SingleInstanceCoordinator.cs`
- Create: `desktop/tests/ActivationProtocolTests.cs`
- Modify: `desktop/tests/TestRunner.cs`
- Modify: `desktop/test.ps1`

**Interfaces:**
- Produces: `ActivationProtocol.SerializeActivate() : string`
- Produces: `ActivationProtocol.IsActivate(string message) : bool`
- Produces: `SingleInstanceCoordinator.TryBecomePrimary() : bool`
- Produces: `SingleInstanceCoordinator.SignalPrimary(TimeSpan timeout) : bool`
- Produces event: `SingleInstanceCoordinator.ActivateRequested`

- [ ] **Step 1: Write failing protocol tests**

```csharp
runner.Add("activation protocol accepts exact command", delegate {
    AssertEx.True(ActivationProtocol.IsActivate("ACTIVATE\n"));
});
runner.Add("activation protocol rejects arbitrary input", delegate {
    AssertEx.False(ActivationProtocol.IsActivate("RESTART\n"));
});
```

- [ ] **Step 2: Run the red test**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1 -Filter ActivationProtocol`

Expected: compilation fails because activation types do not exist.

- [ ] **Step 3: Implement mutex and named-pipe coordination**

Use mutex `Local\DeepSeekHarness.Desktop.v1` and pipe `DeepSeekHarness.Desktop.Activation.v1`. The primary listener reads one UTF-8 line at a time and raises `ActivateRequested` only for exact ACTIVATE. A secondary instance connects for at most 1500ms, writes ACTIVATE, flushes, and exits. Cancellation and disposal must unblock the listener.

- [ ] **Step 4: Run tests and a two-process smoke check**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Then start a primary coordinator test host, invoke a secondary host, and assert the primary records one activation while the secondary exits within 1500ms.

- [ ] **Step 5: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src/SingleInstanceCoordinator.cs desktop/tests desktop/test.ps1
  git commit -m "feat: activate a single desktop instance"
}
```

---

### Task 6: Native WebView2 window and conditional startup presentation

**Files:**
- Create: `desktop/src/AppAssets.cs`
- Create: `desktop/src/SplashOverlay.cs`
- Create: `desktop/src/MainWindow.cs`
- Create: `desktop/src/Program.cs` with a UI-preview entry point
- Create: `desktop/assets/app.manifest`
- Create: `desktop/assets/favicon.svg`
- Create: `desktop/build.ps1`
- Reuse: `dsh-whale.ico`

**Interfaces:**
- Consumes: `AppPaths`, `StartupPolicy`, and `OpenReason`
- Produces: `MainWindow.InitializeWebViewAsync() : Task`
- Produces: `MainWindow.NavigateToDsh(Uri uri) : Task`
- Produces: `MainWindow.ShowAnimatedSplash() : void`
- Produces: `MainWindow.ShowWebContent() : void`
- Produces: `MainWindow.ShowError(string title, string detail, Action retry, Action viewLogs, Action exit) : void`
- Produces: `MainWindow.RestoreWithoutAnimation() : void`

- [ ] **Step 1: Add the build script with pinned dependency restore**

`build.ps1` must download only this stable package when absent:

```powershell
$webViewVersion = '1.0.4078.44'
$packageUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$webViewVersion/microsoft.web.webview2.$webViewVersion.nupkg"
```

Extract beneath `desktop\packages\Microsoft.Web.WebView2.1.0.4078.44`, compile `/target:winexe /platform:x64` with the .NET Framework 4.8 references plus the WebView2 `net462` Core and WPF assemblies, and publish to `desktop\publish`. Copy `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.Wpf.dll`, x64 `WebView2Loader.dll`, `dsh-desktop-bootstrap.mjs`, and the package license alongside the executable.

- [ ] **Step 2: Build before UI sources exist and verify red**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1`

Expected: compile fails because `Program.cs`, `MainWindow`, and UI source files do not exist; dependency restore succeeds.

- [ ] **Step 3: Implement app assets and splash overlay**

Use background `#0B0F14`, primary blue `#4D6BFE`, and foreground `#E8EEF9`. Copy `F:\claudecode\node_modules\@deepseek-ai\dsh\node_modules\@deepseek-ai\dsh-web-frontend\dist\favicon.svg` byte-for-byte to `desktop\assets\favicon.svg`. `build.ps1` copies it beside the executable. `AppAssets` loads that exact file with `XDocument`, reads the first SVG `path` element's `d` attribute, and passes it to `Geometry.Parse`; a WPF `Path` with `Stretch=Uniform` and fill `#4D6BFE` renders the whale. Do not use the supplied screenshot as a raster background. Center the whale and `DeepSeek Harness` wordmark.

`ShowAnimatedSplash` starts a 1.2-second left-to-right opacity-mask reveal, followed by a low-contrast 1.8-second repeating shimmer. Calling `ShowWebContent`, `ShowError`, or `RestoreWithoutAnimation` must stop the storyboard and hide the overlay immediately. The animation must never enforce a minimum duration.

- [ ] **Step 4: Implement the WebView2 window**

Create a 1280x800 WPF window with minimum 900x600, title `DeepSeek Harness`, the existing whale icon, and WebView2 filling the content area. Initialize with `CoreWebView2Environment.CreateAsync(null, paths.WebView2Data)`. Keep the window unshown during the first 3 seconds; a fast ready flow shows WebUI directly. A slow flow shows the splash. Navigation to unknown origins is canceled, while explicit new-window requests open through the default browser.

For this task's independently runnable UI preview, `Program.Main` reads `DSH_DESKTOP_UI_TEST_STATE`. Value `splash` shows the animated splash, `error` shows the error overlay with inert preview callbacks, and `web` shows the empty WebView host. Any other value exits with an explanatory message written to `desktop.log`. Task 7 replaces this preview branch with full application orchestration while retaining the three explicit UI-test values.

- [ ] **Step 5: Build and manually inspect the window states**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1`

Expected: `desktop\publish\DeepSeek Harness.exe` builds with no console subsystem. Launch it three times with `DSH_DESKTOP_UI_TEST_STATE=splash`, `error`, and `web`; capture screenshots and confirm no clipping at 100%, 125%, and 150% display scaling.

- [ ] **Step 6: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src desktop/assets desktop/build.ps1
  git commit -m "feat: add native webview shell and splash"
}
```

---

### Task 7: Tray lifecycle and desktop orchestration

**Files:**
- Create: `desktop/src/TrayController.cs`
- Create: `desktop/src/DesktopApplication.cs`
- Modify: `desktop/src/Program.cs`
- Modify: `desktop/build.ps1`

**Interfaces:**
- Consumes: all prior component interfaces
- `DesktopApplication.RunColdStartAsync() : Task` owns the cold flow
- `DesktopApplication.Restore(OpenReason reason) : void` must bypass animation for TrayRestore and SecondaryActivation
- `DesktopApplication.RestartServiceAsync() : Task` uses ServiceRestart and may animate only after 3 seconds
- `DesktopApplication.ExitAsync() : Task` stops only an owned backend

- [ ] **Step 1: Add orchestration tests to the pure policy suite**

Extend startup tests to cover the exact call-site reasons used by `DesktopApplication`:

```csharp
runner.Add("all restore reasons bypass splash", delegate {
    AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
        OpenReason.TrayRestore, TimeSpan.FromMinutes(1), false));
    AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
        OpenReason.SecondaryActivation, TimeSpan.FromMinutes(1), false));
});
```

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Expected: tests pass before integration, proving the shared policy already rejects restore animation.

- [ ] **Step 2: Implement tray commands**

Create one `NotifyIcon` using the executable icon and a `ContextMenuStrip` with exactly Open, Restart Service, View Logs, separator, and Exit. Double-click maps to Open. Restart is disabled when backend ownership is External. Dispose the icon before application shutdown so no ghost icon remains.

- [ ] **Step 3: Implement the application controller**

The cold-start flow must start WebView2 and `EnsureReadyAsync` concurrently, run a 3-second delay, and show the splash only if the readiness task is still incomplete. On Ready, navigate and reveal WebUI. On conflict, early exit, timeout, missing dependency, or WebView2 failure, show the actionable error overlay.

Intercept `MainWindow.Closing`: unless `ExitAsync` set the explicit-exit flag, cancel closing and call `Hide`. Tray Open and named-pipe activation call `Restore` directly and never evaluate a cold-start animation timer.

- [ ] **Step 4: Implement the STA entry point**

`Program.Main` must:

1. Create `SingleInstanceCoordinator`.
2. If secondary, signal primary and exit zero without initializing WPF or WebView2.
3. If primary, create `Application`, `AppPaths`, logger, installation, supervisor, window, tray, and controller.
4. Marshal pipe activation to the WPF dispatcher as `OpenReason.SecondaryActivation`.
5. Run WPF until explicit Exit.

- [ ] **Step 5: Build and run lifecycle smoke tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1`

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1`

Expected: tests and build pass; launching the executable creates one native window and one tray icon with no console.

- [ ] **Step 6: Create a conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop/src desktop/tests desktop/build.ps1
  git commit -m "feat: wire desktop and tray lifecycle"
}
```

---

### Task 8: Shortcut, documentation, and end-to-end verification

**Files:**
- Create: `desktop/install-desktop-shortcut.ps1`
- Create: `desktop/README.md`
- Modify: `desktop/build.ps1`

**Interfaces:**
- Consumes: `desktop\publish\DeepSeek Harness.exe`
- Produces: user desktop shortcut `DeepSeek Harness.lnk`

- [ ] **Step 1: Implement shortcut installation**

Resolve the real Desktop directory through `[Environment]::GetFolderPath('DesktopDirectory')`, falling back to the `User Shell Folders` registry value only when necessary. Create or update `DeepSeek Harness.lnk` through `WScript.Shell` with:

```text
TargetPath       = E:\dsh\desktop\publish\DeepSeek Harness.exe
WorkingDirectory = E:\dsh\desktop\publish
IconLocation     = E:\dsh\desktop\publish\DeepSeek Harness.exe,0
WindowStyle      = 1
```

Do not delete unrelated shortcuts. If an older DeepSeek Harness shortcut exists at the same resolved path, update it in place.

- [ ] **Step 2: Document operation and recovery**

`desktop/README.md` must include exact build, test, shortcut install, launch, tray restore, restart, log locations, port-conflict recovery, WebView2-missing recovery, and explicit Exit instructions. State that closing the window leaves DSH running.

- [ ] **Step 3: Run the complete automated verification**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

Expected: all tests pass and the published folder contains the executable, three WebView2 binaries, the bootstrap, and license files.

- [ ] **Step 4: Run the cold-start and lifecycle acceptance checks**

From a stopped state, time desktop click to interactive WebUI and record the result in `desktop\publish\verification.txt`. Verify normal cached cold start is at most 6 seconds. Force a greater-than-3-second delay and verify the reveal appears; run normally below the threshold and verify it does not. Close to tray, restore from tray, and confirm no animation. Launch the shortcut twice and confirm one window/tray icon. Use tray Exit and confirm port 8080 and the owned Node process disappear.

- [ ] **Step 5: Exercise failure states**

Bind a disposable local test server to port 8080 that returns `<title>Other App</title>` and verify the shell reports a port conflict without navigating. Temporarily point a test build at a missing DSH entry and verify Retry, View Logs, and Exit. Restore the real path after the test.

- [ ] **Step 6: Install the desktop shortcut after verification**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\install-desktop-shortcut.ps1`

Expected: the desktop shortcut targets the published executable and launches with no terminal.

- [ ] **Step 7: Create a conditional final checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add desktop
  git commit -m "feat: verify deepseek harness desktop shell"
}
```

---

### Task 9: GitHub-ready folder organization and root README

**Files:**
- Create: `README.md`
- Create directory: `assets/branding/`
- Create directory: `samples/vision/`
- Create directory: `tools/`
- Move: `dsh-whale.ico` to `assets/branding/dsh-whale.ico`
- Move: `dsh-whale.png` to `assets/branding/dsh-whale.png`
- Move: `make-icon.ps1` to `tools/make-icon.ps1`
- Move: `README-vision-setup.md` to `docs/vision-setup.md`
- Move: `upload-large.png`, `upload-large-small.jpg`, and `upload-small.png` to `samples/vision/`
- Create: `samples/vision/.gitkeep`
- Modify: `desktop/build.ps1`
- Modify: `desktop/install-desktop-shortcut.ps1`
- Modify: `desktop/README.md`
- Modify: `tools/make-icon.ps1`
- Preserve in place: `dsh-vision-mcp.cjs`, `start-dsh-web.cmd`, and `open-dsh-web.cmd`

**Interfaces:**
- Produces: repository identity **DSH Desktop** with recommended GitHub slug `dsh-desktop`
- Preserves: `E:\dsh\dsh-vision-mcp.cjs` because the active DSH profile references that exact path
- Preserves: root legacy launchers until users have migrated existing shortcuts

- [ ] **Step 1: Move only the explicitly listed files**

Resolve every source and destination to an absolute path beneath `E:\dsh` before moving. Create the three destination directories first. Do not move or delete any file not listed in this task. After moving, verify every listed destination exists and each listed source no longer exists. Keep the local sample images ignored by Git and track only `samples/vision/.gitkeep` so opaque test screenshots are not accidentally published.

- [ ] **Step 2: Update path consumers**

Change the desktop build and shortcut scripts to use `E:\dsh\assets\branding\dsh-whale.ico`. Update `tools\make-icon.ps1` so future output goes to `assets\branding`. Update documentation links to `docs\vision-setup.md`, `desktop\README.md`, and the organized sample paths. Keep the vision MCP configuration path unchanged.

- [ ] **Step 3: Write the root GitHub README**

Start with:

```markdown
# DSH Desktop

A lightweight native Windows desktop shell for DeepSeek Harness, with WebView2, silent backend startup, system-tray residency, and conditional cold-start animation.
```

Include: feature list, a 5-6 second normal cached cold-start claim qualified as the local acceptance target, prerequisites, quick start, build/test commands, desktop shortcut installation, tray behavior, exact log locations, project directory tree, vision integration link, screenshots placeholder section containing only instructions for the repository owner to add final screenshots, known limitations, and a note that DeepSeek Harness remains a separate upstream dependency. Do not claim affiliation with DeepSeek beyond describing compatibility.

- [ ] **Step 4: Verify the organized repository**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
rg -n "E:\\dsh\\dsh-whale|README-vision-setup|README-desktop|upload-large.png|upload-small.png" . -g '!desktop/packages/**' -g '!desktop/publish/**' -g '!samples/vision/**'
```

Expected: tests and build pass; the reference scan returns no stale paths outside historical design/plan documents, and the published executable still contains the application icon.

- [ ] **Step 5: Create the final conditional checkpoint**

```powershell
if (git rev-parse --is-inside-work-tree 2>$null) {
  git add README.md assets samples/vision/.gitkeep tools docs desktop dsh-vision-mcp.cjs start-dsh-web.cmd open-dsh-web.cmd
  git commit -m "docs: prepare dsh desktop for github"
}
```

---

## Plan self-review result

- Spec coverage: every confirmed behavior, performance target, tray rule, ownership rule, error state, non-goal, and GitHub-readiness request maps to a numbered task.
- Placeholder scan: no deferred implementation markers or unspecified test requests remain.
- Type consistency: `OpenReason`, `AppPaths`, `BackendLaunchSpec`, `ReadinessResult`, `BackendOwnership`, and coordinator/controller method names are consistent across producer and consumer tasks.
- Scope: one Windows desktop-shell deliverable; no independent subsystem needs a separate plan.
