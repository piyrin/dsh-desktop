using System;

internal interface IWebViewRenderHost
{
    bool IsInitialized { get; }
    void MakeWebViewVisible();
    void ShowWindow();
    void UpdateLayout();
    void UpdateWindowPosition();
}

internal static class WebViewRenderSynchronizer
{
    internal static void Present(IWebViewRenderHost host, bool revealWindow)
    {
        if (host == null) throw new ArgumentNullException("host");

        host.MakeWebViewVisible();
        if (!revealWindow) return;

        host.ShowWindow();
        if (!host.IsInitialized) return;

        host.UpdateLayout();
        host.UpdateWindowPosition();
    }
}
