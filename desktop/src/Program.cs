using System;
using System.IO;
using System.Windows;

internal static class Program
{
    [STAThread]
    internal static int Main()
    {
        string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
        AppPaths paths = AppPaths.Create(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            applicationDirectory);

        UiPreviewState previewState;
        string requestedState = Environment.GetEnvironmentVariable("DSH_DESKTOP_UI_TEST_STATE");
        if (!UiPreviewStateParser.TryParse(requestedState, out previewState))
        {
            AppLogger.WriteDesktop(
                paths,
                "UI preview did not start. Set DSH_DESKTOP_UI_TEST_STATE to splash, error, or web. Received: "
                    + (requestedState ?? "<unset>"));
            return 2;
        }

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
}
