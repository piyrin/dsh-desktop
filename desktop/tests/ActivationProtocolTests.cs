using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
        runner.Add("cross-thread disposal releases primary mutex", delegate {
            SingleInstanceCoordinator primary = new SingleInstanceCoordinator();
            AssertEx.True(primary.TryBecomePrimary());
            DisposeOnWorker(primary);
            using (SingleInstanceCoordinator next = new SingleInstanceCoordinator())
            {
                AssertEx.True(TryBecomePrimaryOnWorker(next));
            }
        });
        runner.Add("signal primary respects a busy-pipe deadline", delegate {
            using (NamedPipeServerStream busyPipe = new NamedPipeServerStream("DeepSeekHarness.Desktop.Activation.v1", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
            using (SingleInstanceCoordinator secondary = new SingleInstanceCoordinator())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                AssertEx.False(secondary.SignalPrimary(TimeSpan.FromMilliseconds(1)));
                stopwatch.Stop();
                AssertEx.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(50));
            }
        });
        runner.Add("stalled client does not block later activation", delegate {
            using (SingleInstanceCoordinator primary = new SingleInstanceCoordinator())
            using (SingleInstanceCoordinator secondary = new SingleInstanceCoordinator())
            using (ManualResetEvent activated = new ManualResetEvent(false))
            using (NamedPipeClientStream stalledClient = new NamedPipeClientStream(".", "DeepSeekHarness.Desktop.Activation.v1", PipeDirection.Out))
            {
                primary.ActivateRequested += delegate { activated.Set(); };
                AssertEx.True(primary.TryBecomePrimary());
                stalledClient.Connect(1000);
                using (StreamWriter writer = new StreamWriter(stalledClient, new System.Text.UTF8Encoding(false), 1024, true))
                {
                    writer.Write("ACT");
                    writer.Flush();
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                AssertEx.True(secondary.SignalPrimary(TimeSpan.FromMilliseconds(1500)));
                AssertEx.True(activated.WaitOne(1000));
                stopwatch.Stop();
                AssertEx.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
            }
        });
        runner.Add("concurrent primary election callers agree", delegate {
            using (SingleInstanceCoordinator coordinator = new SingleInstanceCoordinator())
            using (ManualResetEvent start = new ManualResetEvent(false))
            using (ManualResetEvent completed = new ManualResetEvent(false))
            {
                bool first = false;
                bool second = false;
                int completedCount = 0;
                Thread firstCaller = new Thread(new ThreadStart(delegate {
                    start.WaitOne();
                    first = coordinator.TryBecomePrimary();
                    if (Interlocked.Increment(ref completedCount) == 2) completed.Set();
                }));
                Thread secondCaller = new Thread(new ThreadStart(delegate {
                    start.WaitOne();
                    second = coordinator.TryBecomePrimary();
                    if (Interlocked.Increment(ref completedCount) == 2) completed.Set();
                }));
                firstCaller.Start();
                secondCaller.Start();
                start.Set();
                AssertEx.True(completed.WaitOne(2000));
                firstCaller.Join();
                secondCaller.Join();
                AssertEx.True(first);
                AssertEx.True(second);
            }
        });
        runner.Add("blocked activation handler cannot delay mutex release", delegate {
            SingleInstanceCoordinator primary = new SingleInstanceCoordinator();
            try
            {
                using (SingleInstanceCoordinator secondary = new SingleInstanceCoordinator())
                using (ManualResetEvent handlerStarted = new ManualResetEvent(false))
                using (ManualResetEvent releaseHandler = new ManualResetEvent(false))
                {
                    primary.ActivateRequested += delegate {
                        handlerStarted.Set();
                        releaseHandler.WaitOne();
                    };
                    if (!primary.TryBecomePrimary()) throw new Exception("Primary did not acquire the mutex.");
                    if (!secondary.SignalPrimary(TimeSpan.FromMilliseconds(1500))) throw new Exception("Secondary did not signal the primary.");
                    if (!handlerStarted.WaitOne(1000)) throw new Exception("Activation handler did not start.");
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    primary.Dispose();
                    stopwatch.Stop();
                    if (!(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500))) throw new Exception("Disposal waited for the activation handler.");
                    using (SingleInstanceCoordinator next = new SingleInstanceCoordinator())
                    {
                        if (!TryBecomePrimaryOnWorker(next)) throw new Exception("Mutex was not released after disposal.");
                    }
                    releaseHandler.Set();
                }
            }
            finally { primary.Dispose(); }
        });
        runner.Add("repeated stalled clients leave listener available", delegate {
            using (SingleInstanceCoordinator primary = new SingleInstanceCoordinator())
            using (SingleInstanceCoordinator secondary = new SingleInstanceCoordinator())
            using (ManualResetEvent activated = new ManualResetEvent(false))
            {
                primary.ActivateRequested += delegate { activated.Set(); };
                AssertEx.True(primary.TryBecomePrimary());
                for (int index = 0; index < 4; index++)
                {
                    using (NamedPipeClientStream stalledClient = new NamedPipeClientStream(".", "DeepSeekHarness.Desktop.Activation.v1", PipeDirection.Out))
                    {
                        stalledClient.Connect(1000);
                        using (StreamWriter writer = new StreamWriter(stalledClient, new System.Text.UTF8Encoding(false), 1024, true))
                        {
                            writer.Write("ACT");
                            writer.Flush();
                        }
                        Thread.Sleep(300);
                    }
                }
                AssertEx.True(secondary.SignalPrimary(TimeSpan.FromMilliseconds(1500)));
                AssertEx.True(activated.WaitOne(1000));
            }
        });
        runner.Add("blocked pipe write aborts before signal returns", delegate {
            using (BlockingFlushServer server = new BlockingFlushServer())
            using (SingleInstanceCoordinator secondary = new SingleInstanceCoordinator())
            {
                server.Start();
                Stopwatch stopwatch = Stopwatch.StartNew();
                AssertEx.False(secondary.SignalPrimary(TimeSpan.FromMilliseconds(100)));
                stopwatch.Stop();
                AssertEx.True(server.ClientConnected.WaitOne(1000));
                AssertEx.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500));
                server.ReleaseRead.Set();
                AssertEx.True(server.ClientDisconnected.WaitOne(500));
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

    private static void DisposeOnWorker(SingleInstanceCoordinator coordinator)
    {
        using (ManualResetEvent completed = new ManualResetEvent(false))
        {
            Thread worker = new Thread(new ThreadStart(delegate {
                coordinator.Dispose();
                completed.Set();
            }));
            worker.Start();
            AssertEx.True(completed.WaitOne(2000));
            worker.Join();
        }
    }

    private sealed class BlockingFlushServer : IDisposable
    {
        private readonly NamedPipeServerStream server = new NamedPipeServerStream("DeepSeekHarness.Desktop.Activation.v1", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        private readonly Thread thread;

        internal BlockingFlushServer()
        {
            ClientConnected = new ManualResetEvent(false);
            ReleaseRead = new ManualResetEvent(false);
            ClientDisconnected = new ManualResetEvent(false);
            thread = new Thread(new ThreadStart(Run));
            thread.IsBackground = true;
        }

        internal ManualResetEvent ClientConnected { get; private set; }
        internal ManualResetEvent ReleaseRead { get; private set; }
        internal ManualResetEvent ClientDisconnected { get; private set; }

        internal void Start()
        {
            thread.Start();
        }

        public void Dispose()
        {
            ReleaseRead.Set();
            server.Dispose();
            thread.Join(1000);
            ClientConnected.Dispose();
            ReleaseRead.Dispose();
            ClientDisconnected.Dispose();
        }

        private void Run()
        {
            try
            {
                server.WaitForConnection();
                ClientConnected.Set();
                ReleaseRead.WaitOne();
                while (server.ReadByte() >= 0) { }
                ClientDisconnected.Set();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
