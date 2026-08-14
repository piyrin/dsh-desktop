using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

internal static class DesktopApplicationTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("program launch selection keeps explicit previews and defaults to normal", delegate {
            UiPreviewState preview;
            AssertEx.Equal(ProgramLaunchMode.Normal, ProgramLaunchModePolicy.Resolve(null, out preview));
            AssertEx.Equal(ProgramLaunchMode.Preview, ProgramLaunchModePolicy.Resolve("splash", out preview));
            AssertEx.Equal(UiPreviewState.Splash, preview);
            AssertEx.Equal(ProgramLaunchMode.Preview, ProgramLaunchModePolicy.Resolve("error", out preview));
            AssertEx.Equal(UiPreviewState.Error, preview);
            AssertEx.Equal(ProgramLaunchMode.Preview, ProgramLaunchModePolicy.Resolve("web", out preview));
            AssertEx.Equal(UiPreviewState.Web, preview);
            AssertEx.Equal(ProgramLaunchMode.InvalidPreview, ProgramLaunchModePolicy.Resolve("unknown", out preview));
        });

        runner.Add("cold start begins WebView and backend concurrently without an early surface", delegate {
            DesktopHarness harness = new DesktopHarness();
            Task run = harness.Application.RunColdStartAsync();

            AssertEx.Equal(1, harness.Window.InitializeCalls);
            AssertEx.Equal(1, harness.Backend.EnsureCalls);
            AssertEx.Equal(0, harness.Window.SurfaceCalls);

            harness.Backend.EnsureCompletion.SetResult(Ready());
            harness.Window.InitializeCompletion.SetResult(true);
            AssertEx.True(run.Wait(1000));
            AssertEx.Equal(1, harness.Window.NavigateCalls);
            AssertEx.Equal(1, harness.Window.WebCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
        });

        runner.Add("cold start shows splash only after threshold while readiness remains incomplete", delegate {
            DesktopHarness harness = new DesktopHarness();
            Task run = harness.Application.RunColdStartAsync();

            harness.DelayCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());

            AssertEx.True(run.Wait(1000));
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("ready cold start never waits for the splash delay", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());

            Task run = harness.Application.RunColdStartAsync();

            AssertEx.True(run.Wait(1000));
            AssertEx.False(harness.DelayCompletion.Task.IsCompleted);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("WebView failure is actionable and retry initiates a fresh attempt", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Window.InitializeCompletion.SetException(new InvalidOperationException("runtime unavailable"));
            harness.Backend.EnsureCompletion.SetResult(Ready());

            AssertEx.True(harness.Application.RunColdStartAsync().Wait(1000));
            AssertEx.True(harness.Window.ErrorTitle.IndexOf("WebView2", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEx.True(harness.Window.ErrorDetail.IndexOf("runtime unavailable", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEx.True(harness.Window.RetryAction != null);
            AssertEx.True(harness.Window.ViewLogsAction != null);
            AssertEx.True(harness.Window.ExitAction != null);

            harness.Window.InitializeCompletion = Completed();
            harness.Backend.EnsureCompletion = Completed(Ready());
            harness.Window.RetryAction();
            AssertEx.True(WaitUntil(delegate { return harness.Window.WebCalls == 1; }));
            AssertEx.Equal(2, harness.Window.InitializeCalls);
        });

        runner.Add("failed retryable operation starts a new task on the next call", delegate {
            int attempts = 0;
            RetryableAsyncOperation operation = new RetryableAsyncOperation();
            Func<Task> factory = delegate {
                attempts++;
                if (attempts == 1)
                {
                    TaskCompletionSource<bool> failed = new TaskCompletionSource<bool>();
                    failed.SetException(new InvalidOperationException("first failure"));
                    return failed.Task;
                }
                return Task.FromResult(true);
            };

            bool failedFirst = false;
            try { operation.Run(factory).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { failedFirst = true; }
            operation.Run(factory).GetAwaiter().GetResult();

            AssertEx.True(failedFirst);
            AssertEx.Equal(2, attempts);
        });

        runner.Add("window close hides without stopping backend or disposing tray", delegate {
            DesktopHarness harness = new DesktopHarness();

            CancelEventArgs closing = harness.Window.RaiseClosing();

            AssertEx.True(closing.Cancel);
            AssertEx.Equal(1, harness.Window.HideCalls);
            AssertEx.Equal(0, harness.Backend.StopCalls);
            AssertEx.False(harness.Tray.IsDisposed);
        });

        runner.Add("explicit exit disposes tray before stopping an owned backend", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.Owned;
            harness.Backend.BeforeStop = delegate { AssertEx.True(harness.Tray.IsDisposed); };

            harness.Application.ExitAsync().GetAwaiter().GetResult();

            AssertEx.Equal(1, harness.Backend.StopCalls);
            AssertEx.Equal(1, harness.Window.CloseCalls);
            AssertEx.Equal(1, harness.ShutdownCalls);
        });

        runner.Add("explicit exit never asks to stop an external backend", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.External;

            harness.Application.ExitAsync().GetAwaiter().GetResult();

            AssertEx.True(harness.Tray.IsDisposed);
            AssertEx.Equal(0, harness.Backend.StopCalls);
        });

        runner.Add("close racing with explicit exit is not converted into hide", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.Owned;
            harness.Backend.StopCompletion = new TaskCompletionSource<bool>();

            Task exit = harness.Application.ExitAsync();
            CancelEventArgs closing = harness.Window.RaiseClosing();
            AssertEx.False(closing.Cancel);
            AssertEx.Equal(0, harness.Window.HideCalls);
            harness.Backend.StopCompletion.SetResult(true);
            AssertEx.True(exit.Wait(1000));
        });

        runner.Add("secondary activation marshals restore and never starts splash", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Dispatcher.HasAccess = false;

            harness.Application.Restore(OpenReason.SecondaryActivation);
            AssertEx.Equal(0, harness.Window.RestoreCalls);
            AssertEx.Equal(1, harness.Dispatcher.PendingCount);

            harness.Dispatcher.HasAccess = true;
            harness.Dispatcher.RunPending();
            AssertEx.Equal(1, harness.Window.RestoreCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
        });

        runner.Add("owned restart animates only after three second delay", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.Owned;
            harness.Window.InitializeCompletion.SetResult(true);

            Task restart = harness.Application.RestartServiceAsync();
            AssertEx.Equal(1, harness.Backend.RestartCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            harness.DelayCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Backend.RestartCompletion.SetResult(Ready());

            AssertEx.True(restart.Wait(1000));
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("external restart stays disabled and does not call backend", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.External;
            harness.Application.RefreshTrayAvailability();

            harness.Application.RestartServiceAsync().GetAwaiter().GetResult();

            AssertEx.False(harness.Tray.RestartEnabled);
            AssertEx.Equal(0, harness.Backend.RestartCalls);
        });
    }

    private static ReadinessResult Ready()
    {
        return new ReadinessResult(ReadinessState.Ready, TimeSpan.FromMilliseconds(20), "ready");
    }

    private static TaskCompletionSource<bool> Completed()
    {
        TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        completion.SetResult(true);
        return completion;
    }

    private static TaskCompletionSource<ReadinessResult> Completed(ReadinessResult result)
    {
        TaskCompletionSource<ReadinessResult> completion = new TaskCompletionSource<ReadinessResult>();
        completion.SetResult(result);
        return completion;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    private sealed class DesktopHarness
    {
        internal readonly FakeWindow Window = new FakeWindow();
        internal readonly FakeBackend Backend = new FakeBackend();
        internal readonly FakeTray Tray = new FakeTray();
        internal readonly FakeDispatcher Dispatcher = new FakeDispatcher();
        internal TaskCompletionSource<bool> DelayCompletion = new TaskCompletionSource<bool>();
        internal int ShutdownCalls;
        internal DesktopApplication Application;

        internal DesktopHarness()
        {
            Application = new DesktopApplication(
                Window,
                Backend,
                Tray,
                Dispatcher,
                delegate(TimeSpan delay) { return DelayCompletion.Task; },
                delegate { Window.ViewLogsCalls++; },
                delegate { ShutdownCalls++; },
                delegate(string message) { Window.LogMessages.Add(message); },
                new Uri("http://127.0.0.1:8080/"));
        }
    }

    private sealed class FakeWindow : IDesktopWindow
    {
        internal TaskCompletionSource<bool> InitializeCompletion = new TaskCompletionSource<bool>();
        internal int InitializeCalls;
        internal int NavigateCalls;
        internal int SplashCalls;
        internal int WebCalls;
        internal int ErrorCalls;
        internal int RestoreCalls;
        internal int HideCalls;
        internal int CloseCalls;
        internal int ViewLogsCalls;
        internal string ErrorTitle;
        internal string ErrorDetail;
        internal Action RetryAction;
        internal Action ViewLogsAction;
        internal Action ExitAction;
        internal readonly List<string> LogMessages = new List<string>();

        public event CancelEventHandler Closing;
        public Task InitializeWebViewAsync() { InitializeCalls++; return InitializeCompletion.Task; }
        public Task NavigateToDsh(Uri uri) { NavigateCalls++; return Task.FromResult(true); }
        public void ShowAnimatedSplash() { SplashCalls++; }
        public void ShowWebContent() { WebCalls++; }
        public void ShowError(string title, string detail, Action retry, Action viewLogs, Action exit)
        {
            ErrorCalls++;
            ErrorTitle = title;
            ErrorDetail = detail;
            RetryAction = retry;
            ViewLogsAction = viewLogs;
            ExitAction = exit;
        }
        public void RestoreWithoutAnimation() { RestoreCalls++; }
        public void Hide() { HideCalls++; }
        public void Close() { CloseCalls++; }
        internal int SurfaceCalls { get { return SplashCalls + WebCalls + ErrorCalls; } }
        internal CancelEventArgs RaiseClosing()
        {
            CancelEventArgs arguments = new CancelEventArgs();
            CancelEventHandler handler = Closing;
            if (handler != null) handler(this, arguments);
            return arguments;
        }
    }

    private sealed class FakeBackend : IDesktopBackend
    {
        internal BackendOwnership OwnershipValue = BackendOwnership.None;
        internal TaskCompletionSource<ReadinessResult> EnsureCompletion = new TaskCompletionSource<ReadinessResult>();
        internal TaskCompletionSource<ReadinessResult> RestartCompletion = new TaskCompletionSource<ReadinessResult>();
        internal TaskCompletionSource<bool> StopCompletion;
        internal int EnsureCalls;
        internal int RestartCalls;
        internal int StopCalls;
        internal Action BeforeStop;
        public BackendOwnership Ownership { get { return OwnershipValue; } }
        public Task<ReadinessResult> EnsureReadyAsync(CancellationToken token) { EnsureCalls++; return EnsureCompletion.Task; }
        public Task<ReadinessResult> RestartAsync(CancellationToken token) { RestartCalls++; return RestartCompletion.Task; }
        public Task StopOwnedAsync()
        {
            StopCalls++;
            if (BeforeStop != null) BeforeStop();
            return StopCompletion == null ? Task.FromResult(true) : StopCompletion.Task;
        }
    }

    private sealed class FakeTray : IDesktopTray
    {
        private EventHandler openRequested;
        private EventHandler restartServiceRequested;
        private EventHandler viewLogsRequested;
        private EventHandler exitRequested;
        public event EventHandler OpenRequested
        {
            add { openRequested += value; }
            remove { openRequested -= value; }
        }
        public event EventHandler RestartServiceRequested
        {
            add { restartServiceRequested += value; }
            remove { restartServiceRequested -= value; }
        }
        public event EventHandler ViewLogsRequested
        {
            add { viewLogsRequested += value; }
            remove { viewLogsRequested -= value; }
        }
        public event EventHandler ExitRequested
        {
            add { exitRequested += value; }
            remove { exitRequested -= value; }
        }
        internal bool IsDisposed;
        internal bool RestartEnabled;
        public void SetRestartEnabled(bool enabled) { RestartEnabled = enabled; }
        public void Dispose()
        {
            IsDisposed = true;
            openRequested = null;
            restartServiceRequested = null;
            viewLogsRequested = null;
            exitRequested = null;
        }
    }

    private sealed class FakeDispatcher : IDesktopDispatcher
    {
        private readonly Queue<Action> pending = new Queue<Action>();
        internal bool HasAccess = true;
        public bool CheckAccess() { return HasAccess; }
        public void BeginInvoke(Action action) { pending.Enqueue(action); }
        internal int PendingCount { get { return pending.Count; } }
        internal void RunPending()
        {
            while (pending.Count > 0) pending.Dequeue()();
        }
    }
}
