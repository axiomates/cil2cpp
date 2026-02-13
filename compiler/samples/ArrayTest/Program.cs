using System;

public class Program
{
    public static void Main()
    {
        // Array initializer (uses RuntimeHelpers.InitializeArray)
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };

        // Array element access (ldelem)
        Console.WriteLine(numbers[0]);  // 10
        Console.WriteLine(numbers[2]);  // 30
        Console.WriteLine(numbers[4]);  // 50

        // Array length (ldlen)
        Console.WriteLine(numbers.Length);  // 5

        // Array element write (stelem) + read
        numbers[1] = 99;
        Console.WriteLine(numbers[1]);  // 99

        // Dynamic array creation + fill
        int[] arr = new int[3];
        for (int i = 0; i < 3; i++)
            arr[i] = i * 100;
        Console.WriteLine(arr[0]);  // 0
        Console.WriteLine(arr[1]);  // 100
        Console.WriteLine(arr[2]);  // 200
    }
}
