using System.IO;

internal sealed class AppPaths
{
    internal string Root { get; private set; }
    internal string Logs { get; private set; }
    internal string NodeCompileCache { get; private set; }
    internal string WebView2Data { get; private set; }
    internal string DesktopLog { get; private set; }
    internal string DshStdoutLog { get; private set; }
    internal string DshStderrLog { get; private set; }
    internal string BootstrapScript { get; private set; }

    private AppPaths() { }

    internal static AppPaths Create(string localAppData, string applicationDirectory)
    {
        string root = Path.Combine(localAppData, "DeepSeekHarness");
        string logs = Path.Combine(root, "logs");
        return new AppPaths
        {
            Root = root,
            Logs = logs,
            NodeCompileCache = Path.Combine(root, "cache", "node-compile"),
            WebView2Data = Path.Combine(root, "webview2"),
            DesktopLog = Path.Combine(logs, "desktop.log"),
            DshStdoutLog = Path.Combine(logs, "dsh-stdout.log"),
            DshStderrLog = Path.Combine(logs, "dsh-stderr.log"),
            BootstrapScript = Path.Combine(applicationDirectory, "runtime", "dsh-desktop-bootstrap.mjs")
        };
    }
}
