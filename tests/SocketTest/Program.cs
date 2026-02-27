using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== SocketTest ===");

        // Phase 1: Socket creation
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
            Console.WriteLine($"IPEndPoint: OK ({ep})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IPEndPoint: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        // Phase 3: DNS resolution
        try
        {
            var addresses = Dns.GetHostAddresses("localhost");
            Console.WriteLine($"DNS Resolve: OK ({addresses.Length} addresses)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DNS Resolve: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        Console.WriteLine("=== Done ===");
    }
}
