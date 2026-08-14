using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

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
    private const int ClientReadTimeoutMilliseconds = 250;

    private readonly object sync = new object();
    private readonly ManualResetEvent electionCompleted = new ManualResetEvent(false);
    private NamedPipeServerStream listeningPipe;
    private Thread primaryThread;
    private bool electionAttempted;
    private bool primary;
    private bool disposed;

    internal event EventHandler ActivateRequested;

    internal bool TryBecomePrimary()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (!electionAttempted)
            {
                electionAttempted = true;
                primaryThread = new Thread(RunPrimary);
                primaryThread.IsBackground = true;
                primaryThread.Start();
            }
        }

        electionCompleted.WaitOne();
        lock (sync) return primary;
    }

    internal bool SignalPrimary(TimeSpan timeout)
    {
        if (disposed) return false;

        int waitMilliseconds = ToBoundedMilliseconds(timeout);
        if (waitMilliseconds == 0) return false;

        Stopwatch stopwatch = Stopwatch.StartNew();
        SignalAttempt attempt = new SignalAttempt();
        using (Timer deadline = new Timer(delegate { attempt.Expire(); }, null, waitMilliseconds, Timeout.Infinite))
        {
            return SendActivation(attempt, stopwatch, waitMilliseconds);
        }
    }

    public void Dispose()
    {
        NamedPipeServerStream pipeToClose;
        Thread threadToJoin;

        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            pipeToClose = listeningPipe;
            threadToJoin = primaryThread;
            listeningPipe = null;
        }

        if (pipeToClose != null)
        {
            WakeListener();
            try { pipeToClose.Dispose(); }
            catch (IOException) { }
        }
        if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            threadToJoin.Join(1000);
    }

    private void RunPrimary()
    {
        Mutex primaryMutex = null;
        bool ownsPrimaryMutex = false;
        try
        {
            primaryMutex = new Mutex(false, MutexName);
            try { ownsPrimaryMutex = primaryMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsPrimaryMutex = true; }
            lock (sync)
            {
                primary = ownsPrimaryMutex && !disposed;
                electionCompleted.Set();
            }
            if (!primary) return;
            ListenForActivation();
        }
        finally
        {
            if (!electionCompleted.WaitOne(0))
            {
                lock (sync)
                {
                    primary = false;
                    electionCompleted.Set();
                }
            }
            if (ownsPrimaryMutex) primaryMutex.ReleaseMutex();
            if (primaryMutex != null) primaryMutex.Dispose();
            lock (sync) primary = false;
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
                    server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
        byte[] buffer = new byte[1];
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            int remainingMilliseconds = ClientReadTimeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0) return null;
            using (ManualResetEvent readCompleted = new ManualResetEvent(false))
            {
                IAsyncResult read = stream.BeginRead(buffer, 0, 1, delegate(IAsyncResult ignored) {
                    try { readCompleted.Set(); }
                    catch (ObjectDisposedException) { }
                }, null);
                if (!readCompleted.WaitOne(remainingMilliseconds))
                {
                    AbortRead(stream, read, readCompleted);
                    return null;
                }
                int readCount;
                try { readCount = stream.EndRead(read); }
                catch (IOException) { return null; }
                catch (ObjectDisposedException) { return null; }
                if (readCount == 0) return null;
            }
            bytes.Add(buffer[0]);
            if (buffer[0] == '\n') return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    private static bool SendActivation(SignalAttempt attempt, Stopwatch stopwatch, int budgetMilliseconds)
    {
        while (!attempt.IsExpired)
        {
            int remainingMilliseconds = RemainingMilliseconds(stopwatch, budgetMilliseconds);
            if (remainingMilliseconds <= 0) return false;

            NamedPipeClientStream client = null;
            bool connected = false;
            try
            {
                client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                if (!attempt.SetClient(client)) return false;
                client.Connect(remainingMilliseconds);
                connected = true;
                if (attempt.IsExpired) return false;
                byte[] activation = new UTF8Encoding(false).GetBytes(ActivationProtocol.SerializeActivate());
                IAsyncResult write = client.BeginWrite(activation, 0, activation.Length, null, null);
                if (!write.AsyncWaitHandle.WaitOne(RemainingMilliseconds(stopwatch, budgetMilliseconds)))
                {
                    attempt.Expire();
                    write.AsyncWaitHandle.WaitOne();
                    try { client.EndWrite(write); }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                    catch (OperationCanceledException) { }
                    return false;
                }
                client.EndWrite(write);
                if (attempt.IsExpired) return false;
                client.Flush();
                return !attempt.IsExpired && RemainingMilliseconds(stopwatch, budgetMilliseconds) >= 0;
            }
            catch (IOException)
            {
                if (attempt.IsExpired || connected) return false;
                int delayMilliseconds = Math.Min(10, RemainingMilliseconds(stopwatch, budgetMilliseconds));
                if (delayMilliseconds <= 0) return false;
                Thread.Sleep(delayMilliseconds);
            }
            catch (TimeoutException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch (OperationCanceledException) { return false; }
            finally
            {
                attempt.ClearClient(client);
                if (client != null) client.Dispose();
            }
        }
        return false;
    }

    private static void AbortRead(Stream stream, IAsyncResult read, ManualResetEvent readCompleted)
    {
        try { stream.Dispose(); }
        finally
        {
            readCompleted.WaitOne();
            try { stream.EndRead(read); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    private void OnActivateRequested()
    {
        EventHandler handler = ActivateRequested;
        if (handler == null) return;
        ThreadPool.QueueUserWorkItem(delegate {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { }
        });
    }

    private static int ToBoundedMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return 0;
        if (timeout.TotalMilliseconds >= MaximumSignalWaitMilliseconds) return MaximumSignalWaitMilliseconds;
        return (int)timeout.TotalMilliseconds;
    }

    private static int RemainingMilliseconds(Stopwatch stopwatch, int budgetMilliseconds)
    {
        long remaining = budgetMilliseconds - stopwatch.ElapsedMilliseconds;
        return remaining <= 0 ? 0 : (int)remaining;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(IntPtr fileHandle, IntPtr overlapped);

    private static void CancelPendingIo(NamedPipeClientStream client)
    {
        try
        {
            SafePipeHandle handle = client.SafePipeHandle;
            bool addedReference = false;
            try
            {
                handle.DangerousAddRef(ref addedReference);
                CancelIoEx(handle.DangerousGetHandle(), IntPtr.Zero);
            }
            finally
            {
                if (addedReference) handle.DangerousRelease();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
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

    private sealed class SignalAttempt
    {
        private readonly object signalSync = new object();
        private NamedPipeClientStream client;
        private bool expired;

        internal bool IsExpired
        {
            get { lock (signalSync) return expired; }
        }

        internal bool SetClient(NamedPipeClientStream candidate)
        {
            lock (signalSync)
            {
                if (expired) return false;
                client = candidate;
                return true;
            }
        }

        internal void ClearClient(NamedPipeClientStream candidate)
        {
            lock (signalSync)
            {
                if (Object.ReferenceEquals(client, candidate)) client = null;
            }
        }

        internal void Expire()
        {
            NamedPipeClientStream clientToClose;
            lock (signalSync)
            {
                if (expired) return;
                expired = true;
                clientToClose = client;
            }
            if (clientToClose != null)
            {
                CancelPendingIo(clientToClose);
                clientToClose.Dispose();
            }
        }
    }
}
