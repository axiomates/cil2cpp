using System;
using Newtonsoft.Json;

/// <summary>
/// Phase D.0 test: NuGet package integration validation.
/// Verifies that CIL2CPP can compile a project with NuGet PackageReference
/// (Newtonsoft.Json) through the full pipeline: NuGet → Cecil → IR → C++.
/// </summary>
class Program
{
    class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    static void Main()
    {
        Console.WriteLine("=== NuGetSimpleTest ===");

        // Serialize
        var person = new Person { Name = "Alice", Age = 30 };
        string json = JsonConvert.SerializeObject(person);
        Console.WriteLine("Serialized: " + json);

        // Deserialize
        var deserialized = JsonConvert.DeserializeObject<Person>(json);
        bool ok = deserialized != null
                  && deserialized.Name == "Alice"
                  && deserialized.Age == 30;
        Console.WriteLine(ok ? "NuGet: OK" : "NuGet: FAIL");

        Console.WriteLine("=== Done ===");
    }
}
