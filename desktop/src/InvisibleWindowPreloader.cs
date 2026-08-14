using System.Threading.Tasks;
using System;

internal interface IInvisiblePreloadWindow
{
    bool IsLoaded { get; }
    bool ShowInTaskbar { get; set; }
    bool ShowActivated { get; set; }
    double Opacity { get; set; }
    Task ShowAndWaitUntilLoadedAsync();
    void Hide();
}

internal static class InvisibleWindowPreloader
{
    internal static async Task PrepareAsync(IInvisiblePreloadWindow window)
    {
        if (window.IsLoaded) return;

        bool originalShowInTaskbar = window.ShowInTaskbar;
        bool originalShowActivated = window.ShowActivated;
        double originalOpacity = window.Opacity;
        Exception primaryFailure = null;
        try
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Opacity = 0.0;
            await window.ShowAndWaitUntilLoadedAsync();
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        Exception cleanupFailure = TryCleanup(null, window.Hide);
        cleanupFailure = TryCleanup(cleanupFailure, delegate { window.Opacity = originalOpacity; });
        cleanupFailure = TryCleanup(cleanupFailure, delegate { window.ShowActivated = originalShowActivated; });
        cleanupFailure = TryCleanup(cleanupFailure, delegate { window.ShowInTaskbar = originalShowInTaskbar; });

        if (primaryFailure != null)
        {
            if (cleanupFailure != null)
            {
                try { primaryFailure.Data["InvisiblePreloadCleanupFailure"] = cleanupFailure; }
                catch (Exception) { }
            }
            throw primaryFailure;
        }
        if (cleanupFailure != null) throw cleanupFailure;
    }

    private static Exception TryCleanup(Exception current, Action cleanup)
    {
        try
        {
            cleanup();
            return current;
        }
        catch (Exception exception)
        {
            return current == null ? exception : new AggregateException(current, exception);
        }
    }
}
