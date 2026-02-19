using System;
using System.IO;

public class Program
{
    public static void Main()
    {
        string tempDir = Path.GetTempPath();
        string testDir = Path.Combine(tempDir, "cil2cpp_io_test");
        string testFile = Path.Combine(testDir, "test.txt");

        // --- Path operations ---
        Console.WriteLine("=== Path ===");

        string fullPath = Path.GetFullPath(".");
        Console.WriteLine(fullPath.Length > 0 ? "GetFullPath: OK" : "GetFullPath: FAIL");

        string dirName = Path.GetDirectoryName(testFile);
        Console.WriteLine(dirName != null ? "GetDirectoryName: OK" : "GetDirectoryName: FAIL");

        string fileName = Path.GetFileName(testFile);
        Console.WriteLine(fileName == "test.txt" ? "GetFileName: OK" : "GetFileName: FAIL");

        string stem = Path.GetFileNameWithoutExtension(testFile);
        Console.WriteLine(stem == "test" ? "GetFileNameWithoutExtension: OK" : "GetFileNameWithoutExtension: FAIL");

        string ext = Path.GetExtension(testFile);
        Console.WriteLine(ext == ".txt" ? "GetExtension: OK" : "GetExtension: FAIL");

        Console.WriteLine(tempDir.Length > 0 ? "GetTempPath: OK" : "GetTempPath: FAIL");

        // --- Directory operations ---
        Console.WriteLine("=== Directory ===");

        // Clean up from previous runs
        if (Directory.Exists(testDir))
        {
            if (File.Exists(testFile))
                File.Delete(testFile);
            // Note: we can't delete directories yet, so skip if exists
        }

        if (!Directory.Exists(testDir))
        {
            Directory.CreateDirectory(testDir);
            Console.WriteLine("CreateDirectory: OK");
        }
        else
        {
            Console.WriteLine("CreateDirectory: SKIP (exists)");
        }

        Console.WriteLine(Directory.Exists(testDir) ? "Exists(dir): OK" : "Exists(dir): FAIL");
        Console.WriteLine(!Directory.Exists(testDir + "_nonexistent") ? "NotExists: OK" : "NotExists: FAIL");

        // --- File operations ---
        Console.WriteLine("=== File ===");

        File.WriteAllText(testFile, "Hello, System.IO!");
        Console.WriteLine("WriteAllText: OK");

        Console.WriteLine(File.Exists(testFile) ? "Exists: OK" : "Exists: FAIL");
        Console.WriteLine(!File.Exists(testFile + ".nope") ? "NotExists: OK" : "NotExists: FAIL");

        string content = File.ReadAllText(testFile);
        Console.WriteLine(content == "Hello, System.IO!" ? "ReadAllText: OK" : "ReadAllText: FAIL");

        // ReadAllBytes / WriteAllBytes
        byte[] bytes = File.ReadAllBytes(testFile);
        Console.WriteLine(bytes.Length > 0 ? "ReadAllBytes: OK" : "ReadAllBytes: FAIL");

        string bytesFile = Path.Combine(testDir, "bytes.bin");
        File.WriteAllBytes(bytesFile, bytes);
        byte[] bytes2 = File.ReadAllBytes(bytesFile);
        Console.WriteLine(bytes.Length == bytes2.Length ? "WriteAllBytes: OK" : "WriteAllBytes: FAIL");

        // AppendAllText
        File.AppendAllText(testFile, " Appended.");
        string appended = File.ReadAllText(testFile);
        Console.WriteLine(appended == "Hello, System.IO! Appended." ? "AppendAllText: OK" : "AppendAllText: FAIL");

        // ReadAllLines
        string multiLine = Path.Combine(testDir, "lines.txt");
        File.WriteAllText(multiLine, "Line1\nLine2\nLine3");
        string[] lines = File.ReadAllLines(multiLine);
        Console.WriteLine(lines.Length == 3 ? "ReadAllLines: OK" : "ReadAllLines: FAIL");

        // Copy
        string copyDest = Path.Combine(testDir, "copy.txt");
        File.Copy(testFile, copyDest, true);
        Console.WriteLine(File.Exists(copyDest) ? "Copy: OK" : "Copy: FAIL");

        // Move
        string moveDest = Path.Combine(testDir, "moved.txt");
        File.Move(copyDest, moveDest, true);
        Console.WriteLine(File.Exists(moveDest) && !File.Exists(copyDest) ? "Move: OK" : "Move: FAIL");

        // Delete
        File.Delete(moveDest);
        Console.WriteLine(!File.Exists(moveDest) ? "Delete: OK" : "Delete: FAIL");

        // Cleanup
        File.Delete(testFile);
        File.Delete(bytesFile);
        File.Delete(multiLine);

        Console.WriteLine("=== Done ===");
    }
}
