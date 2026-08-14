using System;
using System.Collections.Generic;

internal sealed class BackendLaunchSpec
{
    internal string FileName { get; private set; }
    internal string Arguments { get; private set; }
    internal string WorkingDirectory { get; private set; }
    internal IDictionary<string, string> Environment { get; private set; }

    private BackendLaunchSpec() { }

    internal static BackendLaunchSpec Create(DshInstallation installation, AppPaths paths, int port)
    {
        if (installation == null) throw new ArgumentNullException("installation");
        if (paths == null) throw new ArgumentNullException("paths");

        return new BackendLaunchSpec
        {
            FileName = installation.NodeExe,
            Arguments = "\"" + paths.BootstrapScript.Replace("\"", "\\\"") + "\"",
            WorkingDirectory = installation.Directory,
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DSH_DESKTOP_ENTRY", installation.JsEntry },
                { "DSH_DESKTOP_PORT", port.ToString() },
                { "NODE_COMPILE_CACHE", paths.NodeCompileCache }
            }
        };
    }
}
