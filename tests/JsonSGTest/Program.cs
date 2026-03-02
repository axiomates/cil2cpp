using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Phase D.5 test: System.Text.Json Source Generator validation.
/// Verifies that SG-produced IL compiles through CIL2CPP.
/// Uses [JsonSerializable] to force source generator codepath
/// (reflection-based JSON is disabled via project property).
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== JsonSGTest ===");

        var person = new Person { Name = "Bob", Age = 25 };

        // Serialize via source generator
        string json = JsonSerializer.Serialize(person, AppJsonContext.Default.Person);
        Console.WriteLine("Serialized: " + json);

        // Deserialize via source generator
        var deserialized = JsonSerializer.Deserialize(json, AppJsonContext.Default.Person);
        bool ok = deserialized != null
                  && deserialized.Name == "Bob"
                  && deserialized.Age == 25;
        Console.WriteLine(ok ? "JsonSG: OK" : "JsonSG: FAIL");

        Console.WriteLine("=== Done ===");
    }
}

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[JsonSerializable(typeof(Person))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
