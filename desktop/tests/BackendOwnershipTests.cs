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
}
