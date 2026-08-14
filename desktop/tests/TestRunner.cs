using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

internal sealed class TestRunner
{
    private readonly List<KeyValuePair<string, Action>> tests = new List<KeyValuePair<string, Action>>();

    internal void Add(string name, Action test)
    {
        tests.Add(new KeyValuePair<string, Action>(name, test));
    }

    internal int Run(string filter)
    {
        int failures = 0;
        foreach (KeyValuePair<string, Action> test in tests)
        {
            if (!String.IsNullOrEmpty(filter) && test.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            try
            {
                test.Value();
                Console.WriteLine("PASS " + test.Key);
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine("FAIL " + test.Key + ": " + exception.Message);
            }
        }
        Console.WriteLine("Tests: " + (tests.Count - failures) + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    internal static int Main(string[] args)
    {
        if (args.Length == 2 && String.Equals(args[0], "--activation-primary", StringComparison.OrdinalIgnoreCase))
            return RunActivationPrimary(args[1]);
        if (args.Length == 1 && String.Equals(args[0], "--activation-secondary", StringComparison.OrdinalIgnoreCase))
            return RunActivationSecondary();

        string filter = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (String.Equals(args[index], "--filter", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                filter = args[++index];
        }

        TestRunner runner = new TestRunner();
        StartupPolicyTests.Register(runner);
        DesktopUiPolicyTests.Register(runner);
        AppAssetsTests.Register(runner);
        SplashOverlayTests.Register(runner);
        InvisibleWindowPreloaderTests.Register(runner);
        DshInstallationTests.Register(runner);
        DshReadinessTests.Register(runner);
        BackendOwnershipTests.Register(runner);
        ActivationProtocolTests.Register(runner);
        DesktopApplicationTests.Register(runner);
        TrayControllerTests.Register(runner);
        return runner.Run(filter);
    }

    private static int RunActivationPrimary(string activationPath)
    {
        using (SingleInstanceCoordinator coordinator = new SingleInstanceCoordinator())
        using (ManualResetEvent activated = new ManualResetEvent(false))
        {
            coordinator.ActivateRequested += delegate {
                File.WriteAllText(activationPath, "activated");
                activated.Set();
            };
            if (!coordinator.TryBecomePrimary()) return 2;
            Console.WriteLine("READY");
            return activated.WaitOne(5000) ? 0 : 3;
        }
    }

    private static int RunActivationSecondary()
    {
        using (SingleInstanceCoordinator coordinator = new SingleInstanceCoordinator())
        {
            if (coordinator.TryBecomePrimary()) return 2;
            return coordinator.SignalPrimary(TimeSpan.FromMilliseconds(1500)) ? 0 : 3;
        }
    }
}

internal static class AssertEx
{
    internal static void True(bool condition)
    {
        if (!condition) throw new Exception("Expected true.");
    }

    internal static void False(bool condition)
    {
        if (condition) throw new Exception("Expected false.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!Object.Equals(expected, actual))
            throw new Exception("Expected " + expected + ", got " + actual + ".");
    }
}
