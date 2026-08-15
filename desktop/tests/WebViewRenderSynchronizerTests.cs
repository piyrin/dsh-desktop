using System;
using System.Collections.Generic;

internal static class WebViewRenderSynchronizerTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("first web presentation refreshes the preloaded controller after showing the window", delegate {
            FakeRenderHost host = new FakeRenderHost { IsInitializedValue = true };

            WebViewRenderSynchronizer.Present(host, true);

            AssertEx.True(host.IsRenderable);
            AssertEx.Equal("visible,show,layout,position", String.Join(",", host.Sequence.ToArray()));
        });

        runner.Add("hidden ready completion does not reopen or refresh the window", delegate {
            FakeRenderHost host = new FakeRenderHost { IsInitializedValue = true };

            WebViewRenderSynchronizer.Present(host, false);

            AssertEx.True(host.IsWebViewVisible);
            AssertEx.False(host.IsWindowVisible);
            AssertEx.False(host.IsRenderable);
            AssertEx.Equal("visible", String.Join(",", host.Sequence.ToArray()));
        });
    }

    private sealed class FakeRenderHost : IWebViewRenderHost
    {
        internal readonly List<string> Sequence = new List<string>();
        internal bool IsInitializedValue;
        internal bool IsWebViewVisible;
        internal bool IsWindowVisible;
        internal bool HasCurrentLayout;
        internal bool IsRenderable;

        public bool IsInitialized { get { return IsInitializedValue; } }

        public void MakeWebViewVisible()
        {
            IsWebViewVisible = true;
            Sequence.Add("visible");
        }

        public void ShowWindow()
        {
            IsWindowVisible = true;
            Sequence.Add("show");
        }

        public void UpdateLayout()
        {
            HasCurrentLayout = IsWebViewVisible && IsWindowVisible;
            Sequence.Add("layout");
        }

        public void UpdateWindowPosition()
        {
            IsRenderable = IsInitialized && HasCurrentLayout;
            Sequence.Add("position");
        }
    }
}
