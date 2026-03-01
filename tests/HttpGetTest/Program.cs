using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// Phase C.6 test: Full HTTP GET using HttpClient against a local echo server.
/// This test will work once the compiler handles the full async HTTP chain.
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== HttpGetTest ===");

        // Spin up a minimal TCP server that returns a fixed HTTP response
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        var serverDone = new ManualResetEventSlim(false);
        var serverThread = new Thread(() =>
        {
            try
            {
                var conn = listener.Accept();
                byte[] buf = new byte[4096];
                conn.Receive(buf);

                string body = "Hello from CIL2CPP!";
                string response = "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: text/plain\r\n"
                    + "Content-Length: " + body.Length + "\r\n"
                    + "Connection: close\r\n"
                    + "\r\n"
                    + body;
                conn.Send(Encoding.UTF8.GetBytes(response));
                conn.Shutdown(SocketShutdown.Both);
                conn.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Server: " + ex.Message);
            }
            finally { serverDone.Set(); }
        });
        serverThread.IsBackground = true;
        serverThread.Start();

        try
        {
            var client = new HttpClient();
            string result = client.GetStringAsync("http://127.0.0.1:" + port + "/").Result;
            Console.WriteLine(result.Contains("Hello") ? "HTTP GET: OK" : "HTTP GET: FAIL");
        }
        catch (Exception ex)
        {
            Console.WriteLine("HTTP GET: FAIL (" + ex.GetType().Name + ": " + ex.Message + ")");
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.WriteLine("  -> " + inner.GetType().Name + ": " + inner.Message);
                inner = inner.InnerException;
            }
        }

        serverDone.Wait(5000);
        listener.Close();
        Console.WriteLine("=== Done ===");
    }
}
