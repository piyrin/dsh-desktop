using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal static class BackendServerHelper
{
    private static int Main(string[] args)
    {
        if (args.Length < 1) return 2;

        int port = Int32.Parse(args[0]);
        string stopListeningPath = args.Length > 1 ? args[1] : null;
        string exitPath = args.Length > 2 ? args[2] : null;
        TcpListener listener = new TcpListener(IPAddress.Loopback, port);

        try
        {
            listener.Start();
            Console.WriteLine("backend-helper-ready");
            Console.Out.Flush();

            while (String.IsNullOrEmpty(stopListeningPath) || !File.Exists(stopListeningPath))
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(5);
                    continue;
                }

                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] request = new byte[4096];
                    stream.Read(request, 0, request.Length);
                    byte[] body = Encoding.UTF8.GetBytes("<html><head><title>DeepSeek Harness</title></head></html>");
                    byte[] headers = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "
                        + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(headers, 0, headers.Length);
                    stream.Write(body, 0, body.Length);
                }
            }
        }
        finally
        {
            listener.Stop();
        }

        Console.WriteLine("backend-helper-stopped");
        Console.Out.Flush();
        while (!String.IsNullOrEmpty(exitPath) && !File.Exists(exitPath))
            Thread.Sleep(5);
        return 0;
    }
}
