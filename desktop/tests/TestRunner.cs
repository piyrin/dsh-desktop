using System;
using System.Collections.Generic;

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
        string filter = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (String.Equals(args[index], "--filter", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                filter = args[++index];
        }

        TestRunner runner = new TestRunner();
        StartupPolicyTests.Register(runner);
        DshInstallationTests.Register(runner);
        DshReadinessTests.Register(runner);
        return runner.Run(filter);
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
