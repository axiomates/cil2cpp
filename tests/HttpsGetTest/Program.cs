using System;
using System.Net.Http;
using System.Net.Security;

/// <summary>
/// Phase E.win test: HTTPS GET using HttpClient over TLS (SChannel on Windows).
/// Tests the full SslStream → SSPI → SChannel P/Invoke chain.
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== HttpsGetTest ===");

        try
        {
            // Use a well-known HTTPS endpoint
            var handler = new SocketsHttpHandler
            {
                // Allow any cert for testing (avoids cert validation issues in AOT)
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                }
            };

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);

            string result = client.GetStringAsync("https://httpbin.org/get").Result;
            Console.WriteLine(result.Length > 0 ? "HTTPS GET: OK (length=" + result.Length + ")" : "HTTPS GET: FAIL (empty)");
        }
        catch (Exception ex)
        {
            Console.WriteLine("HTTPS GET: FAIL (" + ex.GetType().Name + ": " + ex.Message + ")");
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.WriteLine("  -> " + inner.GetType().Name + ": " + inner.Message);
                inner = inner.InnerException;
            }
        }

        Console.WriteLine("=== Done ===");
    }
}
