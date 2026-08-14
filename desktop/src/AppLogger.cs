using System;
using System.IO;
using System.Text;

internal static class AppLogger
{
    private const long MaximumLogBytes = 5L * 1024L * 1024L;
    private static readonly object SyncRoot = new object();

    internal static void WriteDesktop(AppPaths paths, string message)
    {
        if (paths == null) throw new ArgumentNullException("paths");
        Append(paths.DesktopLog, DateTime.Now.ToString("o") + " " + (message ?? String.Empty) + Environment.NewLine);
    }

    internal static void WriteBackendStdout(AppPaths paths, string output)
    {
        if (paths == null) throw new ArgumentNullException("paths");
        Append(paths.DshStdoutLog, output ?? String.Empty);
    }

    internal static void WriteBackendStderr(AppPaths paths, string output)
    {
        if (paths == null) throw new ArgumentNullException("paths");
        Append(paths.DshStderrLog, output ?? String.Empty);
    }

    private static void Append(string path, string content)
    {
        if (String.IsNullOrEmpty(path)) throw new ArgumentException("Log path is required.", "path");
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        lock (SyncRoot)
        {
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaximumLogBytes)
            {
                string prior = path + ".1";
                if (File.Exists(prior)) File.Delete(prior);
                File.Move(path, prior);
            }
            using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
