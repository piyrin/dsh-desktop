using System;

internal static class DesktopUiPolicyTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("animated splash marks only the splash as active", delegate {
            DesktopPresentationState state = new DesktopPresentationState();

            state.ShowAnimatedSplash();

            AssertEx.Equal(DesktopSurface.Splash, state.Surface);
            AssertEx.True(state.IsSplashAnimationRunning);
        });

        runner.Add("web content stops splash animation immediately", delegate {
            DesktopPresentationState state = new DesktopPresentationState();
            state.ShowAnimatedSplash();

            state.ShowWebContent();

            AssertEx.Equal(DesktopSurface.WebContent, state.Surface);
            AssertEx.False(state.IsSplashAnimationRunning);
        });

        runner.Add("error content stops splash animation immediately", delegate {
            DesktopPresentationState state = new DesktopPresentationState();
            state.ShowAnimatedSplash();

            state.ShowError();

            AssertEx.Equal(DesktopSurface.Error, state.Surface);
            AssertEx.False(state.IsSplashAnimationRunning);
        });

        runner.Add("restore bypasses splash animation", delegate {
            DesktopPresentationState state = new DesktopPresentationState();
            state.ShowAnimatedSplash();

            state.RestoreWithoutAnimation();

            AssertEx.Equal(DesktopSurface.WebContent, state.Surface);
            AssertEx.False(state.IsSplashAnimationRunning);
        });

        runner.Add("restore preserves an actionable error", delegate {
            DesktopPresentationState state = new DesktopPresentationState();
            state.ShowError();

            state.RestoreWithoutAnimation();

            AssertEx.Equal(DesktopSurface.Error, state.Surface);
            AssertEx.False(state.IsSplashAnimationRunning);
        });

        runner.Add("only animated splash requests borderless window chrome", delegate {
            DesktopPresentationState state = new DesktopPresentationState();

            AssertEx.Equal(DesktopWindowChromeMode.Native, state.WindowChrome);
            state.ShowAnimatedSplash();
            AssertEx.Equal(DesktopWindowChromeMode.Borderless, state.WindowChrome);
            state.ShowWebContent();
            AssertEx.Equal(DesktopWindowChromeMode.Native, state.WindowChrome);
            state.ShowAnimatedSplash();
            state.ShowError();
            AssertEx.Equal(DesktopWindowChromeMode.Native, state.WindowChrome);
            state.ShowAnimatedSplash();
            state.RestoreWithoutAnimation();
            AssertEx.Equal(DesktopWindowChromeMode.Native, state.WindowChrome);
        });

        runner.Add("navigation allows the configured origin regardless of path", delegate {
            Uri configured = new Uri("http://127.0.0.1:8080/");

            AssertEx.True(NavigationPolicy.IsAllowedOrigin(
                configured, new Uri("http://127.0.0.1:8080/chats/42?panel=files")));
        });

        runner.Add("navigation rejects a different origin", delegate {
            Uri configured = new Uri("http://127.0.0.1:8080/");

            AssertEx.False(NavigationPolicy.IsAllowedOrigin(
                configured, new Uri("http://localhost:8080/")));
            AssertEx.False(NavigationPolicy.IsAllowedOrigin(
                configured, new Uri("https://127.0.0.1:8080/")));
            AssertEx.False(NavigationPolicy.IsAllowedOrigin(
                configured, new Uri("http://127.0.0.1:8081/")));
        });

        runner.Add("external requests are limited to browser protocols", delegate {
            AssertEx.True(NavigationPolicy.CanOpenExternally(new Uri("https://deepseek.com/")));
            AssertEx.True(NavigationPolicy.CanOpenExternally(new Uri("http://example.test/")));
            AssertEx.False(NavigationPolicy.CanOpenExternally(new Uri("file:///C:/Windows/win.ini")));
        });

        runner.Add("preview parser accepts only the three explicit states", delegate {
            UiPreviewState state;

            AssertEx.True(UiPreviewStateParser.TryParse("splash", out state));
            AssertEx.Equal(UiPreviewState.Splash, state);
            AssertEx.True(UiPreviewStateParser.TryParse("error", out state));
            AssertEx.Equal(UiPreviewState.Error, state);
            AssertEx.True(UiPreviewStateParser.TryParse("web", out state));
            AssertEx.Equal(UiPreviewState.Web, state);
            AssertEx.False(UiPreviewStateParser.TryParse("", out state));
            AssertEx.False(UiPreviewStateParser.TryParse("ready", out state));
        });
    }
}
