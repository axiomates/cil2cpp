using System;
using System.Globalization;

class Program
{
    static void Main()
    {
        // Test 1: Decimal arithmetic
        try
        {
            decimal a = 100.50m;
            decimal b = 23.75m;
            Console.WriteLine($"[1] Add: {a + b}, Sub: {a - b}, Mul: {a * b}, Div: {a / b}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 2: Decimal comparison
        try
        {
            decimal x = 10.5m;
            decimal y = 10.50m;
            decimal z = 10.6m;
            Console.WriteLine($"[2] 10.5==10.50: {x == y}, 10.5<10.6: {x < z}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 3: Decimal.Parse
        try
        {
            decimal parsed = decimal.Parse("12345.6789", CultureInfo.InvariantCulture);
            Console.WriteLine($"[3] Parsed: {parsed.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 4: Decimal.TryParse
        try
        {
            bool ok1 = decimal.TryParse("99.99", NumberStyles.Any, CultureInfo.InvariantCulture, out var r1);
            bool ok2 = decimal.TryParse("not-a-number", NumberStyles.Any, CultureInfo.InvariantCulture, out _);
            Console.WriteLine($"[4] TryParse: valid={ok1} ({r1.ToString(CultureInfo.InvariantCulture)}), invalid={ok2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 5: Decimal.ToString with format
        try
        {
            decimal val = 12345.6789m;
            Console.WriteLine($"[5] F2: {val.ToString("F2", CultureInfo.InvariantCulture)}, N0: {val.ToString("N0", CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 6: Math.Round
        try
        {
            decimal val = 3.14159m;
            Console.WriteLine($"[6] Round(2): {Math.Round(val, 2)}, Round(4): {Math.Round(val, 4)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 7: Math.Floor and Math.Ceiling
        try
        {
            decimal val = 3.7m;
            Console.WriteLine($"[7] Floor: {Math.Floor(val)}, Ceiling: {Math.Ceiling(val)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[7] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 8: Decimal.MaxValue/MinValue
        try
        {
            Console.WriteLine($"[8] MaxValue: {decimal.MaxValue.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"[8] MinValue: {decimal.MinValue.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[8] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 9: Decimal to int/double conversion
        try
        {
            decimal d = 42.99m;
            int i = (int)d;
            double dbl = (double)d;
            Console.WriteLine($"[9] ToInt: {i}, ToDouble: {dbl}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[9] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 10: Decimal remainder
        try
        {
            decimal a = 10m;
            decimal b = 3m;
            Console.WriteLine($"[10] Remainder: {a % b}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[10] ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
