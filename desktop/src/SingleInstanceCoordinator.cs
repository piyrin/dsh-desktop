using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

internal static class ActivationProtocol
{
    internal static string SerializeActivate()
    {
        return "ACTIVATE\n";
    }

    internal static bool IsActivate(string message)
    {
        return message == "ACTIVATE\n";
    }
}

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\DeepSeekHarness.Desktop.v1";
    private const string PipeName = "DeepSeekHarness.Desktop.Activation.v1";
    private const int MaximumSignalWaitMilliseconds = 1500;

    private readonly object sync = new object();
    private Mutex mutex;
    private NamedPipeServerStream listeningPipe;
    private Thread listenerThread;
    private bool ownsMutex;
    private bool disposed;

    internal event EventHandler ActivateRequested;

    internal bool TryBecomePrimary()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (mutex != null) return ownsMutex;

            bool createdNew;
            mutex = new Mutex(false, MutexName, out createdNew);
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex) return false;

            listenerThread = new Thread(ListenForActivation);
            listenerThread.IsBackground = true;
            listenerThread.Start();
            return true;
        }
    }

    internal bool SignalPrimary(TimeSpan timeout)
    {
        if (disposed) return false;

        int waitMilliseconds = ToBoundedMilliseconds(timeout);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(waitMilliseconds);
        while (true)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    int remainingMilliseconds = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    try
                    {
                        client.Connect(remainingMilliseconds);
                    }
                    catch (IOException)
                    {
                        if (DateTime.UtcNow >= deadline) return false;
                        Thread.Sleep(10);
                        continue;
                    }
                    using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        writer.Write(ActivationProtocol.SerializeActivate());
                        writer.Flush();
                        return true;
                    }
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        NamedPipeServerStream pipeToClose;
        Thread threadToJoin;
        Mutex mutexToDispose;
        bool releaseMutex;

        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            pipeToClose = listeningPipe;
            threadToJoin = listenerThread;
            mutexToDispose = mutex;
            releaseMutex = ownsMutex;
            ownsMutex = false;
            listeningPipe = null;
            listenerThread = null;
            mutex = null;
        }

        if (pipeToClose != null)
        {
            WakeListener();
            try { pipeToClose.Dispose(); }
            catch (IOException) { }
        }
        if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            threadToJoin.Join(1000);
        if (mutexToDispose != null)
        {
            if (releaseMutex)
            {
                try { mutexToDispose.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            mutexToDispose.Dispose();
        }
    }

    private void ListenForActivation()
    {
        while (true)
        {
            NamedPipeServerStream server;
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                    listeningPipe = server;
                }
            }
            catch (IOException)
            {
                if (disposed) return;
                Thread.Sleep(10);
                continue;
            }

            try
            {
                server.WaitForConnection();
                string message = ReadUtf8Line(server);
                if (ActivationProtocol.IsActivate(message))
                    OnActivateRequested();
            }
            catch (IOException)
            {
                if (!disposed) continue;
            }
            catch (ObjectDisposedException)
            {
                if (!disposed) continue;
            }
            finally
            {
                lock (sync)
                {
                    if (Object.ReferenceEquals(listeningPipe, server)) listeningPipe = null;
                }
                server.Dispose();
            }
        }
    }

    private static string ReadUtf8Line(Stream stream)
    {
        List<byte> bytes = new List<byte>();
        while (true)
        {
            int value = stream.ReadByte();
            if (value < 0) return null;
            bytes.Add((byte)value);
            if (value == '\n') return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    private void OnActivateRequested()
    {
        EventHandler handler = ActivateRequested;
        if (handler != null) handler(this, EventArgs.Empty);
    }

    private static int ToBoundedMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return 0;
        if (timeout.TotalMilliseconds >= MaximumSignalWaitMilliseconds) return MaximumSignalWaitMilliseconds;
        return (int)timeout.TotalMilliseconds;
    }

    private static void WakeListener()
    {
        try
        {
            using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
            {
                client.Connect(100);
            }
        }
        catch (IOException) { }
        catch (TimeoutException) { }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException("SingleInstanceCoordinator");
    }
}
