using System;

internal enum OpenReason
{
    ColdLaunch,
    ServiceRestart,
    TrayRestore,
    SecondaryActivation
}

internal static class StartupPolicy
{
    internal static readonly TimeSpan AnimationThreshold = TimeSpan.FromSeconds(3);

    internal static bool ShouldShowAnimatedSplash(
        OpenReason reason, TimeSpan elapsed, bool backendReady)
    {
        if (backendReady) return false;
        if (reason == OpenReason.TrayRestore || reason == OpenReason.SecondaryActivation) return false;
        return elapsed > AnimationThreshold;
    }
}
