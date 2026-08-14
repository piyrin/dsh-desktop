using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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

internal sealed class RollingTextTail
{
    private readonly int capacity;
    private readonly StringBuilder text = new StringBuilder();
    private readonly object sync = new object();
    internal RollingTextTail(int capacity) { this.capacity = capacity; }
    internal void Append(string value)
    {
        if (String.IsNullOrEmpty(value)) return;
        lock (sync)
        {
            text.Append(value);
            if (text.Length > capacity) text.Remove(0, text.Length - capacity);
        }
    }
    internal string Snapshot() { lock (sync) { return text.ToString(); } }
}

internal sealed class BackendSupervisor
{
    private static readonly Uri ReadinessUri = new Uri("http://127.0.0.1:8080/");
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private readonly BackendLaunchSpec launchSpec;
    private readonly AppPaths paths;
    private readonly SemaphoreSlim stateGate = new SemaphoreSlim(1, 1);
    private Process ownedProcess;
    private JobObject ownedJob;
    private Task standardOutput;
    private Task standardError;
    private readonly RollingTextTail standardErrorTail = new RollingTextTail(2048);

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
        await stateGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            return await EnsureReadyCoreAsync(token).ConfigureAwait(false);
        }
        finally { stateGate.Release(); }
    }

    private async Task<ReadinessResult> EnsureReadyCoreAsync(CancellationToken token)
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

        if (Ownership == BackendOwnership.Owned)
            CleanupExitedOwned();

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
        await stateGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!BackendOwnershipPolicy.MayRestart(Ownership))
            {
                if (Ownership == BackendOwnership.External)
                    return new ReadinessResult(ReadinessState.NotReady, TimeSpan.Zero, "Cannot restart an external DSH backend.");
                return await EnsureReadyCoreAsync(token).ConfigureAwait(false);
            }
            await StopOwnedCoreAsync().ConfigureAwait(false);
            return await EnsureReadyCoreAsync(token).ConfigureAwait(false);
        }
        finally { stateGate.Release(); }
    }

    internal async Task StopOwnedAsync()
    {
        await stateGate.WaitAsync().ConfigureAwait(false);
        try { await StopOwnedCoreAsync().ConfigureAwait(false); }
        finally { stateGate.Release(); }
    }

    private async Task StopOwnedCoreAsync()
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

    internal static async Task<BackendProcessResult> LaunchOwnedForTestingAsync(BackendLaunchSpec spec)
    {
        if (spec == null) throw new ArgumentNullException("spec");
        using (SuspendedBackendProcess native = SuspendedBackendProcess.Start(spec))
        using (JobObject job = new JobObject())
        {
            job.AssignHandle(native.ProcessHandle);
            native.Resume();
            Task<string> output = native.StandardOutput.ReadToEndAsync();
            Task<string> error = native.StandardError.ReadToEndAsync();
            int exitCode = await Task.Factory.StartNew(delegate { return native.WaitForExit(); }).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            return new BackendProcessResult(IntPtr.Zero, exitCode, (await output.ConfigureAwait(false)).TrimEnd('\r', '\n'));
        }
    }

    private void StartOwnedProcess()
    {
        Process process = new Process();
        JobObject job = null;
        bool started = false;
        try
        {
            SuspendedBackendProcess native = SuspendedBackendProcess.Start(launchSpec);
            started = true;
            job = new JobObject();
            job.AssignHandle(native.ProcessHandle);
            process = Process.GetProcessById(native.ProcessId);
            native.Resume();
            native.ReleaseProcessHandle();
            ownedProcess = process;
            ownedJob = job;
            Ownership = BackendOwnership.Owned;
            standardOutput = CaptureOutputAsync(native.StandardOutput, false);
            standardError = CaptureOutputAsync(native.StandardError, true);
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
        return new ReadinessResult(ReadinessState.NotReady, elapsed, "DSH exited with code " + exitCode + ". stderr: " + standardErrorTail.Snapshot());
    }

    private async Task CaptureOutputAsync(StreamReader reader, bool isError)
    {
        string line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            if (isError)
            {
                standardErrorTail.Append(line + Environment.NewLine);
                AppLogger.WriteBackendStderr(paths, line + Environment.NewLine);
            }
            else
                AppLogger.WriteBackendStdout(paths, line + Environment.NewLine);
        }
    }

    private void CleanupExitedOwned()
    {
        JobObject job = ownedJob;
        Process process = ownedProcess;
        ownedJob = null;
        ownedProcess = null;
        standardOutput = null;
        standardError = null;
        Ownership = BackendOwnership.None;
        if (job != null) job.Dispose();
        if (process != null) process.Dispose();
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

}

internal sealed class SuspendedBackendProcess : IDisposable
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 1;
    private IntPtr processHandle;
    private IntPtr threadHandle;
    internal StreamReader StandardOutput { get; private set; }
    internal StreamReader StandardError { get; private set; }
    internal IntPtr ProcessHandle { get { return processHandle; } }
    internal int ProcessId { get; private set; }

    internal static SuspendedBackendProcess Start(BackendLaunchSpec spec)
    {
        SecurityAttributes attributes = new SecurityAttributes();
        attributes.Length = Marshal.SizeOf(typeof(SecurityAttributes));
        attributes.InheritHandle = true;
        IntPtr outputRead, outputWrite, errorRead, errorWrite;
        if (!CreatePipe(out outputRead, out outputWrite, ref attributes, 0) || !CreatePipe(out errorRead, out errorWrite, ref attributes, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to create backend output pipes.");
        try
        {
            SetHandleInformation(outputRead, HandleFlagInherit, 0);
            SetHandleInformation(errorRead, HandleFlagInherit, 0);
            StartupInfo startup = new StartupInfo();
            startup.cb = Marshal.SizeOf(typeof(StartupInfo));
            startup.dwFlags = StartfUseStdHandles;
            startup.hStdInput = GetStdHandle(-10);
            startup.hStdOutput = outputWrite;
            startup.hStdError = errorWrite;
            ProcessInformation info;
            IntPtr environment = BuildEnvironment(spec.Environment);
            try
            {
                StringBuilder command = new StringBuilder("\"" + spec.FileName + "\" " + spec.Arguments);
                if (!CreateProcess(spec.FileName, command, IntPtr.Zero, IntPtr.Zero, true, CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment, environment, spec.WorkingDirectory, ref startup, out info))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to create suspended DSH backend.");
            }
            finally { Marshal.FreeHGlobal(environment); }
            CloseHandle(outputWrite); outputWrite = IntPtr.Zero;
            CloseHandle(errorWrite); errorWrite = IntPtr.Zero;
            SuspendedBackendProcess result = new SuspendedBackendProcess();
            result.processHandle = info.hProcess;
            result.threadHandle = info.hThread;
            result.ProcessId = unchecked((int)info.dwProcessId);
            result.StandardOutput = new StreamReader(new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read));
            result.StandardError = new StreamReader(new FileStream(new SafeFileHandle(errorRead, true), FileAccess.Read));
            outputRead = IntPtr.Zero; errorRead = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (outputRead != IntPtr.Zero) CloseHandle(outputRead);
            if (outputWrite != IntPtr.Zero) CloseHandle(outputWrite);
            if (errorRead != IntPtr.Zero) CloseHandle(errorRead);
            if (errorWrite != IntPtr.Zero) CloseHandle(errorWrite);
        }
    }

    internal void Resume()
    {
        if (ResumeThread(threadHandle) == UInt32.MaxValue)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to resume DSH backend.");
        CloseHandle(threadHandle); threadHandle = IntPtr.Zero;
    }

    internal void ReleaseProcessHandle()
    {
        if (processHandle != IntPtr.Zero) CloseHandle(processHandle);
        processHandle = IntPtr.Zero;
    }

    internal int WaitForExit()
    {
        WaitForSingleObject(processHandle, 0xFFFFFFFF);
        uint code;
        if (!GetExitCodeProcess(processHandle, out code)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return unchecked((int)code);
    }

    public void Dispose()
    {
        if (threadHandle != IntPtr.Zero) CloseHandle(threadHandle);
        if (processHandle != IntPtr.Zero) CloseHandle(processHandle);
        threadHandle = IntPtr.Zero; processHandle = IntPtr.Zero;
        if (StandardOutput != null) StandardOutput.Dispose();
        if (StandardError != null) StandardError.Dispose();
    }

    private static IntPtr BuildEnvironment(IDictionary<string, string> additions)
    {
        SortedDictionary<string, string> values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables()) values[(string)item.Key] = (string)item.Value;
        foreach (KeyValuePair<string, string> item in additions) values[item.Key] = item.Value;
        StringBuilder block = new StringBuilder();
        foreach (KeyValuePair<string, string> item in values) block.Append(item.Key).Append('=').Append(item.Value).Append('\0');
        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool InheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { internal int cb; internal string reserved; internal string desktop; internal string title; internal int x; internal int y; internal int xSize; internal int ySize; internal int xCountChars; internal int yCountChars; internal int fillAttribute; internal uint dwFlags; internal short showWindow; internal short reserved2; internal IntPtr reserved2Ptr; internal IntPtr hStdInput; internal IntPtr hStdOutput; internal IntPtr hStdError; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal IntPtr hProcess; internal IntPtr hThread; internal uint dwProcessId; internal uint dwThreadId; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string app, StringBuilder command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int standardHandle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
