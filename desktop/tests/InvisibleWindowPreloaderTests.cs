using System;
using System.Threading.Tasks;

internal static class InvisibleWindowPreloaderTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("WebView preload loads an invisible nonactivating host then hides it", delegate {
            FakePreloadWindow window = new FakePreloadWindow();

            InvisibleWindowPreloader.PrepareAsync(window).GetAwaiter().GetResult();

            AssertEx.Equal(1, window.ShowCalls);
            AssertEx.Equal(1, window.HideCalls);
            AssertEx.False(window.ShowInTaskbarWhenShown);
            AssertEx.False(window.ShowActivatedWhenShown);
            AssertEx.Equal(0.0, window.OpacityWhenShown);
            AssertEx.Equal(DesktopWindowChromeMode.Borderless, window.WindowChromeWhenShown);
            AssertEx.True(window.ShowInTaskbar);
            AssertEx.True(window.ShowActivated);
            AssertEx.Equal(1.0, window.Opacity);
            AssertEx.Equal(DesktopWindowChromeMode.Native, window.WindowChrome);
        });

        runner.Add("WebView preload leaves an already loaded host untouched", delegate {
            FakePreloadWindow window = new FakePreloadWindow();
            window.IsLoadedValue = true;

            InvisibleWindowPreloader.PrepareAsync(window).GetAwaiter().GetResult();

            AssertEx.Equal(0, window.ShowCalls);
            AssertEx.Equal(0, window.HideCalls);
        });

        runner.Add("WebView preload fault hides and restores without replacing the original failure", delegate {
            InvalidOperationException original = new InvalidOperationException("preload failed after show");
            CleanupFaultingPreloadWindow window = new CleanupFaultingPreloadWindow(original);
            Exception observed = null;

            try { InvisibleWindowPreloader.PrepareAsync(window).GetAwaiter().GetResult(); }
            catch (Exception exception) { observed = exception; }

            AssertEx.True(Object.ReferenceEquals(original, observed));
            AssertEx.Equal(1, window.HideCalls);
            AssertEx.False(window.IsVisible);
            AssertEx.Equal("hide,chrome,opacity,activate,taskbar", String.Join(",", window.CleanupSequence.ToArray()));
            AssertEx.Equal(1.0, window.Opacity);
            AssertEx.True(window.ShowActivated);
            AssertEx.True(window.ShowInTaskbar);
            AssertEx.Equal(DesktopWindowChromeMode.Native, window.WindowChrome);
        });

        runner.Add("WebView preload cleanup preserves the original throw-site stack", delegate {
            InvalidOperationException original = CapturePreloadThrowSite();
            CleanupFaultingPreloadWindow window = new CleanupFaultingPreloadWindow(original);
            Exception observed = null;

            try { InvisibleWindowPreloader.PrepareAsync(window).GetAwaiter().GetResult(); }
            catch (Exception exception) { observed = exception; }

            AssertEx.True(Object.ReferenceEquals(original, observed));
            AssertEx.True(observed.StackTrace != null
                && observed.StackTrace.IndexOf("ThrowFromPreloadThrowSite", StringComparison.Ordinal) >= 0);
            AssertEx.Equal(1, window.HideCalls);
            AssertEx.False(window.IsVisible);
            AssertEx.Equal("hide,chrome,opacity,activate,taskbar", String.Join(",", window.CleanupSequence.ToArray()));
            AssertEx.Equal(1.0, window.Opacity);
            AssertEx.True(window.ShowActivated);
            AssertEx.True(window.ShowInTaskbar);
            AssertEx.Equal(DesktopWindowChromeMode.Native, window.WindowChrome);
        });
    }

    private sealed class FakePreloadWindow : IInvisiblePreloadWindow
    {
        internal bool IsLoadedValue;
        internal int ShowCalls;
        internal int HideCalls;
        internal bool ShowInTaskbarWhenShown;
        internal bool ShowActivatedWhenShown;
        internal double OpacityWhenShown;
        internal DesktopWindowChromeMode WindowChromeWhenShown;

        public bool IsLoaded { get { return IsLoadedValue; } }
        public bool ShowInTaskbar { get; set; }
        public bool ShowActivated { get; set; }
        public double Opacity { get; set; }
        public DesktopWindowChromeMode WindowChrome { get; set; }

        internal FakePreloadWindow()
        {
            ShowInTaskbar = true;
            ShowActivated = true;
            Opacity = 1.0;
            WindowChrome = DesktopWindowChromeMode.Native;
        }

        public Task ShowAndWaitUntilLoadedAsync()
        {
            ShowCalls++;
            ShowInTaskbarWhenShown = ShowInTaskbar;
            ShowActivatedWhenShown = ShowActivated;
            OpacityWhenShown = Opacity;
            WindowChromeWhenShown = WindowChrome;
            IsLoadedValue = true;
            return Task.FromResult(true);
        }

        public void Hide()
        {
            HideCalls++;
        }
    }

    private sealed class CleanupFaultingPreloadWindow : IInvisiblePreloadWindow
    {
        private readonly Exception preloadFailure;
        private bool showInTaskbar = true;
        private bool showActivated = true;
        private double opacity = 1.0;
        private DesktopWindowChromeMode windowChrome = DesktopWindowChromeMode.Native;
        private bool cleanupFaultsArmed;

        internal readonly System.Collections.Generic.List<string> CleanupSequence =
            new System.Collections.Generic.List<string>();
        internal int HideCalls;
        internal bool IsVisible;

        internal CleanupFaultingPreloadWindow(Exception preloadFailure)
        {
            this.preloadFailure = preloadFailure;
        }

        public bool IsLoaded { get { return false; } }
        public bool ShowInTaskbar
        {
            get { return showInTaskbar; }
            set
            {
                showInTaskbar = value;
                if (cleanupFaultsArmed && value)
                {
                    CleanupSequence.Add("taskbar");
                    throw new InvalidOperationException("taskbar restore failed");
                }
            }
        }
        public bool ShowActivated
        {
            get { return showActivated; }
            set
            {
                showActivated = value;
                if (cleanupFaultsArmed && value)
                {
                    CleanupSequence.Add("activate");
                    throw new InvalidOperationException("activation restore failed");
                }
            }
        }
        public double Opacity
        {
            get { return opacity; }
            set
            {
                opacity = value;
                if (cleanupFaultsArmed && value == 1.0)
                {
                    CleanupSequence.Add("opacity");
                    throw new InvalidOperationException("opacity restore failed");
                }
            }
        }
        public DesktopWindowChromeMode WindowChrome
        {
            get { return windowChrome; }
            set
            {
                windowChrome = value;
                if (cleanupFaultsArmed && value == DesktopWindowChromeMode.Native)
                {
                    CleanupSequence.Add("chrome");
                    throw new InvalidOperationException("chrome restore failed");
                }
            }
        }

        public Task ShowAndWaitUntilLoadedAsync()
        {
            IsVisible = true;
            cleanupFaultsArmed = true;
            TaskCompletionSource<bool> failed = new TaskCompletionSource<bool>();
            failed.SetException(preloadFailure);
            return failed.Task;
        }

        public void Hide()
        {
            HideCalls++;
            IsVisible = false;
            CleanupSequence.Add("hide");
            throw new InvalidOperationException("hide failed");
        }
    }

    private static InvalidOperationException CapturePreloadThrowSite()
    {
        try { ThrowFromPreloadThrowSite(); }
        catch (InvalidOperationException exception) { return exception; }
        throw new InvalidOperationException("The preload throw-site helper did not throw.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowFromPreloadThrowSite()
    {
        throw new InvalidOperationException("preload failed at the marked throw site");
    }
}
