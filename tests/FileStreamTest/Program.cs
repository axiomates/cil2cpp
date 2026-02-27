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

        // --- StreamReader ReadLine diagnostic ---
        Console.Write("ReadLine test: ");
        Console.Out.Flush();

        // Step-by-step ReadLine implementation to find crash
        var sr = new StreamReader(swFile);

        // Test 1: Can we Peek?
        int peek = sr.Peek();
        Console.Write("peek=");
        Console.Write(peek);
        Console.Write(" ");
        Console.Out.Flush();

        // Test 2: Can we read the whole stream as chars?
        char[] charBuf = new char[256];
        int charCount = sr.Read(charBuf, 0, 256);
        Console.Write("chars=");
        Console.Write(charCount);
        Console.Write(" ");
        Console.Out.Flush();
        sr.Dispose();

        // Test 3: StringBuilder test
        var sb = new StringBuilder();
        sb.Append("Test");
        sb.Append('X');
        Console.Write("sb=");
        Console.Write(sb.ToString());
        Console.Write(" ");
        Console.Out.Flush();

        // Test 4: StringBuilder with Append(char[], int, int) — used by ReadLine
        var sb2 = new StringBuilder();
        char[] testChars = new char[] { 'A', 'B', 'C', 'D', 'E' };
        sb2.Append(testChars, 1, 3);
        Console.Write("sb2=");
        Console.Write(sb2.ToString());
        Console.Write(" ");
        Console.Out.Flush();

        // Test 5: IndexOf on ReadOnlySpan<char>
        Console.Write("indexOf=");
        Console.Out.Flush();
        ReadOnlySpan<char> spanTest = "Hello\nWorld".AsSpan();
        int idx = spanTest.IndexOfAny('\r', '\n');
        Console.Write(idx);
        Console.Write(" ");
        Console.Out.Flush();

        // Test 6: Actual ReadLine
        Console.Write("RL=");
        Console.Out.Flush();
        var sr2 = new StreamReader(swFile);
        string? line1 = sr2.ReadLine();
        Console.Write("[");
        Console.Write(line1 ?? "(null)");
        Console.Write("]");
        Console.Out.Flush();
        sr2.Dispose();

        Console.WriteLine();

        // --- Cleanup ---
        File.Delete(testFile);
        File.Delete(swFile);
        Console.WriteLine("=== Done ===");
    }
}
