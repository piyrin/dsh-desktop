using System;
using System.Threading;

internal static class ActivationProtocolTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("activation protocol serializes exact command", delegate {
            AssertEx.Equal("ACTIVATE\n", ActivationProtocol.SerializeActivate());
        });
        runner.Add("activation protocol accepts exact command", delegate {
            AssertEx.True(ActivationProtocol.IsActivate("ACTIVATE\n"));
        });
        runner.Add("activation protocol rejects arbitrary input", delegate {
            AssertEx.False(ActivationProtocol.IsActivate("RESTART\n"));
        });
        runner.Add("only one coordinator becomes primary", delegate {
            using (SingleInstanceCoordinator first = new SingleInstanceCoordinator())
            using (SingleInstanceCoordinator second = new SingleInstanceCoordinator())
            {
                AssertEx.True(first.TryBecomePrimary());
                AssertEx.False(TryBecomePrimaryOnWorker(second));
            }
        });
        runner.Add("secondary signals one activation to primary", delegate {
            using (SingleInstanceCoordinator primary = new SingleInstanceCoordinator())
            using (SingleInstanceCoordinator secondary = new SingleInstanceCoordinator())
            using (ManualResetEvent activated = new ManualResetEvent(false))
            {
                int activations = 0;
                primary.ActivateRequested += delegate {
                    Interlocked.Increment(ref activations);
                    activated.Set();
                };
                if (!primary.TryBecomePrimary()) throw new Exception("Primary did not acquire the mutex.");
                if (TryBecomePrimaryOnWorker(secondary)) throw new Exception("Secondary acquired the mutex.");
                if (!secondary.SignalPrimary(TimeSpan.FromMilliseconds(1500))) throw new Exception("Secondary did not connect to the primary pipe.");
                if (!activated.WaitOne(2000)) throw new Exception("Primary did not raise an activation event.");
                AssertEx.Equal(1, activations);
            }
        });
        runner.Add("disposing primary promptly releases listener and mutex", delegate {
            SingleInstanceCoordinator primary = new SingleInstanceCoordinator();
            AssertEx.True(primary.TryBecomePrimary());
            DateTime beforeDispose = DateTime.UtcNow;
            primary.Dispose();
            AssertEx.True(DateTime.UtcNow - beforeDispose < TimeSpan.FromSeconds(1));
            using (SingleInstanceCoordinator next = new SingleInstanceCoordinator())
            {
                AssertEx.True(next.TryBecomePrimary());
            }
        });
    }

    private static bool TryBecomePrimaryOnWorker(SingleInstanceCoordinator coordinator)
    {
        bool becamePrimary = true;
        using (ManualResetEvent completed = new ManualResetEvent(false))
        {
            Thread worker = new Thread(new ThreadStart(delegate {
                becamePrimary = coordinator.TryBecomePrimary();
                completed.Set();
            }));
            worker.Start();
            AssertEx.True(completed.WaitOne(2000));
            worker.Join();
        }
        return becamePrimary;
    }
}
