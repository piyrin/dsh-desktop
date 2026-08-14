using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Net.Http;

internal static class DshReadinessTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("DshReadiness connection refusal is not ready", delegate {
            AssertEx.Equal(ReadinessState.NotReady,
                DshReadiness.Classify(null, null, new HttpRequestException("refused")));
        });
        runner.Add("DshReadiness shipped title marker is ready", delegate {
            AssertEx.Equal(ReadinessState.Ready,
                DshReadiness.Classify(200, "<title>DeepSeek Harness</title>", null));
        });
        runner.Add("DshReadiness case variant title marker is a port conflict", delegate {
            AssertEx.Equal(ReadinessState.PortConflict,
                DshReadiness.Classify(200, "<TITLE>deepseek harness</TITLE>", null));
        });
        runner.Add("DshReadiness unrelated html is a port conflict", delegate {
            AssertEx.Equal(ReadinessState.PortConflict,
                DshReadiness.Classify(200, "<title>Other App</title>", null));
        });
        runner.Add("DshReadiness non-success http response is a port conflict", delegate {
            AssertEx.Equal(ReadinessState.PortConflict,
                DshReadiness.Classify(404, "missing", null));
        });
        runner.Add("DshReadiness rejects a disposable non-DSH server on port 8080", delegate {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 8080);
            listener.Start();
            Thread server = new Thread(new ThreadStart(delegate {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] request = new byte[2048];
                        stream.Read(request, 0, request.Length);
                        byte[] body = Encoding.UTF8.GetBytes("<title>Other App</title>");
                        byte[] headers = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: "
                            + body.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(headers, 0, headers.Length);
                        stream.Write(body, 0, body.Length);
                    }
                }
                catch (SocketException) { }
            }));
            server.IsBackground = true;
            server.Start();
            try
            {
                ReadinessResult result = DshReadiness.WaitAsync(
                    new Uri("http://127.0.0.1:8080/"),
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None).GetAwaiter().GetResult();

                AssertEx.Equal(ReadinessState.PortConflict, result.State);
                AssertEx.True(result.Detail.IndexOf("occupied", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally
            {
                listener.Stop();
                server.Join(1000);
            }
        });
    }
}
