using System;
using System.Globalization;

class Program
{
    static void Main()
    {
        // Test 1: DateTime construction and properties
        try
        {
            var dt = new DateTime(2026, 3, 21, 14, 30, 45);
            Console.WriteLine($"[1] DateTime: {dt.Year}-{dt.Month:D2}-{dt.Day:D2} {dt.Hour}:{dt.Minute}:{dt.Second}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 2: DateTime.Now is valid (non-default)
        try
        {
            var now = DateTime.Now;
            bool valid = now.Year >= 2026;
            Console.WriteLine($"[2] DateTime.Now valid: {valid}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 3: DateTime.UtcNow is valid
        try
        {
            var utc = DateTime.UtcNow;
            bool valid = utc.Year >= 2026;
            Console.WriteLine($"[3] DateTime.UtcNow valid: {valid}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 4: DateTime arithmetic
        try
        {
            var dt = new DateTime(2026, 1, 1);
            var dt2 = dt.AddDays(79);
            Console.WriteLine($"[4] AddDays(79): {dt2.Month}/{dt2.Day}");
            var dt3 = dt.AddHours(25);
            Console.WriteLine($"[4] AddHours(25): day={dt3.Day}, hour={dt3.Hour}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 5: DateTime comparison
        try
        {
            var d1 = new DateTime(2026, 1, 1);
            var d2 = new DateTime(2026, 12, 31);
            Console.WriteLine($"[5] Compare: d1<d2={d1 < d2}, d1==d1={d1 == d1}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 6: DateTime.ToString with format
        try
        {
            var dt = new DateTime(2026, 3, 21, 14, 30, 45);
            string formatted = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Console.WriteLine($"[6] Formatted: {formatted}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 7: DateTime.Parse
        try
        {
            var parsed = DateTime.Parse("2026-03-21 14:30:45", CultureInfo.InvariantCulture);
            Console.WriteLine($"[7] Parsed: {parsed.Year}-{parsed.Month:D2}-{parsed.Day:D2} {parsed.Hour}:{parsed.Minute}:{parsed.Second}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[7] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 8: TimeSpan
        try
        {
            var ts1 = TimeSpan.FromHours(2.5);
            var ts2 = TimeSpan.FromMinutes(45);
            var total = ts1 + ts2;
            Console.WriteLine($"[8] TimeSpan: {total.Hours}h {total.Minutes}m");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[8] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 9: TimeSpan subtraction
        try
        {
            var start = new DateTime(2026, 3, 21, 8, 0, 0);
            var end = new DateTime(2026, 3, 21, 17, 30, 0);
            var diff = end - start;
            Console.WriteLine($"[9] Work hours: {diff.TotalHours}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[9] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 10: DateTimeOffset
        try
        {
            var dto = new DateTimeOffset(2026, 3, 21, 14, 30, 0, TimeSpan.FromHours(8));
            Console.WriteLine($"[10] DateTimeOffset: {dto.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[10] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 11: DayOfWeek
        try
        {
            var dt = new DateTime(2026, 3, 21);
            Console.WriteLine($"[11] DayOfWeek: {dt.DayOfWeek}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[11] ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Test 12: DateTime.TryParse
        try
        {
            bool ok1 = DateTime.TryParse("2026-03-21", CultureInfo.InvariantCulture, DateTimeStyles.None, out var r1);
            bool ok2 = DateTime.TryParse("not-a-date", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            Console.WriteLine($"[12] TryParse: valid={ok1}, invalid={ok2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[12] ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
