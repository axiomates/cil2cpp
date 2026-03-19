using System;
using System.Globalization;
using Humanizer;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== HumanizerTest ===");

        // Force English culture for deterministic output
        var en = new CultureInfo("en");

        // String humanization
        Console.WriteLine("PascalCaseInput".Humanize());
        Console.WriteLine("Underscored_input_string".Humanize());

        // Truncation
        Console.WriteLine("Long text that should be truncated".Truncate(10));

        // Number to words (explicit English)
        Console.WriteLine(1.ToWords(en));
        Console.WriteLine(42.ToWords(en));

        // Pluralization
        Console.WriteLine("person".Pluralize());
        Console.WriteLine("dogs".Singularize());

        Console.WriteLine("=== Done ===");
    }
}
