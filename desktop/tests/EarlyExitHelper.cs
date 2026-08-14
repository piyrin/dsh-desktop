using System;
using System.IO;
using System.Threading;
internal static class EarlyExitHelper
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "stderr") Console.Error.WriteLine("first-generation-error");
        if (args.Length > 2 && args[0] == "wait")
        {
            File.WriteAllText(args[1], "ready");
            while (!File.Exists(args[2])) Thread.Sleep(10);
        }
        return 23;
    }
}
