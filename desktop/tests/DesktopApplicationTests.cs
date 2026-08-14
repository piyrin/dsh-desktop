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
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);
            Task run = harness.Application.RunColdStartAsync();

            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());

            AssertEx.True(run.Wait(1000));
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("readiness winning just after threshold never waits for the timer", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());

            Task run = harness.Application.RunColdStartAsync();

            AssertEx.True(run.Wait(1000));
            AssertEx.False(harness.Timer.ThresholdCompletion.Task.IsCompleted);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("exactly three elapsed seconds does not show splash", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Timer.ElapsedValue = TimeSpan.FromSeconds(3);
            Task run = harness.Application.RunColdStartAsync();

            harness.Timer.ThresholdCompletion.SetResult(true);
            Thread.Sleep(20);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());
            AssertEx.True(run.Wait(1000));
        });

        runner.Add("elapsed time beyond three seconds shows splash", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Timer.ElapsedValue = TimeSpan.FromTicks(TimeSpan.FromSeconds(3).Ticks + 1);
            Task run = harness.Application.RunColdStartAsync();

            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());
            AssertEx.True(run.Wait(1000));
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
            AssertEx.True(harness.Window.LogMessages.Exists(delegate(string message) {
                return message.IndexOf("System.InvalidOperationException", StringComparison.Ordinal) >= 0
                    && message.IndexOf("runtime unavailable", StringComparison.Ordinal) >= 0;
            }));

            harness.Window.InitializeCompletion = Completed();
            harness.Backend.EnsureCompletion = Completed(Ready());
            harness.Window.RetryAction();
            AssertEx.True(WaitUntil(delegate { return harness.Window.WebCalls == 1; }));
            AssertEx.Equal(2, harness.Window.InitializeCalls);
        });

        runner.Add("port conflict is actionable and never navigates to the unknown service", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(new ReadinessResult(
                ReadinessState.PortConflict,
                TimeSpan.FromMilliseconds(12),
                "The DSH port is occupied by another application."));

            AssertEx.True(harness.Application.RunColdStartAsync().Wait(1000));
            AssertEx.True(harness.Window.ErrorTitle.IndexOf("Port 8080", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEx.True(harness.Window.RetryAction != null);
            AssertEx.True(harness.Window.ViewLogsAction != null);
            AssertEx.True(harness.Window.ExitAction != null);
            AssertEx.Equal(0, harness.Window.NavigateCalls);
            AssertEx.Equal(0, harness.Window.WebCalls);
        });

        runner.Add("missing DSH dependency is actionable without navigating", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(new ReadinessResult(
                ReadinessState.NotReady,
                TimeSpan.Zero,
                "Required Node or DSH files could not be found. DSH JavaScript entry not found: lib\\bin.js"));

            AssertEx.True(harness.Application.RunColdStartAsync().Wait(1000));
            AssertEx.True(harness.Window.ErrorDetail.IndexOf("lib\\bin.js", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEx.True(harness.Window.RetryAction != null);
            AssertEx.True(harness.Window.ViewLogsAction != null);
            AssertEx.True(harness.Window.ExitAction != null);
            AssertEx.Equal(0, harness.Window.NavigateCalls);
            AssertEx.Equal(0, harness.Window.WebCalls);
        });

        runner.Add("missing installation retry rediscovers and navigates after repair", delegate {
            RecoveringBackendFactory factory = new RecoveringBackendFactory();
            List<string> logs = new List<string>();
            RecoveringDesktopBackend backend = new RecoveringDesktopBackend(
                factory,
                delegate(string message) { logs.Add(message); });
            DesktopHarness harness = new DesktopHarness(backend);
            harness.Window.InitializeCompletion.SetResult(true);

            AssertEx.True(harness.Application.RunColdStartAsync().Wait(1000));
            AssertEx.Equal(1, factory.CreateCalls);
            AssertEx.True(harness.Window.ErrorDetail.IndexOf("lib\\bin.js", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEx.True(harness.Window.RetryAction != null);
            AssertEx.Equal(0, harness.Window.NavigateCalls);

            RecoveringReadyBackend repaired = new RecoveringReadyBackend();
            factory.AvailableBackend = repaired;
            harness.Window.RetryAction();

            AssertEx.True(WaitUntil(delegate { return harness.Window.WebCalls == 1; }));
            AssertEx.Equal(2, factory.CreateCalls);
            AssertEx.Equal(1, repaired.EnsureCalls);
            AssertEx.Equal(1, harness.Window.NavigateCalls);
            AssertEx.Equal(BackendOwnership.Owned, backend.Ownership);
            AssertEx.True(logs.Exists(delegate(string message) {
                return message.IndexOf("rediscovery succeeded", StringComparison.OrdinalIgnoreCase) >= 0;
            }));
        });

        runner.Add("repeated missing installation attempts are rediscovered instead of cached", delegate {
            RecoveringBackendFactory factory = new RecoveringBackendFactory();
            RecoveringDesktopBackend backend = new RecoveringDesktopBackend(factory, delegate(string message) { });

            ReadinessResult first = backend.EnsureReadyAsync(CancellationToken.None).GetAwaiter().GetResult();
            ReadinessResult second = backend.EnsureReadyAsync(CancellationToken.None).GetAwaiter().GetResult();

            AssertEx.Equal(ReadinessState.NotReady, first.State);
            AssertEx.Equal(ReadinessState.NotReady, second.State);
            AssertEx.Equal(2, factory.CreateCalls);
        });

        runner.Add("concurrent recovery creates one backend and exit waits to stop it", delegate {
            RecoveringReadyBackend created = new RecoveringReadyBackend();
            BlockingRecoveringBackendFactory factory = new BlockingRecoveringBackendFactory(created);
            RecoveringDesktopBackend backend = new RecoveringDesktopBackend(factory, delegate(string message) { });
            Task<ReadinessResult>[] readiness = new Task<ReadinessResult>[4];
            for (int index = 0; index < readiness.Length; index++)
            {
                readiness[index] = Task.Run(delegate {
                    return backend.EnsureReadyAsync(CancellationToken.None).GetAwaiter().GetResult();
                });
            }

            AssertEx.True(factory.CreateEntered.WaitOne(1000));
            Task stop = backend.StopOwnedAsync();
            AssertEx.False(stop.IsCompleted);
            factory.AllowCreate.Set();
            AssertEx.True(Task.WaitAll(readiness, 2000));
            AssertEx.True(stop.Wait(2000));

            AssertEx.Equal(1, factory.CreateCalls);
            AssertEx.Equal(4, created.EnsureCalls);
            AssertEx.Equal(1, created.StopCalls);
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

        runner.Add("exit delegates the external ownership decision to the serialized backend stop", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.External;

            harness.Application.ExitAsync().GetAwaiter().GetResult();

            AssertEx.True(harness.Tray.IsDisposed);
            AssertEx.Equal(1, harness.Backend.StopCalls);
        });

        runner.Add("exit stop observes ownership acquired after exit entry", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.None;
            harness.Backend.BeforeStop = delegate {
                harness.Backend.OwnershipValue = BackendOwnership.Owned;
            };

            harness.Application.ExitAsync().GetAwaiter().GetResult();

            AssertEx.Equal(1, harness.Backend.StopCalls);
            AssertEx.Equal(BackendOwnership.Owned, harness.Backend.OwnershipValue);
        });

        runner.Add("tray disposal failure does not interrupt ordered exit teardown", delegate {
            List<string> sequence = new List<string>();
            DesktopHarness harness = new DesktopHarness();
            harness.Tray.Sequence = sequence;
            harness.Tray.ThrowOnDispose = true;
            harness.Backend.Sequence = sequence;
            harness.Window.Sequence = sequence;
            harness.ShutdownAction = delegate { sequence.Add("shutdown"); };

            harness.Application.ExitAsync().GetAwaiter().GetResult();

            AssertEx.Equal("tray.dispose,backend.stop,window.close,shutdown", String.Join(",", sequence.ToArray()));
            AssertEx.True(harness.Window.LogMessages.Exists(delegate(string message) {
                return message.IndexOf("tray", StringComparison.OrdinalIgnoreCase) >= 0;
            }));
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
            AssertEx.Equal(1, harness.Window.WaitingCalls);
            AssertEx.Equal(0, harness.Window.RestoreCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
        });

        runner.Add("secondary activation during cold start shows static waiting then ready content", delegate {
            DesktopHarness harness = new DesktopHarness();
            Task run = harness.Application.RunColdStartAsync();

            harness.Application.Restore(OpenReason.SecondaryActivation);

            AssertEx.Equal(1, harness.Window.WaitingCalls);
            AssertEx.True(harness.Window.IsVisible);
            AssertEx.Equal(0, harness.Window.WebCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());
            AssertEx.True(run.Wait(1000));
            AssertEx.Equal(1, harness.Window.NavigateCalls);
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("secondary activation suppresses only its cold start splash generation", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);
            Task run = harness.Application.RunColdStartAsync();

            harness.Application.Restore(OpenReason.SecondaryActivation);
            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(harness.Timer.ElapsedObserved.Task.Wait(1000));

            AssertEx.Equal(1, harness.Window.WaitingCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            AssertEx.Equal(0, harness.Window.WebCalls);
            AssertEx.True(harness.Window.IsVisible);
            AssertEx.False(run.IsCompleted);

            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());
            AssertEx.True(run.Wait(1000));

            harness.Timer.ThresholdCompletion = new TaskCompletionSource<bool>();
            harness.Timer.ElapsedObserved = new TaskCompletionSource<bool>();
            harness.Backend.OwnershipValue = BackendOwnership.Owned;
            Task restart = harness.Application.RestartServiceAsync();
            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(harness.Timer.ElapsedObserved.Task.Wait(1000));

            AssertEx.Equal(1, harness.Window.SplashCalls);
            harness.Backend.RestartCompletion.SetResult(Ready());
            AssertEx.True(restart.Wait(1000));
        });

        runner.Add("secondary activation suppresses a retry splash while restoring its final error", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.EnsureCompletion.SetResult(new ReadinessResult(
                ReadinessState.NotReady, TimeSpan.FromSeconds(30), "timed out"));
            AssertEx.True(harness.Application.RunColdStartAsync().Wait(1000));
            AssertEx.Equal(1, harness.Window.ErrorCalls);

            harness.Timer.ThresholdCompletion = new TaskCompletionSource<bool>();
            harness.Timer.ElapsedObserved = new TaskCompletionSource<bool>();
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);
            harness.Backend.EnsureCompletion = new TaskCompletionSource<ReadinessResult>();
            harness.Window.RetryAction();
            AssertEx.True(WaitUntil(delegate { return harness.Backend.EnsureCalls == 2; }));

            harness.Application.Restore(OpenReason.SecondaryActivation);
            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(harness.Timer.ElapsedObserved.Task.Wait(1000));

            AssertEx.Equal(1, harness.Window.RestoreCalls);
            AssertEx.Equal(0, harness.Window.WaitingCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            AssertEx.Equal(0, harness.Window.WebCalls);
            AssertEx.Equal(1, harness.Window.ErrorCalls);
            AssertEx.True(harness.Window.IsVisible);
            AssertEx.False(harness.Backend.EnsureCompletion.Task.IsCompleted);

            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());
            AssertEx.True(WaitUntil(delegate { return harness.Window.WebCalls == 1; }));
            AssertEx.Equal(0, harness.Window.SplashCalls);
        });

        runner.Add("ready completion after close never transiently shows the window", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);
            Task run = harness.Application.RunColdStartAsync();
            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Window.RaiseClosing();
            int historyAfterClose = harness.Window.VisibilityHistory.Count;

            harness.Window.InitializeCompletion.SetResult(true);
            harness.Backend.EnsureCompletion.SetResult(Ready());
            AssertEx.True(run.Wait(1000));

            AssertEx.False(harness.Window.WasVisibleAfter(historyAfterClose));
        });

        runner.Add("error completion after close never transiently shows the window", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);
            Task run = harness.Application.RunColdStartAsync();
            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Window.RaiseClosing();
            int historyAfterClose = harness.Window.VisibilityHistory.Count;

            harness.Backend.EnsureCompletion.SetResult(new ReadinessResult(
                ReadinessState.NotReady, TimeSpan.FromSeconds(30), "timed out"));
            AssertEx.True(run.Wait(1000));

            AssertEx.Equal(1, harness.Window.ErrorCalls);
            AssertEx.False(harness.Window.WasVisibleAfter(historyAfterClose));
        });

        runner.Add("owned restart animates only after three second delay", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.Owned;
            harness.Window.InitializeCompletion.SetResult(true);
            harness.Timer.ElapsedValue = TimeSpan.FromMilliseconds(3001);

            Task restart = harness.Application.RestartServiceAsync();
            AssertEx.Equal(1, harness.Backend.RestartCalls);
            AssertEx.Equal(0, harness.Window.SplashCalls);
            harness.Timer.ThresholdCompletion.SetResult(true);
            AssertEx.True(WaitUntil(delegate { return harness.Window.SplashCalls == 1; }));
            harness.Backend.RestartCompletion.SetResult(Ready());

            AssertEx.True(restart.Wait(1000));
            AssertEx.Equal(1, harness.Window.WebCalls);
        });

        runner.Add("restart completion after close never transiently shows the window", delegate {
            DesktopHarness harness = new DesktopHarness();
            harness.Backend.OwnershipValue = BackendOwnership.Owned;
            harness.Window.InitializeCompletion.SetResult(true);
            Task restart = harness.Application.RestartServiceAsync();
            harness.Window.RaiseClosing();
            int historyAfterClose = harness.Window.VisibilityHistory.Count;

            harness.Backend.RestartCompletion.SetResult(Ready());
            AssertEx.True(restart.Wait(1000));

            AssertEx.False(harness.Window.WasVisibleAfter(historyAfterClose));
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
        internal readonly FakeStartupTimer Timer = new FakeStartupTimer();
        internal int ShutdownCalls;
        internal Action ShutdownAction;
        internal DesktopApplication Application;

        private readonly IDesktopBackend backendOverride;

        internal DesktopHarness()
            : this(null)
        {
        }

        internal DesktopHarness(IDesktopBackend backendOverride)
        {
            this.backendOverride = backendOverride;
            ShutdownAction = delegate { ShutdownCalls++; };
            RecreateApplication();
        }

        internal void RecreateApplication()
        {
            Application = new DesktopApplication(
                Window,
                backendOverride ?? Backend,
                Tray,
                Dispatcher,
                delegate { return Timer; },
                delegate { Window.ViewLogsCalls++; },
                delegate { ShutdownAction(); },
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
        internal int WaitingCalls;
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
        internal readonly List<bool> VisibilityHistory = new List<bool>();
        internal List<string> Sequence;
        internal bool IsVisible;

        public event CancelEventHandler Closing;
        public Task InitializeWebViewAsync() { InitializeCalls++; return InitializeCompletion.Task; }
        public Task NavigateToDsh(Uri uri) { NavigateCalls++; return Task.FromResult(true); }
        public void ShowAnimatedSplash() { SplashCalls++; SetVisible(true); }
        public void ShowStaticWaiting() { WaitingCalls++; SetVisible(true); }
        public void PresentWebContent(bool revealWindow)
        {
            WebCalls++;
            if (revealWindow) SetVisible(true);
        }
        public void ShowWebContent() { PresentWebContent(true); }
        public void PresentError(
            string title,
            string detail,
            Action retry,
            Action viewLogs,
            Action exit,
            bool revealWindow)
        {
            RecordError(title, detail, retry, viewLogs, exit);
            if (revealWindow) SetVisible(true);
        }
        public void ShowError(string title, string detail, Action retry, Action viewLogs, Action exit)
        {
            RecordError(title, detail, retry, viewLogs, exit);
            SetVisible(true);
        }
        private void RecordError(string title, string detail, Action retry, Action viewLogs, Action exit)
        {
            ErrorCalls++;
            ErrorTitle = title;
            ErrorDetail = detail;
            RetryAction = retry;
            ViewLogsAction = viewLogs;
            ExitAction = exit;
        }
        public void RestoreWithoutAnimation() { RestoreCalls++; SetVisible(true); }
        public void Hide() { HideCalls++; SetVisible(false); }
        public void Close()
        {
            CloseCalls++;
            if (Sequence != null) Sequence.Add("window.close");
            SetVisible(false);
        }
        internal int SurfaceCalls { get { return SplashCalls + WaitingCalls + WebCalls + ErrorCalls; } }
        internal bool WasVisibleAfter(int historyIndex)
        {
            for (int index = historyIndex; index < VisibilityHistory.Count; index++)
                if (VisibilityHistory[index]) return true;
            return false;
        }
        internal CancelEventArgs RaiseClosing()
        {
            CancelEventArgs arguments = new CancelEventArgs();
            CancelEventHandler handler = Closing;
            if (handler != null) handler(this, arguments);
            return arguments;
        }
        private void SetVisible(bool visible)
        {
            IsVisible = visible;
            VisibilityHistory.Add(visible);
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
        internal List<string> Sequence;
        public BackendOwnership Ownership { get { return OwnershipValue; } }
        public Task<ReadinessResult> EnsureReadyAsync(CancellationToken token) { EnsureCalls++; return EnsureCompletion.Task; }
        public Task<ReadinessResult> RestartAsync(CancellationToken token) { RestartCalls++; return RestartCompletion.Task; }
        public Task StopOwnedAsync()
        {
            StopCalls++;
            if (Sequence != null) Sequence.Add("backend.stop");
            if (BeforeStop != null) BeforeStop();
            return StopCompletion == null ? Task.FromResult(true) : StopCompletion.Task;
        }
    }

    private sealed class RecoveringBackendFactory : IDesktopBackendFactory
    {
        internal int CreateCalls;
        internal IDesktopBackend AvailableBackend;

        public IDesktopBackend Create()
        {
            CreateCalls++;
            if (AvailableBackend == null)
                throw new System.IO.FileNotFoundException("DSH JavaScript entry not found: lib\\bin.js");
            return AvailableBackend;
        }
    }

    private sealed class BlockingRecoveringBackendFactory : IDesktopBackendFactory
    {
        private readonly IDesktopBackend backend;
        internal readonly ManualResetEvent CreateEntered = new ManualResetEvent(false);
        internal readonly ManualResetEvent AllowCreate = new ManualResetEvent(false);
        internal int CreateCalls;

        internal BlockingRecoveringBackendFactory(IDesktopBackend backend)
        {
            this.backend = backend;
        }

        public IDesktopBackend Create()
        {
            Interlocked.Increment(ref CreateCalls);
            CreateEntered.Set();
            AllowCreate.WaitOne();
            return backend;
        }
    }

    private sealed class RecoveringReadyBackend : IDesktopBackend
    {
        internal int EnsureCalls;
        internal int RestartCalls;
        internal int StopCalls;

        public BackendOwnership Ownership { get { return BackendOwnership.Owned; } }
        public Task<ReadinessResult> EnsureReadyAsync(CancellationToken token)
        {
            Interlocked.Increment(ref EnsureCalls);
            return Task.FromResult(Ready());
        }
        public Task<ReadinessResult> RestartAsync(CancellationToken token)
        {
            Interlocked.Increment(ref RestartCalls);
            return Task.FromResult(Ready());
        }
        public Task StopOwnedAsync()
        {
            Interlocked.Increment(ref StopCalls);
            return Task.FromResult(true);
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
        internal bool ThrowOnDispose;
        internal List<string> Sequence;
        public void SetRestartEnabled(bool enabled) { RestartEnabled = enabled; }
        public void Dispose()
        {
            IsDisposed = true;
            if (Sequence != null) Sequence.Add("tray.dispose");
            openRequested = null;
            restartServiceRequested = null;
            viewLogsRequested = null;
            exitRequested = null;
            if (ThrowOnDispose) throw new InvalidOperationException("tray disposal failed");
        }
    }

    private sealed class FakeStartupTimer : IStartupTimer
    {
        internal TaskCompletionSource<bool> ThresholdCompletion = new TaskCompletionSource<bool>();
        internal TaskCompletionSource<bool> ElapsedObserved = new TaskCompletionSource<bool>();
        internal TimeSpan ElapsedValue;
        public TimeSpan Elapsed
        {
            get
            {
                ElapsedObserved.TrySetResult(true);
                return ElapsedValue;
            }
        }
        public Task WaitForThresholdAsync(TimeSpan threshold)
        {
            AssertEx.Equal(TimeSpan.FromSeconds(3), threshold);
            return ThresholdCompletion.Task;
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
