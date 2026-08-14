using System;
using System.IO;

internal static class DshInstallationTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("DshInstallation discovers node and entry from PATH", delegate
        {
            string root = MakeFakeInstallation();
            try
            {
                string nodeDirectory = Path.Combine(root, "node");
                string wrapperDirectory = Path.Combine(root, "bin");
                DshInstallation installation = DshInstallation.Discover(
                    nodeDirectory + ";" + wrapperDirectory);

                AssertEx.Equal(Path.Combine(nodeDirectory, "node.exe"), installation.NodeExe);
                AssertEx.Equal(Path.Combine(wrapperDirectory, "dsh.cmd"), installation.CommandWrapper);
                AssertEx.Equal(Path.Combine(wrapperDirectory, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"), installation.JsEntry);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

        runner.Add("launch spec bypasses command wrappers", delegate
        {
            string root = MakeFakeInstallation();
            try
            {
                string nodeDirectory = Path.Combine(root, "node");
                string wrapperDirectory = Path.Combine(root, "bin");
                DshInstallation installation = DshInstallation.Discover(nodeDirectory + ";" + wrapperDirectory);
                AppPaths paths = AppPaths.Create(Path.Combine(root, "local"), Path.Combine(root, "app"));
                BackendLaunchSpec spec = BackendLaunchSpec.Create(installation, paths, 8080);

                AssertEx.Equal(installation.NodeExe, spec.FileName);
                AssertEx.Equal("\"" + paths.BootstrapScript + "\"", spec.Arguments);
                AssertEx.False(spec.Arguments.Contains("dsh.cmd"));
                AssertEx.Equal(installation.Directory, spec.WorkingDirectory);
                AssertEx.Equal(installation.JsEntry, spec.Environment["DSH_DESKTOP_ENTRY"]);
                AssertEx.Equal("8080", spec.Environment["DSH_DESKTOP_PORT"]);
                AssertEx.Equal(paths.NodeCompileCache, spec.Environment["NODE_COMPILE_CACHE"]);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

        runner.Add("launch spec uses packaged bootstrap beside executable", delegate
        {
            string root = MakeFakeInstallation();
            try
            {
                string nodeDirectory = Path.Combine(root, "node");
                string wrapperDirectory = Path.Combine(root, "bin");
                string applicationDirectory = Path.Combine(root, "publish");
                Directory.CreateDirectory(applicationDirectory);
                string packagedBootstrap = Path.Combine(applicationDirectory, "dsh-desktop-bootstrap.mjs");
                File.WriteAllText(packagedBootstrap, "// packaged bootstrap");

                DshInstallation installation = DshInstallation.Discover(nodeDirectory + ";" + wrapperDirectory);
                AppPaths paths = AppPaths.Create(Path.Combine(root, "local"), applicationDirectory);
                BackendLaunchSpec spec = BackendLaunchSpec.Create(installation, paths, 8080);

                AssertEx.True(File.Exists(paths.BootstrapScript));
                AssertEx.Equal("\"" + packagedBootstrap + "\"", spec.Arguments);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
        runner.Add("DshInstallation reports a missing JavaScript entry without touching the real install", delegate
        {
            string root = Path.Combine(Path.GetTempPath(), "dsh-task8-missing-" + Guid.NewGuid().ToString("N"));
            string fakeBin = Path.Combine(root, "bin");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(Path.Combine(fakeBin, "node.exe"), "fake node");
            File.WriteAllText(Path.Combine(fakeBin, "dsh.cmd"), "fake wrapper");
            try
            {
                bool missingEntryReported = false;
                try { DshInstallation.Discover(fakeBin); }
                catch (FileNotFoundException exception)
                {
                    missingEntryReported = exception.Message.IndexOf("lib\\bin.js", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                AssertEx.True(missingEntryReported);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private static string MakeFakeInstallation()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-task2-" + Guid.NewGuid().ToString("N"));
        string nodeDirectory = Path.Combine(root, "node");
        string wrapperDirectory = Path.Combine(root, "bin");
        string entryDirectory = Path.Combine(wrapperDirectory, "node_modules", "@deepseek-ai", "dsh", "lib");
        Directory.CreateDirectory(nodeDirectory);
        Directory.CreateDirectory(entryDirectory);
        File.WriteAllText(Path.Combine(nodeDirectory, "node.exe"), "fake node");
        File.WriteAllText(Path.Combine(wrapperDirectory, "dsh.cmd"), "fake wrapper");
        File.WriteAllText(Path.Combine(entryDirectory, "bin.js"), "fake entry");
        return root;
    }
}
