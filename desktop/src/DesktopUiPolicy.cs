using System;

internal enum DesktopSurface
{
    Hidden,
    Splash,
    WebContent,
    Error
}

internal sealed class DesktopPresentationState
{
    internal DesktopSurface Surface { get; private set; }
    internal bool IsSplashAnimationRunning { get; private set; }

    internal void ShowAnimatedSplash()
    {
        Surface = DesktopSurface.Splash;
        IsSplashAnimationRunning = true;
    }

    internal void ShowWebContent()
    {
        Surface = DesktopSurface.WebContent;
        IsSplashAnimationRunning = false;
    }

    internal void ShowError()
    {
        Surface = DesktopSurface.Error;
        IsSplashAnimationRunning = false;
    }

    internal void RestoreWithoutAnimation()
    {
        if (Surface == DesktopSurface.Hidden || Surface == DesktopSurface.Splash)
            Surface = DesktopSurface.WebContent;
        IsSplashAnimationRunning = false;
    }
}

internal static class NavigationPolicy
{
    internal static bool IsAllowedOrigin(Uri allowedOrigin, Uri candidate)
    {
        if (allowedOrigin == null || candidate == null) return false;
        if (!allowedOrigin.IsAbsoluteUri || !candidate.IsAbsoluteUri) return false;

        return String.Equals(allowedOrigin.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
            && String.Equals(allowedOrigin.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
            && allowedOrigin.Port == candidate.Port;
    }

    internal static bool CanOpenExternally(Uri candidate)
    {
        if (candidate == null || !candidate.IsAbsoluteUri) return false;
        return String.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || String.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}

internal enum UiPreviewState
{
    Splash,
    Error,
    Web
}

internal static class UiPreviewStateParser
{
    internal static bool TryParse(string value, out UiPreviewState state)
    {
        if (String.Equals(value, "splash", StringComparison.OrdinalIgnoreCase))
        {
            state = UiPreviewState.Splash;
            return true;
        }
        if (String.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
        {
            state = UiPreviewState.Error;
            return true;
        }
        if (String.Equals(value, "web", StringComparison.OrdinalIgnoreCase))
        {
            state = UiPreviewState.Web;
            return true;
        }

        state = default(UiPreviewState);
        return false;
    }
}
