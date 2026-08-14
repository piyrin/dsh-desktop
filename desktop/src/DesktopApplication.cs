using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

internal enum ProgramLaunchMode
{
    Normal,
    Preview,
    InvalidPreview
}

internal static class ProgramLaunchModePolicy
{
    internal static ProgramLaunchMode Resolve(string requestedState, out UiPreviewState previewState)
    {
        if (String.IsNullOrEmpty(requestedState))
        {
            previewState = default(UiPreviewState);
            return ProgramLaunchMode.Normal;
        }
        if (UiPreviewStateParser.TryParse(requestedState, out previewState))
            return ProgramLaunchMode.Preview;
        return ProgramLaunchMode.InvalidPreview;
    }
}

internal interface IDesktopWindow
{
    event CancelEventHandler Closing;
    Task InitializeWebViewAsync();
    Task NavigateToDsh(Uri uri);
    void ShowAnimatedSplash();
    void ShowStaticWaiting();
    void PresentWebContent(bool revealWindow);
    void PresentError(
        string title,
        string detail,
        Action retry,
        Action viewLogs,
        Action exit,
        bool revealWindow);
    void RestoreWithoutAnimation();
    void Hide();
    void Close();
}

internal interface IDesktopBackend
{
    BackendOwnership Ownership { get; }
    Task<ReadinessResult> EnsureReadyAsync(CancellationToken token);
    Task<ReadinessResult> RestartAsync(CancellationToken token);
    Task StopOwnedAsync();
}

internal interface IDesktopDispatcher
{
    bool CheckAccess();
    void BeginInvoke(Action action);
}

internal interface IStartupTimer
{
    TimeSpan Elapsed { get; }
    Task WaitForThresholdAsync(TimeSpan threshold);
}

internal sealed class StopwatchStartupTimer : IStartupTimer
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed { get { return stopwatch.Elapsed; } }

    public Task WaitForThresholdAsync(TimeSpan threshold)
    {
        return Task.Delay(threshold);
    }
}

internal sealed class RetryableAsyncOperation
{
    private readonly object sync = new object();
    private Task current;

    internal Task Run(Func<Task> operation)
    {
        if (operation == null) throw new ArgumentNullException("operation");
        lock (sync)
        {
            if (current == null || current.IsFaulted || current.IsCanceled)
            {
                try { current = operation(); }
                catch (Exception exception)
                {
                    TaskCompletionSource<bool> failed = new TaskCompletionSource<bool>();
                    failed.SetException(exception);
                    current = failed.Task;
                }
                if (current == null)
                    throw new InvalidOperationException("The asynchronous operation returned no task.");
            }
            return current;
        }
    }
}

internal sealed class DesktopApplication
{
    private sealed class StartupPresentationOperation
    {
        private int animationSuppressed;

        internal bool IsAnimationSuppressed
        {
            get { return Interlocked.CompareExchange(ref animationSuppressed, 0, 0) != 0; }
        }

        internal void SuppressAnimation()
        {
            Interlocked.Exchange(ref animationSuppressed, 1);
        }
    }

    private readonly IDesktopWindow window;
    private readonly IDesktopBackend backend;
    private readonly IDesktopTray tray;
    private readonly IDesktopDispatcher dispatcher;
    private readonly Func<IStartupTimer> timerFactory;
    private readonly Action openLogs;
    private readonly Action shutdown;
    private readonly Action<string> log;
    private readonly Uri dshUri;
    private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
    private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);
    private readonly object exitSync = new object();
    private readonly object presentationSync = new object();
    private volatile bool explicitExitRequested;
    private bool hiddenByClose;
    private bool hasFinalPresentation;
    private StartupPresentationOperation activePresentationOperation;
    private Task exitTask;

    internal DesktopApplication(
        IDesktopWindow window,
        IDesktopBackend backend,
        IDesktopTray tray,
        IDesktopDispatcher dispatcher,
        Func<IStartupTimer> timerFactory,
        Action openLogs,
        Action shutdown,
        Action<string> log,
        Uri dshUri)
    {
        if (window == null) throw new ArgumentNullException("window");
        if (backend == null) throw new ArgumentNullException("backend");
        if (tray == null) throw new ArgumentNullException("tray");
        if (dispatcher == null) throw new ArgumentNullException("dispatcher");
        if (timerFactory == null) throw new ArgumentNullException("timerFactory");
        if (openLogs == null) throw new ArgumentNullException("openLogs");
        if (shutdown == null) throw new ArgumentNullException("shutdown");
        if (log == null) throw new ArgumentNullException("log");
        if (dshUri == null) throw new ArgumentNullException("dshUri");
        this.window = window;
        this.backend = backend;
        this.tray = tray;
        this.dispatcher = dispatcher;
        this.timerFactory = timerFactory;
        this.openLogs = openLogs;
        this.shutdown = shutdown;
        this.log = log;
        this.dshUri = dshUri;

        window.Closing += OnWindowClosing;
        tray.OpenRequested += OnTrayOpen;
        tray.RestartServiceRequested += OnTrayRestart;
        tray.ViewLogsRequested += OnTrayViewLogs;
        tray.ExitRequested += OnTrayExit;
        RefreshTrayAvailability();
    }

    internal Task RunColdStartAsync()
    {
        return RunOnUiAsync(delegate {
            return RunStartupOperationAsync(
                OpenReason.ColdLaunch,
                delegate(CancellationToken token) { return backend.EnsureReadyAsync(token); });
        });
    }

    internal Task RestartServiceAsync()
    {
        return RunOnUiAsync(delegate {
            if (!BackendOwnershipPolicy.MayRestart(backend.Ownership))
            {
                RefreshTrayAvailability();
                return Task.FromResult(true);
            }
            return RunStartupOperationAsync(
                OpenReason.ServiceRestart,
                delegate(CancellationToken token) { return backend.RestartAsync(token); });
        });
    }

    internal void Restore(OpenReason reason)
    {
        if (reason != OpenReason.TrayRestore && reason != OpenReason.SecondaryActivation)
            throw new ArgumentOutOfRangeException("reason", "Restore accepts only tray or secondary activation reasons.");
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(delegate { Restore(reason); });
            return;
        }
        if (explicitExitRequested) return;
        hiddenByClose = false;
        bool showFinalPresentation;
        lock (presentationSync)
        {
            showFinalPresentation = hasFinalPresentation;
            if (!showFinalPresentation && activePresentationOperation != null)
                activePresentationOperation.SuppressAnimation();
        }
        if (showFinalPresentation)
            window.RestoreWithoutAnimation();
        else
            window.ShowStaticWaiting();
    }

    internal void RefreshTrayAvailability()
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RefreshTrayAvailability);
            return;
        }
        tray.SetRestartEnabled(BackendOwnershipPolicy.MayRestart(backend.Ownership));
    }

    internal Task ExitAsync()
    {
        lock (exitSync)
        {
            if (exitTask != null) return exitTask;
            explicitExitRequested = true;
            exitTask = RunOnUiAsync(ExitCoreAsync);
            return exitTask;
        }
    }

    private async Task RunStartupOperationAsync(
        OpenReason reason,
        Func<CancellationToken, Task<ReadinessResult>> ensureReady)
    {
        await operationGate.WaitAsync();
        StartupPresentationOperation presentationOperation = null;
        try
        {
            if (explicitExitRequested) return;
            presentationOperation = BeginPresentationOperation();
            CancellationToken token = lifetimeCancellation.Token;
            IStartupTimer timer = InvokeTimer();
            Task initialization = InvokeTask(window.InitializeWebViewAsync);
            Task<ReadinessResult> readiness = InvokeReadinessTask(ensureReady, token);
            Task threshold = InvokeTask(delegate {
                return timer.WaitForThresholdAsync(StartupPolicy.AnimationThreshold);
            });
            Observe(threshold, "Startup animation threshold");

            Task first = await Task.WhenAny(readiness, threshold);
            if (Object.ReferenceEquals(first, threshold)
                && threshold.Status == TaskStatus.RanToCompletion
                && !readiness.IsCompleted
                && !explicitExitRequested
                && !hiddenByClose
                && StartupPolicy.ShouldShowAnimatedSplash(
                    reason,
                    timer.Elapsed,
                    false)
                && !presentationOperation.IsAnimationSuppressed)
            {
                window.ShowAnimatedSplash();
            }

            ReadinessResult result;
            try { result = await readiness; }
            catch (OperationCanceledException)
            {
                Observe(initialization, "WebView2 initialization during cancellation");
                return;
            }
            catch (Exception exception)
            {
                Observe(initialization, "WebView2 initialization after backend failure");
                ShowServiceError("DeepSeek Harness could not start", exception.Message);
                return;
            }

            RefreshTrayAvailability();
            if (result == null || result.State != ReadinessState.Ready)
            {
                Observe(initialization, "WebView2 initialization after readiness failure");
                ShowReadinessError(result);
                return;
            }

            try
            {
                await initialization;
                await window.NavigateToDsh(dshUri);
            }
            catch (OperationCanceledException)
            {
                if (!explicitExitRequested) ShowWebViewError("WebView2 initialization was cancelled.");
                return;
            }
            catch (Exception exception)
            {
                ShowWebViewError(exception.Message);
                return;
            }

            if (explicitExitRequested) return;
            lock (presentationSync) { hasFinalPresentation = true; }
            window.PresentWebContent(!hiddenByClose);
        }
        finally
        {
            if (presentationOperation != null)
                EndPresentationOperation(presentationOperation);
            operationGate.Release();
        }
    }

    private async Task ExitCoreAsync()
    {
        lifetimeCancellation.Cancel();
        try { tray.Dispose(); }
        catch (Exception exception)
        {
            log("Unable to dispose the tray icon during exit: " + exception.Message);
        }
        try
        {
            await backend.StopOwnedAsync();
        }
        catch (Exception exception)
        {
            log("Unable to stop the owned DSH backend during exit: " + exception.Message);
        }

        try { window.Close(); }
        catch (Exception exception) { log("Unable to close the desktop window: " + exception.Message); }
        try { shutdown(); }
        catch (Exception exception) { log("Unable to shut down the desktop application: " + exception.Message); }
    }

    private IStartupTimer InvokeTimer()
    {
        try
        {
            IStartupTimer timer = timerFactory();
            if (timer == null) throw new InvalidOperationException("The startup timer factory returned no timer.");
            return timer;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Unable to create the startup timer.", exception);
        }
    }

    private StartupPresentationOperation BeginPresentationOperation()
    {
        StartupPresentationOperation operation = new StartupPresentationOperation();
        lock (presentationSync) { activePresentationOperation = operation; }
        return operation;
    }

    private void EndPresentationOperation(StartupPresentationOperation operation)
    {
        lock (presentationSync)
        {
            if (Object.ReferenceEquals(activePresentationOperation, operation))
                activePresentationOperation = null;
        }
    }

    private static Task InvokeTask(Func<Task> operation)
    {
        try
        {
            Task task = operation();
            if (task == null) throw new InvalidOperationException("The asynchronous operation returned no task.");
            return task;
        }
        catch (Exception exception) { return FaultedTask(exception); }
    }

    private static Task<ReadinessResult> InvokeReadinessTask(
        Func<CancellationToken, Task<ReadinessResult>> operation,
        CancellationToken token)
    {
        try
        {
            Task<ReadinessResult> task = operation(token);
            if (task == null) throw new InvalidOperationException("The backend readiness operation returned no task.");
            return task;
        }
        catch (Exception exception)
        {
            TaskCompletionSource<ReadinessResult> failed = new TaskCompletionSource<ReadinessResult>();
            failed.SetException(exception);
            return failed.Task;
        }
    }

    private static Task FaultedTask(Exception exception)
    {
        TaskCompletionSource<bool> failed = new TaskCompletionSource<bool>();
        failed.SetException(exception);
        return failed.Task;
    }

    private void ShowReadinessError(ReadinessResult result)
    {
        if (explicitExitRequested) return;
        if (result != null && result.State == ReadinessState.PortConflict)
            ShowServiceError("Port 8080 is already in use", result.Detail);
        else
            ShowServiceError(
                "DeepSeek Harness could not start",
                result == null ? "The backend readiness check returned no result." : result.Detail);
    }

    private void ShowWebViewError(string detail)
    {
        if (explicitExitRequested) return;
        ShowServiceError(
            "WebView2 could not start",
            "Install or repair the Microsoft Edge WebView2 Runtime, then Retry. " + (detail ?? String.Empty));
    }

    private void ShowServiceError(string title, string detail)
    {
        if (explicitExitRequested) return;
        lock (presentationSync) { hasFinalPresentation = true; }
        window.PresentError(
            title,
            detail ?? String.Empty,
            delegate { Observe(RunColdStartAsync(), "Retry"); },
            OpenLogs,
            delegate { Observe(ExitAsync(), "Exit"); },
            !hiddenByClose);
    }

    private void OpenLogs()
    {
        try { openLogs(); }
        catch (Exception exception)
        {
            log("Unable to open the logs directory: " + exception.Message);
            ShowServiceError("Logs could not be opened", exception.Message);
        }
    }

    private Task RunOnUiAsync(Func<Task> operation)
    {
        if (dispatcher.CheckAccess()) return operation();

        TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        dispatcher.BeginInvoke(delegate {
            Task task;
            try { task = operation(); }
            catch (Exception exception)
            {
                completion.SetException(exception);
                return;
            }
            task.ContinueWith(delegate(Task finished) {
                if (finished.IsCanceled) completion.SetCanceled();
                else if (finished.IsFaulted) completion.SetException(finished.Exception.InnerExceptions);
                else completion.SetResult(true);
            }, TaskScheduler.Default);
        });
        return completion.Task;
    }

    private void Observe(Task task, string operation)
    {
        if (task == null) return;
        task.ContinueWith(delegate(Task failed) {
            Exception exception = failed.Exception == null ? null : failed.Exception.GetBaseException();
            log(operation + " failed: " + (exception == null ? "Unknown error." : exception.Message));
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private void OnWindowClosing(object sender, CancelEventArgs eventArgs)
    {
        if (explicitExitRequested) return;
        eventArgs.Cancel = true;
        hiddenByClose = true;
        window.Hide();
    }

    private void OnTrayOpen(object sender, EventArgs eventArgs)
    {
        Restore(OpenReason.TrayRestore);
    }

    private void OnTrayRestart(object sender, EventArgs eventArgs)
    {
        RunOnUi(delegate { Observe(RestartServiceAsync(), "Restart Service"); });
    }

    private void OnTrayViewLogs(object sender, EventArgs eventArgs)
    {
        RunOnUi(OpenLogs);
    }

    private void OnTrayExit(object sender, EventArgs eventArgs)
    {
        explicitExitRequested = true;
        RunOnUi(delegate { Observe(ExitAsync(), "Exit"); });
    }

    private void RunOnUi(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
