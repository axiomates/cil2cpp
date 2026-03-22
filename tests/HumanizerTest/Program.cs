using System;
using System.Globalization;
using Humanizer;

/// <summary>
/// Phase D.6 test: Humanizer 2.14.1 deep validation.
/// Exercises: string humanization, truncation, number-to-words,
/// pluralization/singularization, ordinalization, dehumanization.
///
/// Known gaps (compiler bugs discovered during M6 Phase 2):
///   - Humanize(LetterCasing.Title/AllCaps/LowerCase): IStringTransformer[] array
///     variance — SZGenericArrayEnumerator&lt;Object&gt; cannot cast to
///     IEnumerator&lt;IStringTransformer&gt;. Array covariance issue in AOT.
///   - TimeSpan.Humanize() — requires ResourceManager (known BCL gap)
///   - DateTime.Humanize() — relative time depends on ResourceManager
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== HumanizerTest ===");

        // Force English culture for deterministic output
        var en = new CultureInfo("en");
        int testNum = 0;

        // Test 1: String humanization — PascalCase
        testNum++;
        {
            var result = "PascalCaseInput".Humanize();
            Console.WriteLine($"[{testNum}] Humanize(Pascal): {(result == "Pascal case input" ? "OK" : "FAIL")} ({result})");
        }

        // Test 2: String humanization — underscore
        testNum++;
        {
            var result = "Underscored_input_string".Humanize();
            Console.WriteLine($"[{testNum}] Humanize(underscore): {(result == "Underscored input string" ? "OK" : "FAIL")}");
        }

        // Test 3: Truncation
        testNum++;
        {
            var result = "Long text that should be truncated".Truncate(10);
            bool ok = result.Length <= 10 && result.Contains("\u2026");
            Console.WriteLine($"[{testNum}] Truncate: {(ok ? "OK" : "FAIL")} ({result})");
        }

        // Test 4: Number to words
        testNum++;
        {
            var one = 1.ToWords(en);
            var fortyTwo = 42.ToWords(en);
            var hundred = 100.ToWords(en);
            bool ok = one == "one" && fortyTwo == "forty-two" && hundred == "one hundred";
            Console.WriteLine($"[{testNum}] ToWords: {(ok ? "OK" : "FAIL")} (1={one}, 42={fortyTwo}, 100={hundred})");
        }

        // Test 5: Larger number to words
        testNum++;
        {
            var thousand = 1000.ToWords(en);
            var big = 12345.ToWords(en);
            Console.WriteLine($"[{testNum}] ToWords(big): {(thousand == "one thousand" ? "OK" : "FAIL")} (1000={thousand})");
        }

        // Test 6: Pluralization
        testNum++;
        {
            var people = "person".Pluralize();
            var men = "man".Pluralize();
            var cats = "cat".Pluralize();
            Console.WriteLine($"[{testNum}] Pluralize: {(people == "people" && men == "men" && cats == "cats" ? "OK" : "FAIL")} ({people}, {men}, {cats})");
        }

        // Test 7: Singularization
        testNum++;
        {
            var dog = "dogs".Singularize();
            var child = "children".Singularize();
            var person = "people".Singularize();
            Console.WriteLine($"[{testNum}] Singularize: {(dog == "dog" && child == "child" && person == "person" ? "OK" : "FAIL")} ({dog}, {child}, {person})");
        }

        // Test 8: Ordinal numbers
        testNum++;
        {
            var first = 1.Ordinalize(en);
            var second = 2.Ordinalize(en);
            var third = 3.Ordinalize(en);
            var eleventh = 11.Ordinalize(en);
            bool ok = first == "1st" && second == "2nd" && third == "3rd" && eleventh == "11th";
            Console.WriteLine($"[{testNum}] Ordinalize: {(ok ? "OK" : "FAIL")} ({first}, {second}, {third}, {eleventh})");
        }

        // Test 9: Dehumanize (sentence back to PascalCase)
        testNum++;
        {
            var result = "Pascal case input".Dehumanize();
            Console.WriteLine($"[{testNum}] Dehumanize: {(result == "PascalCaseInput" ? "OK" : "FAIL")} ({result})");
        }

        // Test 10: Truncate with custom truncator
        testNum++;
        {
            var result = "Hello World from Humanizer".Truncate(15, "...");
            Console.WriteLine($"[{testNum}] Truncate(custom): {(result.EndsWith("...") && result.Length <= 15 ? "OK" : "FAIL")} ({result})");
        }

        Console.WriteLine("=== Done ===");
    }
}
