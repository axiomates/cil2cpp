using System;
using System.Net;
using System.Net.Sockets;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== SocketTest ===");

        // Phase 1: Socket creation + close (Winsock P/Invoke)
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine("Socket Create: OK");
            socket.Close();
            Console.WriteLine("Socket Close: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Socket Create: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        // Phase 2: IPEndPoint creation
        try
        {
            var ep = new IPEndPoint(IPAddress.Loopback, 12345);
            Console.WriteLine("IPEndPoint: OK (127.0.0.1:12345)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IPEndPoint: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        // Phase 3: Second socket creation (verify no cumulative corruption)
        try
        {
            var s2 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Console.WriteLine("UDP Socket: OK");
            s2.Close();
            Console.WriteLine("UDP Close: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UDP Socket: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        Console.WriteLine("=== Done ===");
    }
}
