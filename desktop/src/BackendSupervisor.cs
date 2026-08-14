using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal enum BackendOwnership
{
    None,
    Owned,
    External
}

internal static class BackendOwnershipPolicy
{
    internal static bool MayStop(BackendOwnership ownership)
    {
        return ownership == BackendOwnership.Owned;
    }

    internal static bool MayRestart(BackendOwnership ownership)
    {
        return ownership == BackendOwnership.Owned;
    }
}

internal sealed class BackendProcessResult
{
    internal IntPtr MainWindowHandle { get; private set; }
    internal int ExitCode { get; private set; }
    internal string StandardOutput { get; private set; }

    internal BackendProcessResult(IntPtr mainWindowHandle, int exitCode, string standardOutput)
    {
        MainWindowHandle = mainWindowHandle;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
    }
}

internal sealed class BackendSupervisor
{
    private static readonly Uri ReadinessUri = new Uri("http://127.0.0.1:8080/");
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private readonly BackendLaunchSpec launchSpec;
    private readonly AppPaths paths;
    private Process ownedProcess;
    private JobObject ownedJob;
    private Task<string> standardOutput;
    private Task<string> standardError;

    internal BackendSupervisor(BackendLaunchSpec launchSpec, AppPaths paths)
    {
        if (launchSpec == null) throw new ArgumentNullException("launchSpec");
        if (paths == null) throw new ArgumentNullException("paths");
        this.launchSpec = launchSpec;
        this.paths = paths;
        Ownership = BackendOwnership.None;
    }

    internal BackendOwnership Ownership { get; private set; }

    internal async Task<ReadinessResult> EnsureReadyAsync(CancellationToken token)
    {
        ReadinessResult probe = await DshReadiness.WaitAsync(ReadinessUri, ProbeTimeout, token).ConfigureAwait(false);
        if (probe.State == ReadinessState.Ready)
        {
            if (Ownership != BackendOwnership.Owned)
                Ownership = BackendOwnership.External;
            AppLogger.WriteDesktop(paths, "Attached to ready external DSH backend.");
            return probe;
        }
        if (probe.State == ReadinessState.PortConflict)
            return probe;

        if (Ownership == BackendOwnership.Owned && ownedProcess != null && !ownedProcess.HasExited)
            return await WaitForOwnedReadinessAsync(token).ConfigureAwait(false);

        if (Ownership == BackendOwnership.External)
            return new ReadinessResult(ReadinessState.NotReady, TimeSpan.Zero, "External DSH backend is no longer ready; it will not be stopped or restarted.");

        try
        {
            StartOwnedProcess();
        }
        catch (Exception exception)
        {
            return new ReadinessResult(ReadinessState.NotReady, TimeSpan.Zero, "Unable to start the DSH backend: " + exception.Message);
        }
        return await WaitForOwnedReadinessAsync(token).ConfigureAwait(false);
    }

    internal async Task<ReadinessResult> RestartAsync(CancellationToken token)
    {
        if (!BackendOwnershipPolicy.MayRestart(Ownership))
        {
            if (Ownership == BackendOwnership.External)
                return new ReadinessResult(ReadinessState.NotReady, TimeSpan.Zero, "Cannot restart an external DSH backend.");
            return await EnsureReadyAsync(token).ConfigureAwait(false);
        }
        await StopOwnedAsync().ConfigureAwait(false);
        return await EnsureReadyAsync(token).ConfigureAwait(false);
    }

    internal async Task StopOwnedAsync()
    {
        if (!BackendOwnershipPolicy.MayStop(Ownership))
            return;

        Process process = ownedProcess;
        JobObject job = ownedJob;
        try
        {
            if (job != null) job.Dispose();
            if (process != null)
                await Task.Factory.StartNew(delegate { process.WaitForExit(5000); }).ConfigureAwait(false);
        }
        finally
        {
            ownedJob = null;
            ownedProcess = null;
            standardOutput = null;
            standardError = null;
            Ownership = BackendOwnership.None;
            if (process != null) process.Dispose();
        }
    }

    internal static async Task<BackendProcessResult> LaunchForTestingAsync(BackendLaunchSpec spec)
    {
        if (spec == null) throw new ArgumentNullException("spec");
        using (Process process = new Process())
        {
            process.StartInfo = CreateStartInfo(spec);
            process.Start();
            IntPtr mainWindowHandle = process.MainWindowHandle;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await Task.Factory.StartNew(delegate { process.WaitForExit(); }).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            string captured = await output.ConfigureAwait(false);
            return new BackendProcessResult(mainWindowHandle, process.ExitCode, captured.TrimEnd('\r', '\n'));
        }
    }

    private void StartOwnedProcess()
    {
        Process process = new Process();
        JobObject job = null;
        bool started = false;
        try
        {
            process.StartInfo = CreateStartInfo(launchSpec);
            process.Start();
            started = true;
            job = new JobObject();
            job.Assign(process);
            ownedProcess = process;
            ownedJob = job;
            Ownership = BackendOwnership.Owned;
            standardOutput = CaptureOutputAsync(process.StandardOutput, false);
            standardError = CaptureOutputAsync(process.StandardError, true);
            AppLogger.WriteDesktop(paths, "Started owned DSH backend process " + process.Id + ".");
        }
        catch
        {
            if (job != null)
                job.Dispose();
            else if (started && !process.HasExited)
                process.Kill();
            if (ownedProcess == process)
            {
                ownedProcess = null;
                ownedJob = null;
                standardOutput = null;
                standardError = null;
                Ownership = BackendOwnership.None;
            }
            process.Dispose();
            throw;
        }
    }

    private async Task<ReadinessResult> WaitForOwnedReadinessAsync(CancellationToken token)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < StartupTimeout && !token.IsCancellationRequested)
        {
            if (ownedProcess == null || ownedProcess.HasExited)
                return CreateEarlyExitResult(stopwatch.Elapsed);

            ReadinessResult result = await DshReadiness.WaitAsync(ReadinessUri, TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
            if (result.State != ReadinessState.NotReady)
                return result;
        }
        return new ReadinessResult(ReadinessState.NotReady, stopwatch.Elapsed, "DSH did not become ready within 30 seconds. The owned backend is still running; use Retry or Restart.");
    }

    private ReadinessResult CreateEarlyExitResult(TimeSpan elapsed)
    {
        int exitCode = ownedProcess == null ? -1 : ownedProcess.ExitCode;
        string error = standardError == null ? String.Empty : SafeTaskResult(standardError);
        return new ReadinessResult(ReadinessState.NotReady, elapsed, "DSH exited with code " + exitCode + ". stderr: " + Tail(error, 2048));
    }

    private async Task<string> CaptureOutputAsync(StreamReader reader, bool isError)
    {
        StringBuilder captured = new StringBuilder();
        string line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            captured.AppendLine(line);
            if (isError)
                AppLogger.WriteBackendStderr(paths, line + Environment.NewLine);
            else
                AppLogger.WriteBackendStdout(paths, line + Environment.NewLine);
        }
        return captured.ToString();
    }

    private static ProcessStartInfo CreateStartInfo(BackendLaunchSpec spec)
    {
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = spec.FileName;
        info.Arguments = spec.Arguments;
        info.WorkingDirectory = spec.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        foreach (KeyValuePair<string, string> entry in spec.Environment)
            info.EnvironmentVariables[entry.Key] = entry.Value;
        return info;
    }

    private static string SafeTaskResult(Task<string> task)
    {
        try { return task.Result; }
        catch { return String.Empty; }
    }

    private static string Tail(string value, int length)
    {
        if (String.IsNullOrEmpty(value) || value.Length <= length) return value ?? String.Empty;
        return value.Substring(value.Length - length);
    }
}
