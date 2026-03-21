using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        // Test 1: Basic IsMatch
        try
        {
            bool match1 = Regex.IsMatch("hello123", @"\d+");
            bool match2 = Regex.IsMatch("hello", @"\d+");
            Console.WriteLine($"[1] IsMatch digits: {match1}, {match2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 2: Match with groups
        try
        {
            var m = Regex.Match("2026-03-21", @"(\d{4})-(\d{2})-(\d{2})");
            if (m.Success)
                Console.WriteLine($"[2] Date: year={m.Groups[1].Value}, month={m.Groups[2].Value}, day={m.Groups[3].Value}");
            else
                Console.WriteLine("[2] No match");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 3: Named groups
        try
        {
            var m = Regex.Match("John Smith, age 30", @"(?<name>\w+ \w+), age (?<age>\d+)");
            if (m.Success)
                Console.WriteLine($"[3] Name={m.Groups["name"].Value}, Age={m.Groups["age"].Value}");
            else
                Console.WriteLine("[3] No match");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 4: Replace
        try
        {
            string result = Regex.Replace("foo  bar   baz", @"\s+", " ");
            Console.WriteLine($"[4] Replace: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 5: Split
        try
        {
            string[] parts = Regex.Split("one,two,,three", @",+");
            Console.WriteLine($"[5] Split: {string.Join("|", parts)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 6: IgnoreCase
        try
        {
            bool match = Regex.IsMatch("Hello World", @"hello world", RegexOptions.IgnoreCase);
            Console.WriteLine($"[6] IgnoreCase: {match}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 7: Matches enumeration
        try
        {
            var matches = Regex.Matches("cat bat hat mat", @"\b\wat\b");
            Console.WriteLine($"[7] Matches count: {matches.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[7] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 8: Regex instance with compiled option (falls back to interpreter in AOT)
        try
        {
            var regex = new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");
            bool valid = regex.IsMatch("test@example.com");
            bool invalid = regex.IsMatch("not-an-email");
            Console.WriteLine($"[8] Email: valid={valid}, invalid={invalid}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[8] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 9: Replace with MatchEvaluator delegate
        try
        {
            string result = Regex.Replace("hello world", @"\b\w", m => m.Value.ToUpper());
            Console.WriteLine($"[9] Capitalize: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[9] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 10: Multiline mode
        try
        {
            string input = "first line\nsecond line\nthird line";
            var matches = Regex.Matches(input, @"^\w+ line$", RegexOptions.Multiline);
            Console.WriteLine($"[10] Multiline matches: {matches.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[10] ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
