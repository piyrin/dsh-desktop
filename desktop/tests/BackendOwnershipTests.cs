using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class BackendOwnershipTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("external backend cannot be stopped", delegate {
            AssertEx.False(BackendOwnershipPolicy.MayStop(BackendOwnership.External));
        });
        runner.Add("external backend cannot be restarted", delegate {
            AssertEx.False(BackendOwnershipPolicy.MayRestart(BackendOwnership.External));
        });
        runner.Add("owned backend can be stopped and restarted", delegate {
            AssertEx.True(BackendOwnershipPolicy.MayStop(BackendOwnership.Owned));
            AssertEx.True(BackendOwnershipPolicy.MayRestart(BackendOwnership.Owned));
        });
        runner.Add("test launch has no window and captures helper output", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            BackendProcessResult result = BackendSupervisor.LaunchForTestingAsync(CreateLaunchSpec(helper)).GetAwaiter().GetResult();
            AssertEx.Equal(IntPtr.Zero, result.MainWindowHandle);
            AssertEx.Equal(0, result.ExitCode);
            AssertEx.Equal("hidden-helper-ready", result.StandardOutput);
        });
        runner.Add("suspended owned launch captures helper output", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            BackendProcessResult result = BackendSupervisor.LaunchOwnedForTestingAsync(CreateLaunchSpec(helper)).GetAwaiter().GetResult();
            AssertEx.Equal(0, result.ExitCode);
            AssertEx.Equal("hidden-helper-ready", result.StandardOutput);
        });
        runner.Add("rolling stderr tail is bounded", delegate {
            RollingTextTail tail = new RollingTextTail(4);
            tail.Append("abc");
            tail.Append("def");
            AssertEx.Equal("cdef", tail.Snapshot());
        });
        runner.Add("cancelled ensure never launches backend", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            AppPaths paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "dsh-task4-cancel"), Path.GetDirectoryName(helper));
            BackendSupervisor supervisor = new BackendSupervisor(CreateLaunchSpec(helper), paths);
            bool cancelled = false;
            try { supervisor.EnsureReadyAsync(new System.Threading.CancellationToken(true)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            AssertEx.True(cancelled);
            AssertEx.Equal(BackendOwnership.None, supervisor.Ownership);
        });
        runner.Add("cancellation during probe never launches backend", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            AppPaths paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "dsh-task4-cancel-mid"), Path.GetDirectoryName(helper));
            BackendSupervisor supervisor = new BackendSupervisor(CreateLaunchSpec(helper), paths);
            System.Threading.CancellationTokenSource cancellation = new System.Threading.CancellationTokenSource();
            cancellation.CancelAfter(20);
            bool cancelled = false;
            try { supervisor.EnsureReadyAsync(cancellation.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            AssertEx.True(cancelled);
            AssertEx.Equal(BackendOwnership.None, supervisor.Ownership);
        });
        runner.Add("failed suspended setup terminates only the new child", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            AssertEx.True(BackendSupervisor.FailedSetupCleansUpForTestingAsync(CreateLaunchSpec(helper)).GetAwaiter().GetResult());
        });
        runner.Add("new backend generation clears prior stderr tail", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "EarlyExitHelper.exe");
            AppPaths paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "dsh-task4-tail"), Path.GetDirectoryName(helper));
            BackendLaunchSpec spec = CreateLaunchSpec(helper);
            Set(spec, "<Arguments>k__BackingField", "stderr");
            BackendSupervisor supervisor = new BackendSupervisor(spec, paths);
            ReadinessResult first = supervisor.EnsureReadyAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.True(first.Detail.IndexOf("first-generation-error", StringComparison.Ordinal) >= 0);
            Set(spec, "<Arguments>k__BackingField", String.Empty);
            ReadinessResult second = supervisor.EnsureReadyAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.True(second.Detail.IndexOf("first-generation-error", StringComparison.Ordinal) < 0);
        });
        runner.Add("post-create cancellation terminates suspended child", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            AssertEx.True(BackendSupervisor.PostCreateCancellationCleansUpForTestingAsync(CreateLaunchSpec(helper)).GetAwaiter().GetResult());
        });
        runner.Add("job setup failure terminates suspended child", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            AssertEx.True(BackendSupervisor.JobSetupFailureCleansUpForTestingAsync(CreateLaunchSpec(helper)).GetAwaiter().GetResult());
        });
        runner.Add("start failure preserves cancellation and releases exact child resources", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "NoWindowHelper.exe");
            AppPaths paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "dsh-task4-start-cleanup"), Path.GetDirectoryName(helper));
            BackendSupervisor supervisor = new BackendSupervisor(CreateLaunchSpec(helper), paths);
            System.Threading.CancellationTokenSource cancellation = new System.Threading.CancellationTokenSource();
            SuspendedBackendProcess native = null;
            StreamReader output = null;
            StreamReader error = null;
            int processId = 0;
            OperationCanceledException observed = null;

            try
            {
                supervisor.StartOwnedProcessForTesting(cancellation.Token, delegate(SuspendedBackendProcess created) {
                    native = created;
                    output = created.StandardOutput;
                    error = created.StandardError;
                    processId = created.ProcessId;
                    MethodInfo closeHandle = typeof(SuspendedBackendProcess).GetMethod("CloseHandle", BindingFlags.Static | BindingFlags.NonPublic);
                    AssertEx.True((bool)closeHandle.Invoke(null, new object[] { created.ProcessHandle }));
                    cancellation.Cancel();
                });
            }
            catch (OperationCanceledException exception) { observed = exception; }

            AssertEx.True(observed != null);
            AssertEx.Equal(cancellation.Token, observed.CancellationToken);
            AssertEx.True(WaitForProcessExit(processId));
            AssertEx.Equal(IntPtr.Zero, (IntPtr)Get(native, "processHandle"));
            AssertEx.Equal(IntPtr.Zero, (IntPtr)Get(native, "threadHandle"));
            AssertEx.Equal<StreamReader>(null, native.StandardOutput);
            AssertEx.Equal<StreamReader>(null, native.StandardError);
            AssertEx.True(IsDisposed(output));
            AssertEx.True(IsDisposed(error));
            AssertEx.Equal(BackendOwnership.None, supervisor.Ownership);
        });
        runner.Add("late stderr from prior generation stays out of current detail", delegate {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-output", "EarlyExitHelper.exe");
            string testDirectory = Path.Combine(Path.GetTempPath(), "dsh-task4-late-tail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            string secondReady = Path.Combine(testDirectory, "second-ready");
            string secondRelease = Path.Combine(testDirectory, "second-release");
            AppPaths paths = AppPaths.Create(testDirectory, Path.GetDirectoryName(helper));
            BackendLaunchSpec spec = CreateLaunchSpec(helper);
            Set(spec, "<Arguments>k__BackingField", "stderr");
            System.Threading.Tasks.TaskCompletionSource<bool> firstAppendReached = new System.Threading.Tasks.TaskCompletionSource<bool>();
            System.Threading.Tasks.TaskCompletionSource<bool> releaseFirstAppend = new System.Threading.Tasks.TaskCompletionSource<bool>();
            BackendSupervisor supervisor = new BackendSupervisor(spec, paths, delegate(string line) {
                if (line.IndexOf("first-generation-error", StringComparison.Ordinal) >= 0)
                {
                    firstAppendReached.TrySetResult(true);
                    return releaseFirstAppend.Task;
                }
                return System.Threading.Tasks.Task.FromResult(true);
            });

            try
            {
                System.Threading.Tasks.Task<ReadinessResult> firstTask = supervisor.EnsureReadyAsync(System.Threading.CancellationToken.None);
                AssertEx.True(firstAppendReached.Task.Wait(5000));
                firstTask.GetAwaiter().GetResult();

                Set(spec, "<Arguments>k__BackingField", "wait \"" + secondReady + "\" \"" + secondRelease + "\"");
                System.Threading.Tasks.Task<ReadinessResult> secondTask = supervisor.EnsureReadyAsync(System.Threading.CancellationToken.None);
                AssertEx.True(WaitForFile(secondReady));
                releaseFirstAppend.TrySetResult(true);
                File.WriteAllText(secondRelease, "release");
                ReadinessResult second = secondTask.GetAwaiter().GetResult();
                AssertEx.True(second.Detail.IndexOf("first-generation-error", StringComparison.Ordinal) < 0);
            }
            finally
            {
                releaseFirstAppend.TrySetResult(true);
                File.WriteAllText(secondRelease, "release");
                try { supervisor.StopOwnedAsync().GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
        });
    }

    private static BackendLaunchSpec CreateLaunchSpec(string helper)
    {
        BackendLaunchSpec spec = (BackendLaunchSpec)FormatterServices.GetUninitializedObject(typeof(BackendLaunchSpec));
        Set(spec, "<FileName>k__BackingField", helper);
        Set(spec, "<Arguments>k__BackingField", String.Empty);
        Set(spec, "<WorkingDirectory>k__BackingField", Path.GetDirectoryName(helper));
        Set(spec, "<Environment>k__BackingField", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        return spec;
    }

    private static void Set(BackendLaunchSpec spec, string name, object value)
    {
        FieldInfo field = typeof(BackendLaunchSpec).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(spec, value);
    }

    private static object Get(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return field.GetValue(instance);
    }

    private static bool IsDisposed(StreamReader reader)
    {
        try { reader.Peek(); return false; }
        catch (ObjectDisposedException) { return true; }
    }

    private static bool WaitForProcessExit(int processId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using (System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId))
                {
                    if (process.HasExited) return true;
                }
            }
            catch (ArgumentException) { return true; }
            System.Threading.Thread.Sleep(20);
        }
        return false;
    }

    private static bool WaitForFile(string path)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            System.Threading.Thread.Sleep(20);
        }
        return false;
    }
}
