using System.Threading.Tasks;

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
        try
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Opacity = 0.0;
            await window.ShowAndWaitUntilLoadedAsync();
            window.Hide();
        }
        finally
        {
            window.Opacity = originalOpacity;
            window.ShowActivated = originalShowActivated;
            window.ShowInTaskbar = originalShowInTaskbar;
        }
    }
}
