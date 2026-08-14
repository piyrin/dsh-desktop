using System;
internal static class EarlyExitHelper
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "stderr") Console.Error.WriteLine("first-generation-error");
        return 23;
    }
}
