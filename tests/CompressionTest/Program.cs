using System.IO.Compression;
using System.Text;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== CompressionTest ===");

        // Test 1: GZipStream round-trip
        var original = "Hello, compression! This is a test string repeated enough times to be compressible. "
                     + "Hello, compression! This is a test string repeated enough times to be compressible. "
                     + "Hello, compression! This is a test string repeated enough times to be compressible.";
        var originalBytes = Encoding.UTF8.GetBytes(original);

        // Compress
        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var gzip = new GZipStream(compressedStream, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(originalBytes, 0, originalBytes.Length);
            }
            compressed = compressedStream.ToArray();
        }

        bool compressedSmaller = compressed.Length < originalBytes.Length;
        Console.WriteLine(compressedSmaller ? "GZipCompress: OK" : "GZipCompress: FAIL");

        // Decompress
        byte[] decompressed;
        using (var compressedStream = new MemoryStream(compressed))
        using (var gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
        using (var resultStream = new MemoryStream())
        {
            gzip.CopyTo(resultStream);
            decompressed = resultStream.ToArray();
        }

        string result = Encoding.UTF8.GetString(decompressed);
        Console.WriteLine(result == original ? "GZipDecompress: OK" : "GZipDecompress: FAIL");

        // Test 2: DeflateStream round-trip
        byte[] deflateCompressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var deflate = new DeflateStream(compressedStream, CompressionMode.Compress, leaveOpen: true))
            {
                deflate.Write(originalBytes, 0, originalBytes.Length);
            }
            deflateCompressed = compressedStream.ToArray();
        }

        byte[] deflateDecompressed;
        using (var compressedStream = new MemoryStream(deflateCompressed))
        using (var deflate = new DeflateStream(compressedStream, CompressionMode.Decompress))
        using (var resultStream = new MemoryStream())
        {
            deflate.CopyTo(resultStream);
            deflateDecompressed = resultStream.ToArray();
        }

        string deflateResult = Encoding.UTF8.GetString(deflateDecompressed);
        Console.WriteLine(deflateResult == original ? "DeflateRoundTrip: OK" : "DeflateRoundTrip: FAIL");

        Console.WriteLine("=== Done ===");
    }
}
