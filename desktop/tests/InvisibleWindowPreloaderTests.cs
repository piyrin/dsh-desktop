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
            AssertEx.True(window.ShowInTaskbar);
            AssertEx.True(window.ShowActivated);
            AssertEx.Equal(1.0, window.Opacity);
        });

        runner.Add("WebView preload leaves an already loaded host untouched", delegate {
            FakePreloadWindow window = new FakePreloadWindow();
            window.IsLoadedValue = true;

            InvisibleWindowPreloader.PrepareAsync(window).GetAwaiter().GetResult();

            AssertEx.Equal(0, window.ShowCalls);
            AssertEx.Equal(0, window.HideCalls);
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

        public bool IsLoaded { get { return IsLoadedValue; } }
        public bool ShowInTaskbar { get; set; }
        public bool ShowActivated { get; set; }
        public double Opacity { get; set; }

        internal FakePreloadWindow()
        {
            ShowInTaskbar = true;
            ShowActivated = true;
            Opacity = 1.0;
        }

        public Task ShowAndWaitUntilLoadedAsync()
        {
            ShowCalls++;
            ShowInTaskbarWhenShown = ShowInTaskbar;
            ShowActivatedWhenShown = ShowActivated;
            OpacityWhenShown = Opacity;
            IsLoadedValue = true;
            return Task.FromResult(true);
        }

        public void Hide()
        {
            HideCalls++;
        }
    }
}
