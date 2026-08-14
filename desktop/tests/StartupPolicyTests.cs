using System;

internal static class StartupPolicyTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("cold launch stays unanimated at three seconds", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.ColdLaunch, TimeSpan.FromSeconds(3), false));
        });
        runner.Add("cold launch animates after three seconds", delegate {
            AssertEx.True(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.ColdLaunch, TimeSpan.FromMilliseconds(3001), false));
        });
        runner.Add("ready backend never animates", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.ColdLaunch, TimeSpan.FromSeconds(8), true));
        });
        runner.Add("tray restore never animates", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.TrayRestore, TimeSpan.FromSeconds(20), false));
        });
        runner.Add("secondary activation never animates", delegate {
            AssertEx.False(StartupPolicy.ShouldShowAnimatedSplash(
                OpenReason.SecondaryActivation, TimeSpan.FromSeconds(20), false));
        });
    }
}
