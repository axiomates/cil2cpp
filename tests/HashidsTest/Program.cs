using System;
using System.Linq;

class Program
{
    static void Main()
    {
        // [1] Basic encode/decode
        try
        {
            var hashids = new HashidsNet.Hashids("salt");
            string encoded = hashids.Encode(1, 2, 3);
            int[] decoded = hashids.Decode(encoded);
            Console.WriteLine($"[1] Encoded: {encoded}, Decoded: {string.Join(",", decoded)}");
        }
        catch (Exception ex) { Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [2] Minimum length
        try
        {
            var hashids = new HashidsNet.Hashids("salt", 8);
            string encoded = hashids.Encode(42);
            Console.WriteLine($"[2] MinLength=8: {encoded} (len={encoded.Length})");
        }
        catch (Exception ex) { Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [3] Single value encode/decode
        try
        {
            var hashids = new HashidsNet.Hashids("my salt");
            string encoded = hashids.Encode(12345);
            int[] decoded = hashids.Decode(encoded);
            Console.WriteLine($"[3] Single: {encoded} -> {decoded[0]}");
        }
        catch (Exception ex) { Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [4] Long value encode/decode
        try
        {
            var hashids = new HashidsNet.Hashids("salt");
            string encoded = hashids.EncodeLong(9999999999L);
            long[] decoded = hashids.DecodeLong(encoded);
            Console.WriteLine($"[4] Long: {encoded} -> {decoded[0]}");
        }
        catch (Exception ex) { Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [5] Hex encode/decode
        try
        {
            var hashids = new HashidsNet.Hashids("salt");
            string encoded = hashids.EncodeHex("DEADBEEF");
            string decoded = hashids.DecodeHex(encoded);
            Console.WriteLine($"[5] Hex: {encoded} -> {decoded}");
        }
        catch (Exception ex) { Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [6] LINQ Intersect (exercises fault handler - used internally by Hashids)
        try
        {
            int[] a = { 1, 2, 3, 4 };
            int[] b = { 3, 4, 5, 6 };
            int[] result = a.Intersect(b).ToArray();
            Console.WriteLine($"[6] Intersect: {string.Join(",", result)}");
        }
        catch (Exception ex) { Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [7] Different salt produces different encoding
        try
        {
            var h1 = new HashidsNet.Hashids("salt1");
            var h2 = new HashidsNet.Hashids("salt2");
            string e1 = h1.Encode(100);
            string e2 = h2.Encode(100);
            Console.WriteLine($"[7] Different salts: {e1} != {e2} -> {e1 != e2}");
        }
        catch (Exception ex) { Console.WriteLine($"[7] ERROR: {ex.GetType().Name}: {ex.Message}"); }
    }
}
