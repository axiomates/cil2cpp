using Microsoft.Extensions.Configuration;

/// <summary>
/// Phase D.4 test: Microsoft.Extensions.Configuration deep validation.
/// Exercises: in-memory provider, section access, GetValue&lt;T&gt;, nested keys,
/// boolean/int/double parsing, array-style keys, multiple providers, overrides.
///
/// Known gaps (not tested):
///   - Environment variable provider (requires test isolation)
///   - Command-line provider (would need arg passing)
///   - IOptions&lt;T&gt; pattern (requires M.E.Options.ConfigurationExtensions package)
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== ConfigTest ===");

        int testNum = 0;

        // Test 1: Build configuration from in-memory data
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppName"] = "TestApp",
                    ["Port"] = "8080",
                })
                .Build();
            Console.WriteLine($"[{testNum}] Built: {(config["AppName"] == "TestApp" ? "OK" : "FAIL")}");
        }

        // Test 2: GetValue<int>
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Port"] = "8080",
                    ["Count"] = "42",
                })
                .Build();
            int port = config.GetValue<int>("Port");
            int count = config.GetValue<int>("Count");
            Console.WriteLine($"[{testNum}] GetValue<int>: {(port == 8080 && count == 42 ? "OK" : "FAIL")} (port={port}, count={count})");
        }

        // Test 3: GetValue<bool>
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Enabled"] = "true",
                    ["Verbose"] = "false",
                    ["Flag"] = "True",
                })
                .Build();
            bool enabled = config.GetValue<bool>("Enabled");
            bool verbose = config.GetValue<bool>("Verbose");
            bool flag = config.GetValue<bool>("Flag");
            Console.WriteLine($"[{testNum}] GetValue<bool>: {(enabled && !verbose && flag ? "OK" : "FAIL")}");
        }

        // Test 4: GetValue<double>
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Rate"] = "3.14",
                    ["Factor"] = "0.5",
                })
                .Build();
            double rate = config.GetValue<double>("Rate");
            double factor = config.GetValue<double>("Factor");
            bool ok = Math.Abs(rate - 3.14) < 0.001 && Math.Abs(factor - 0.5) < 0.001;
            Console.WriteLine($"[{testNum}] GetValue<double>: {(ok ? "OK" : "FAIL")} (rate={rate}, factor={factor})");
        }

        // Test 5: Section access with nested keys
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:Name"] = "mydb",
                })
                .Build();
            var section = config.GetSection("Database");
            bool ok = section["Host"] == "localhost"
                && section["Port"] == "5432"
                && section["Name"] == "mydb";
            Console.WriteLine($"[{testNum}] Section: {(ok ? "OK" : "FAIL")} (host={section["Host"]}, port={section["Port"]})");
        }

        // Test 6: Missing key returns null / default
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();
            var missing = config["NonExistent"];
            int defaultVal = config.GetValue<int>("Missing", 99);
            Console.WriteLine($"[{testNum}] Missing: {(missing == null && defaultVal == 99 ? "OK" : "FAIL")}");
        }

        // Test 7: Array-style keys (indexed)
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Items:0"] = "Alpha",
                    ["Items:1"] = "Beta",
                    ["Items:2"] = "Gamma",
                })
                .Build();
            var section = config.GetSection("Items");
            var children = section.GetChildren().ToList();
            bool ok = children.Count == 3
                && children[0].Value == "Alpha"
                && children[1].Value == "Beta"
                && children[2].Value == "Gamma";
            Console.WriteLine($"[{testNum}] Array keys: {(ok ? "OK" : "FAIL")} (count={children.Count})");
        }

        // Test 8: Multiple providers (later overrides earlier)
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Name"] = "Original",
                    ["Color"] = "Red",
                })
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Name"] = "Override",
                })
                .Build();
            bool ok = config["Name"] == "Override" && config["Color"] == "Red";
            Console.WriteLine($"[{testNum}] Override: {(ok ? "OK" : "FAIL")} (name={config["Name"]}, color={config["Color"]})");
        }

        // Test 9: GetSection on non-existent key returns empty section
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();
            var section = config.GetSection("Ghost");
            var children = section.GetChildren().ToList();
            Console.WriteLine($"[{testNum}] Empty section: {(section.Value == null && children.Count == 0 ? "OK" : "FAIL")}");
        }

        // Test 10: Deep nested hierarchy
        testNum++;
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["A:B:C:D"] = "deep-value",
                    ["A:B:X"] = "mid-value",
                })
                .Build();
            var deep = config["A:B:C:D"];
            var mid = config.GetSection("A:B")["X"];
            Console.WriteLine($"[{testNum}] Deep nested: {(deep == "deep-value" && mid == "mid-value" ? "OK" : "FAIL")}");
        }

        Console.WriteLine("=== Done ===");
    }
}
