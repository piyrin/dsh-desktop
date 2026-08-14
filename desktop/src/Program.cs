using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

internal static class Program
{
    private static readonly Uri DshUri = new Uri("http://127.0.0.1:8080/");

    [STAThread]
    internal static int Main()
    {
        string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
        AppPaths paths = AppPaths.Create(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            applicationDirectory);

        string requestedState = Environment.GetEnvironmentVariable("DSH_DESKTOP_UI_TEST_STATE");
        UiPreviewState previewState;
        ProgramLaunchMode launchMode = ProgramLaunchModePolicy.Resolve(requestedState, out previewState);
        if (launchMode == ProgramLaunchMode.Preview)
            return RunPreview(paths, previewState);
        if (launchMode == ProgramLaunchMode.InvalidPreview)
        {
            TryLog(paths, "UI preview did not start. Expected splash, error, or web. Received: " + requestedState);
            return 2;
        }

        using (SingleInstanceCoordinator coordinator = new SingleInstanceCoordinator())
        {
            try
            {
                if (!coordinator.TryBecomePrimary())
                {
                    if (!coordinator.SignalPrimary(TimeSpan.FromMilliseconds(1500)))
                        TryLog(paths, "A primary desktop instance exists, but activation was not acknowledged within 1500 ms.");
                    return 0;
                }
            }
            catch (Exception exception)
            {
                TryLog(paths, "Single-instance startup failed: " + exception);
                return 1;
            }

            return RunPrimary(paths, applicationDirectory, coordinator);
        }
    }

    private static int RunPrimary(
        AppPaths paths,
        string applicationDirectory,
        SingleInstanceCoordinator coordinator)
    {
        Application application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        MainWindow window = null;
        TrayController tray = null;
        try
        {
            window = new MainWindow(paths);
            application.MainWindow = window;
            tray = new TrayController(Path.Combine(applicationDirectory, "dsh-whale.ico"));
            IDesktopBackend backend = CreateBackend(paths);
            DesktopApplication desktop = new DesktopApplication(
                new MainWindowAdapter(window),
                backend,
                tray,
                new WpfDispatcherAdapter(application.Dispatcher),
                delegate(TimeSpan duration) { return Task.Delay(duration); },
                delegate { OpenLogs(paths); },
                application.Shutdown,
                delegate(string message) { TryLog(paths, message); },
                DshUri);

            coordinator.ActivateRequested += delegate {
                try
                {
                    application.Dispatcher.BeginInvoke(
                        new Action(delegate { desktop.Restore(OpenReason.SecondaryActivation); }),
                        DispatcherPriority.Send);
                }
                catch (InvalidOperationException exception)
                {
                    TryLog(paths, "Secondary activation could not reach the UI dispatcher: " + exception.Message);
                }
            };

            application.Dispatcher.BeginInvoke(new Action(delegate {
                Observe(desktop.RunColdStartAsync(), paths, "Cold startup");
            }), DispatcherPriority.Normal);

            return application.Run();
        }
        catch (Exception exception)
        {
            TryLog(paths, "Desktop shell startup failed: " + exception);
            if (tray != null) tray.Dispose();
            if (window != null)
            {
                try { window.Close(); }
                catch (Exception) { }
            }
            return 1;
        }
        finally
        {
            if (tray != null) tray.Dispose();
        }
    }

    private static IDesktopBackend CreateBackend(AppPaths paths)
    {
        try
        {
            DshInstallation installation = DshInstallation.Discover(
                Environment.GetEnvironmentVariable("PATH"));
            BackendLaunchSpec launchSpec = BackendLaunchSpec.Create(installation, paths, 8080);
            return new BackendSupervisorAdapter(new BackendSupervisor(launchSpec, paths));
        }
        catch (Exception exception)
        {
            string detail = "Required Node or DSH files could not be found. Verify that node.exe and dsh.cmd are on PATH, then Retry. "
                + exception.Message;
            TryLog(paths, detail);
            return new UnavailableDesktopBackend(detail);
        }
    }

    private static int RunPreview(AppPaths paths, UiPreviewState previewState)
    {
        Application application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        MainWindow window = new MainWindow(paths);
        application.MainWindow = window;

        if (previewState == UiPreviewState.Splash)
        {
            window.ShowAnimatedSplash();
        }
        else if (previewState == UiPreviewState.Error)
        {
            Action inertPreviewAction = delegate { };
            window.ShowError(
                "DeepSeek Harness could not start",
                "The local service did not become ready. Retry the startup, inspect the desktop logs for details, or exit the application.",
                inertPreviewAction,
                inertPreviewAction,
                inertPreviewAction);
        }
        else
        {
            window.ShowWebContent();
        }

        return application.Run();
    }

    private static void OpenLogs(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Logs);
        Process.Start(new ProcessStartInfo(paths.Logs) { UseShellExecute = true });
    }

    private static void Observe(Task task, AppPaths paths, string operation)
    {
        task.ContinueWith(delegate(Task failed) {
            Exception exception = failed.Exception == null ? null : failed.Exception.GetBaseException();
            TryLog(paths, operation + " failed: " + (exception == null ? "Unknown error." : exception.ToString()));
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private static void TryLog(AppPaths paths, string message)
    {
        try { AppLogger.WriteDesktop(paths, message); }
        catch (Exception) { }
    }

    private sealed class MainWindowAdapter : IDesktopWindow
    {
        private readonly MainWindow window;

        internal MainWindowAdapter(MainWindow window)
        {
            this.window = window;
        }

        public event CancelEventHandler Closing
        {
            add { window.Closing += value; }
            remove { window.Closing -= value; }
        }

        public Task InitializeWebViewAsync() { return window.InitializeWebViewAsync(); }
        public Task NavigateToDsh(Uri uri) { return window.NavigateToDsh(uri); }
        public void ShowAnimatedSplash() { window.ShowAnimatedSplash(); }
        public void ShowWebContent() { window.ShowWebContent(); }
        public void ShowError(string title, string detail, Action retry, Action viewLogs, Action exit)
        {
            window.ShowError(title, detail, retry, viewLogs, exit);
        }
        public void RestoreWithoutAnimation() { window.RestoreWithoutAnimation(); }
        public void Hide() { window.Hide(); }
        public void Close() { window.Close(); }
    }

    private sealed class BackendSupervisorAdapter : IDesktopBackend
    {
        private readonly BackendSupervisor supervisor;

        internal BackendSupervisorAdapter(BackendSupervisor supervisor)
        {
            this.supervisor = supervisor;
        }

        public BackendOwnership Ownership { get { return supervisor.Ownership; } }
        public Task<ReadinessResult> EnsureReadyAsync(CancellationToken token)
        {
            return supervisor.EnsureReadyAsync(token);
        }
        public Task<ReadinessResult> RestartAsync(CancellationToken token)
        {
            return supervisor.RestartAsync(token);
        }
        public Task StopOwnedAsync() { return supervisor.StopOwnedAsync(); }
    }

    private sealed class UnavailableDesktopBackend : IDesktopBackend
    {
        private readonly ReadinessResult failure;

        internal UnavailableDesktopBackend(string detail)
        {
            failure = new ReadinessResult(ReadinessState.NotReady, TimeSpan.Zero, detail);
        }

        public BackendOwnership Ownership { get { return BackendOwnership.None; } }
        public Task<ReadinessResult> EnsureReadyAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                TaskCompletionSource<ReadinessResult> cancelled = new TaskCompletionSource<ReadinessResult>();
                cancelled.SetCanceled();
                return cancelled.Task;
            }
            return Task.FromResult(failure);
        }
        public Task<ReadinessResult> RestartAsync(CancellationToken token)
        {
            return EnsureReadyAsync(token);
        }
        public Task StopOwnedAsync() { return Task.FromResult(true); }
    }

    private sealed class WpfDispatcherAdapter : IDesktopDispatcher
    {
        private readonly Dispatcher dispatcher;

        internal WpfDispatcherAdapter(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        public bool CheckAccess() { return dispatcher.CheckAccess(); }
        public void BeginInvoke(Action action)
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }
    }
}
