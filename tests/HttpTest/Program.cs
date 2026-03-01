using System;
using System.Net.Http;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== HttpTest ===");

        // Phase 1: HttpClient construction
        try
        {
            var client = new HttpClient();
            Console.WriteLine("HttpClient: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine("HttpClient: FAIL (" + ex.GetType().Name + ": " + ex.Message + ")");
        }

        Console.WriteLine("=== Done ===");
    }
}
