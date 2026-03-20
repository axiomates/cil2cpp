using Microsoft.Extensions.Configuration;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== ConfigTest ===");

        // 1. Build configuration from in-memory data
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "TestApp",
                ["Port"] = "8080",
                ["Nested:Key1"] = "Value1",
                ["Nested:Key2"] = "Value2",
            })
            .Build();
        Console.WriteLine("[1] Configuration built");

        // 2. Read string value
        var appName = config["AppName"];
        Console.WriteLine($"[2] AppName = {appName}");

        // 3. Read typed value
        var port = config.GetValue<int>("Port");
        Console.WriteLine($"[3] Port = {port}");

        // 4. Section access
        var section = config.GetSection("Nested");
        Console.WriteLine($"[4] Nested:Key1 = {section["Key1"]}");
        Console.WriteLine($"[4] Nested:Key2 = {section["Key2"]}");

        // 5. Missing key returns null
        var missing = config["NonExistent"];
        Console.WriteLine($"[5] Missing = {(missing == null ? "null" : missing)}");

        // 6. Default value for missing typed key
        var timeout = config.GetValue<int>("Timeout", 30);
        Console.WriteLine($"[6] Timeout (default) = {timeout}");

        Console.WriteLine("=== Done ===");
    }
}
