using System;
using Slugify;

class Program
{
    static void Main()
    {
        var helper = new SlugHelper();

        // [1] Basic slug
        try
        {
            var result = helper.GenerateSlug("Hello World!");
            Console.WriteLine($"[1] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [2] Slug with special characters
        try
        {
            var result = helper.GenerateSlug("C# is AWESOME!!!");
            Console.WriteLine($"[2] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [3] Slug with mixed case and punctuation
        try
        {
            var result = helper.GenerateSlug("This Is A Test, Really!");
            Console.WriteLine($"[3] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [4] Slug with numbers
        try
        {
            var result = helper.GenerateSlug("Test 123 Value");
            Console.WriteLine($"[4] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [5] Empty string
        try
        {
            var result = helper.GenerateSlug("");
            Console.WriteLine($"[5] Empty: '{result}'");
        }
        catch (Exception ex) { Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [6] Already-slugified string
        try
        {
            var result = helper.GenerateSlug("already-a-slug");
            Console.WriteLine($"[6] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [7] Multiple spaces and dashes
        try
        {
            var result = helper.GenerateSlug("  Multiple   Spaces  And  ---Dashes---  ");
            Console.WriteLine($"[7] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[7] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [8] Custom config
        try
        {
            var config = new SlugHelperConfiguration();
            config.ForceLowerCase = false;
            var customHelper = new SlugHelper(config);
            var result = customHelper.GenerateSlug("Keep CASE");
            Console.WriteLine($"[8] Slug: {result}");
        }
        catch (Exception ex) { Console.WriteLine($"[8] ERROR: {ex.GetType().Name}: {ex.Message}"); }
    }
}
