using System;
using Ardalis.GuardClauses;

class Program
{
    static void Main()
    {
        // [1] Guard.Against.Null — valid value
        try
        {
            string value = "hello";
            Guard.Against.Null(value, nameof(value));
            Console.WriteLine("[1] Null guard passed: hello");
        }
        catch (Exception ex) { Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [2] Guard.Against.Null catches null
        try
        {
            string? value = null;
            Guard.Against.Null(value, nameof(value));
            Console.WriteLine("[2] Should not reach here");
        }
        catch (ArgumentNullException ex) { Console.WriteLine($"[2] Caught null: {ex.ParamName}"); }
        catch (Exception ex) { Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [3] Guard.Against.NullOrEmpty string
        try
        {
            Guard.Against.NullOrEmpty("test", "param");
            Console.WriteLine("[3] NullOrEmpty passed: test");
        }
        catch (Exception ex) { Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [4] Guard.Against.NullOrEmpty catches empty
        try
        {
            Guard.Against.NullOrEmpty("", "param");
            Console.WriteLine("[4] Should not reach here");
        }
        catch (ArgumentException ex) { Console.WriteLine($"[4] Caught empty: {ex.ParamName}"); }
        catch (Exception ex) { Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [5] Guard.Against.NullOrWhiteSpace
        try
        {
            Guard.Against.NullOrWhiteSpace("  ", "param");
            Console.WriteLine("[5] Should not reach here");
        }
        catch (ArgumentException ex) { Console.WriteLine($"[5] Caught whitespace: {ex.ParamName}"); }
        catch (Exception ex) { Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [6] Guard.Against.OutOfRange int
        try
        {
            Guard.Against.OutOfRange(5, "val", 1, 10);
            Console.WriteLine("[6] OutOfRange passed: 5 in [1,10]");
        }
        catch (Exception ex) { Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [7] Guard.Against.OutOfRange catches out of range
        try
        {
            Guard.Against.OutOfRange(15, "val", 1, 10);
            Console.WriteLine("[7] Should not reach here");
        }
        catch (ArgumentOutOfRangeException ex) { Console.WriteLine($"[7] Caught out of range: {ex.ParamName}"); }
        catch (Exception ex) { Console.WriteLine($"[7] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [8] Guard.Against.Zero
        try
        {
            Guard.Against.Zero(42, "val");
            Console.WriteLine("[8] Zero guard passed: 42");
        }
        catch (Exception ex) { Console.WriteLine($"[8] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [9] Guard.Against.Negative
        try
        {
            Guard.Against.Negative(5, "val");
            Console.WriteLine("[9] Negative guard passed: 5");
        }
        catch (Exception ex) { Console.WriteLine($"[9] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [10] Guard.Against.NegativeOrZero catches zero
        try
        {
            Guard.Against.NegativeOrZero(0, "val");
            Console.WriteLine("[10] Should not reach here");
        }
        catch (ArgumentException ex) { Console.WriteLine($"[10] Caught zero: {ex.ParamName}"); }
        catch (Exception ex) { Console.WriteLine($"[10] ERROR: {ex.GetType().Name}: {ex.Message}"); }
    }
}
