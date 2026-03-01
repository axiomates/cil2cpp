using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== SocketTest ===");

        // Phase 1: Socket creation + close
        try
        {
            var s1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine("Socket Create: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Socket Create: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        // Phase 2: IPEndPoint creation
        try
        {
            var ep = new IPEndPoint(IPAddress.Loopback, 12345);
            Console.WriteLine("IPEndPoint: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IPEndPoint: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        // Phase 3: Bind test with step-by-step output
        Socket? server = null;
        try
        {
            Console.WriteLine("Phase3: creating server socket...");
            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine("Phase3: server socket created");

            Console.WriteLine("Phase3: creating endpoint...");
            var bindEp = new IPEndPoint(IPAddress.Loopback, 0);
            Console.WriteLine("Phase3: endpoint created");

            Console.WriteLine("Phase3: calling Bind...");
            server.Bind(bindEp);
            Console.WriteLine("Phase3: Bind OK");

            Console.WriteLine("Phase3: calling Listen...");
            server.Listen(1);
            Console.WriteLine("Phase3: Listen OK");

            var localEp = (IPEndPoint)server.LocalEndPoint!;
            Console.WriteLine($"Phase3: bound to port {localEp.Port}");

            Console.WriteLine("Phase3: creating client socket...");
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine("Phase3: client created");

            Console.WriteLine("Phase3: connecting...");
            client.Connect(new IPEndPoint(IPAddress.Loopback, localEp.Port));
            Console.WriteLine("Phase3: connected");

            Console.WriteLine("Phase3: accepting...");
            var accepted = server.Accept();
            Console.WriteLine("Phase3: accepted");

            // Send and Receive
            byte[] sendBuf = Encoding.UTF8.GetBytes("Hello Socket");
            int sent = client.Send(sendBuf);
            Console.WriteLine($"Send: OK ({sent} bytes)");

            byte[] recvBuf = new byte[1024];
            int received = accepted.Receive(recvBuf);
            string message = Encoding.UTF8.GetString(recvBuf, 0, received);
            Console.WriteLine($"Recv: {message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Phase3: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        // Phase 4: DNS resolution
        try
        {
            Console.WriteLine("Phase4: calling Dns.GetHostAddresses...");
            var addresses = Dns.GetHostAddresses("localhost");
            Console.WriteLine($"DNS: {addresses.Length} addresses");
            foreach (var addr in addresses)
            {
                // Use AddressFamily to verify the address object is valid
                // without calling IPAddress.ToString() which has complex formatting
                Console.WriteLine($"  addr family={addr.AddressFamily}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DNS: FAIL ({ex.GetType().Name}: {ex.Message})");
        }

        Console.WriteLine("=== Done ===");
    }
}
