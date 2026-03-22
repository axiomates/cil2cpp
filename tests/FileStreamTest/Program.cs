using System;
using System.IO;
using System.Text;

/// <summary>
/// Tests FileStream and StreamReader/StreamWriter directly (BCL IL path),
/// NOT through File.ReadAllText/WriteAllText (ICall bypass).
/// </summary>
public class Program
{
    public static void Main()
    {
        string tempDir = Path.GetTempPath();
        string testDir = Path.Combine(tempDir, "cil2cpp_fstream_test");
        string testFile = Path.Combine(testDir, "stream_test.txt");

        // Ensure test directory exists
        if (!Directory.Exists(testDir))
            Directory.CreateDirectory(testDir);

        // --- FileStream Write ---
        Console.Write("FileStream Write: ");
        var fsw = new FileStream(testFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.None);
        byte[] data = Encoding.UTF8.GetBytes("Hello from FileStream!");
        fsw.Write(data, 0, data.Length);
        fsw.Flush();
        fsw.Dispose();
        Console.WriteLine("OK");

        // --- FileStream Read ---
        Console.Write("FileStream Read: ");
        var fsr = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);
        byte[] buffer = new byte[64];
        int bytesRead = fsr.Read(buffer, 0, buffer.Length);
        string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        fsr.Dispose();
        Console.WriteLine(content == "Hello from FileStream!" ? "OK" : "FAIL");

        // --- StreamWriter ---
        Console.Write("StreamWriter: ");
        string swFile = Path.Combine(testDir, "writer_test.txt");
        var sw = new StreamWriter(swFile);
        sw.WriteLine("Line 1");
        sw.WriteLine("Line 2");
        sw.Write("Line 3");
        sw.Dispose();
        Console.WriteLine("OK");

        // --- StreamReader ReadLine ---
        Console.Write("StreamReader ReadLine: ");
        var sr = new StreamReader(swFile);
        string? line1 = sr.ReadLine();
        string? line2 = sr.ReadLine();
        string? line3 = sr.ReadLine();
        sr.Dispose();
        Console.WriteLine(line1 == "Line 1" && line2 == "Line 2" && line3 == "Line 3" ? "OK" : "FAIL");

        // --- Cleanup ---
        File.Delete(testFile);
        File.Delete(swFile);
        Console.WriteLine("=== Done ===");
    }
}
