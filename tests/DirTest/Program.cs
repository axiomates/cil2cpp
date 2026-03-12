using System;
using System.IO;

public class Program
{
    public static void Main()
    {
        string tempDir = Path.GetTempPath();
        Console.WriteLine("TempDir: " + tempDir);

        Console.WriteLine("Exists temp: " + Directory.Exists(tempDir));
        Console.WriteLine("Exists C:\\: " + Directory.Exists("C:\\"));
        Console.WriteLine("Exists C:\\Windows: " + Directory.Exists("C:\\Windows"));

        string testDir = Path.Combine(tempDir, "cil2cpp_dirtest");
        Console.WriteLine("TestDir: " + testDir);
        Console.WriteLine("Exists testDir: " + Directory.Exists(testDir));

        Directory.CreateDirectory(testDir);
        Console.WriteLine("Created!");

        Console.WriteLine("Exists after: " + Directory.Exists(testDir));
    }
}
