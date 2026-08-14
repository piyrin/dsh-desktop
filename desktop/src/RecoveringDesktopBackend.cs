using System;
using System.Threading;
using System.Threading.Tasks;

internal interface IDesktopBackendFactory
{
    IDesktopBackend Create();
}

internal sealed class DelegateDesktopBackendFactory : IDesktopBackendFactory
{
    private readonly Func<IDesktopBackend> create;

    internal DelegateDesktopBackendFactory(Func<IDesktopBackend> create)
    {
        if (create == null) throw new ArgumentNullException("create");
        this.create = create;
    }

    public IDesktopBackend Create()
    {
        return create();
    }
}

internal sealed class RecoveringDesktopBackend : IDesktopBackend
{
    private sealed class DiscoveryResult
    {
        internal IDesktopBackend Backend;
        internal Exception Failure;
    }

    private readonly IDesktopBackendFactory factory;
    private readonly Action<string> log;
    private readonly SemaphoreSlim discoveryGate = new SemaphoreSlim(1, 1);
    private readonly object currentSync = new object();
    private IDesktopBackend current;

    internal RecoveringDesktopBackend(IDesktopBackendFactory factory, Action<string> log)
    {
        if (factory == null) throw new ArgumentNullException("factory");
        if (log == null) throw new ArgumentNullException("log");
        this.factory = factory;
        this.log = log;
    }

    public BackendOwnership Ownership
    {
        get
        {
            IDesktopBackend backend = GetCurrent();
            return backend == null ? BackendOwnership.None : backend.Ownership;
        }
    }

    public async Task<ReadinessResult> EnsureReadyAsync(CancellationToken token)
    {
        DiscoveryResult discovery = await DiscoverAsync(token).ConfigureAwait(false);
        if (discovery.Backend == null) return CreateFailure(discovery.Failure);
        return await discovery.Backend.EnsureReadyAsync(token).ConfigureAwait(false);
    }

    public async Task<ReadinessResult> RestartAsync(CancellationToken token)
    {
        DiscoveryResult discovery = await DiscoverAsync(token).ConfigureAwait(false);
        if (discovery.Backend == null) return CreateFailure(discovery.Failure);
        return await discovery.Backend.RestartAsync(token).ConfigureAwait(false);
    }

    public async Task StopOwnedAsync()
    {
        IDesktopBackend backend;
        await discoveryGate.WaitAsync().ConfigureAwait(false);
        try { backend = GetCurrent(); }
        finally { discoveryGate.Release(); }
        if (backend != null) await backend.StopOwnedAsync().ConfigureAwait(false);
    }

    private async Task<DiscoveryResult> DiscoverAsync(CancellationToken token)
    {
        IDesktopBackend backend = GetCurrent();
        if (backend != null) return new DiscoveryResult { Backend = backend };

        await discoveryGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            backend = GetCurrent();
            if (backend != null) return new DiscoveryResult { Backend = backend };

            log("Rediscovering the Node and DSH installation.");
            try
            {
                backend = factory.Create();
                if (backend == null)
                    throw new InvalidOperationException("The desktop backend factory returned no backend.");
                lock (currentSync) { current = backend; }
                log("DSH installation rediscovery succeeded.");
                return new DiscoveryResult { Backend = backend };
            }
            catch (Exception exception)
            {
                log("DSH installation rediscovery failed: " + exception);
                return new DiscoveryResult { Failure = exception };
            }
        }
        finally
        {
            discoveryGate.Release();
        }
    }

    private IDesktopBackend GetCurrent()
    {
        lock (currentSync) { return current; }
    }

    private static ReadinessResult CreateFailure(Exception exception)
    {
        string message = exception == null ? "Unknown installation discovery error." : exception.Message;
        return new ReadinessResult(
            ReadinessState.NotReady,
            TimeSpan.Zero,
            "Required Node or DSH files could not be found. Verify that node.exe and dsh.cmd are on PATH, then Retry. " + message);
    }
}
