using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

/// <summary>
/// Phase D.0 test: NuGet package integration validation.
/// Verifies that CIL2CPP can compile a project with NuGet PackageReference
/// (Newtonsoft.Json) through the full pipeline: NuGet → Cecil → IR → C++.
/// Tests: basic serialize/deserialize, nested objects, collections, LINQ-to-JSON,
/// settings, JObject, JsonProperty attribute.
///
/// Known gaps (not tested — compiler bugs discovered during M6 Phase 2):
///   - Enum deserialization: Enum.ToObject() conversion fails (Int64 → custom enum type)
///   - Dictionary&lt;string,int&gt; deserialization: NullReferenceException in Newtonsoft reflection path
///   - StringEnumConverter: requires Enum.GetName() reflection not yet fully supported
/// </summary>
class Program
{
    enum Priority { Low, Medium, High, Critical }

    class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    class Address
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
        public int Zip { get; set; }
    }

    class Employee
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public Address? HomeAddress { get; set; }
        public List<string> Skills { get; set; } = new();
    }

    class Task
    {
        [JsonProperty("task_name")]
        public string Title { get; set; } = "";

        [JsonProperty("pri")]
        public int PriorityLevel { get; set; }

        public bool IsCompleted { get; set; }
    }

    static void Main()
    {
        Console.WriteLine("=== NuGetSimpleTest ===");

        int testNum = 0;

        // Test 1: Basic serialize/deserialize (original test)
        testNum++;
        {
            var person = new Person { Name = "Alice", Age = 30 };
            string json = JsonConvert.SerializeObject(person);
            var d = JsonConvert.DeserializeObject<Person>(json);
            bool ok = d != null && d.Name == "Alice" && d.Age == 30;
            Console.WriteLine($"[{testNum}] Basic serialize/deserialize: {(ok ? "OK" : "FAIL")}");
        }

        // Test 2: Nested object serialization
        testNum++;
        {
            var emp = new Employee
            {
                Name = "Bob",
                Age = 25,
                HomeAddress = new Address { Street = "123 Main St", City = "Springfield", Zip = 62704 },
                Skills = new List<string> { "C#", "Python", "SQL" }
            };
            string json = JsonConvert.SerializeObject(emp);
            var d = JsonConvert.DeserializeObject<Employee>(json);
            bool ok = d != null
                && d.Name == "Bob"
                && d.HomeAddress != null
                && d.HomeAddress.City == "Springfield"
                && d.HomeAddress.Zip == 62704
                && d.Skills.Count == 3
                && d.Skills[1] == "Python";
            Console.WriteLine($"[{testNum}] Nested object: {(ok ? "OK" : "FAIL")}");
        }

        // Test 3: List<T> serialization
        testNum++;
        {
            var people = new List<Person>
            {
                new Person { Name = "Alice", Age = 30 },
                new Person { Name = "Bob", Age = 25 },
                new Person { Name = "Carol", Age = 35 }
            };
            string json = JsonConvert.SerializeObject(people);
            try
            {
                var d = JsonConvert.DeserializeObject<List<Person>>(json);
                bool ok = d != null && d.Count == 3 && d[2].Name == "Carol" && d[0].Age == 30;
                Console.WriteLine($"[{testNum}] List<T> serialize: {(ok ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] List<T> serialize: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // Test 4: JsonProperty attribute rename
        testNum++;
        {
            try
            {
                var task = new Task { Title = "Write tests", PriorityLevel = 3, IsCompleted = false };
                string json = JsonConvert.SerializeObject(task);
                bool hasRename = json.Contains("task_name") && json.Contains("Write tests");
                bool hasPri = json.Contains("\"pri\"");
                var d = JsonConvert.DeserializeObject<Task>(json);
                bool roundtrip = d != null && d.Title == "Write tests" && d.PriorityLevel == 3;
                Console.WriteLine($"[{testNum}] JsonProperty rename: {(hasRename && hasPri && roundtrip ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] JsonProperty rename: FAIL ({ex.GetType().Name}: {ex.Message})");
                if (ex.InnerException != null)
                    Console.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
        }

        // Test 5: Formatting.Indented
        testNum++;
        {
            try
            {
                var person = new Person { Name = "Diana", Age = 28 };
                string json = JsonConvert.SerializeObject(person, Formatting.Indented);
                bool hasNewline = json.Contains("\n");
                bool hasName = json.Contains("Diana");
                Console.WriteLine($"[{testNum}] Formatting.Indented: {(hasNewline && hasName ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] Formatting.Indented: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // Test 6: NullValueHandling.Ignore
        testNum++;
        {
            try
            {
                var emp = new Employee { Name = "Eve", Age = 40, HomeAddress = null };
                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                string json = JsonConvert.SerializeObject(emp, settings);
                bool noNull = !json.Contains("HomeAddress");
                bool hasName = json.Contains("Eve");
                Console.WriteLine($"[{testNum}] NullValueHandling.Ignore: {(noNull && hasName ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] NullValueHandling.Ignore: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // Test 7: JObject.Parse + property access
        testNum++;
        {
            try
            {
                string raw = "{\"name\":\"Frank\",\"score\":95,\"tags\":[\"fast\",\"reliable\"]}";
                var jo = JObject.Parse(raw);
                string name = (string?)jo["name"] ?? "";
                int score = (int)(jo["score"] ?? 0);
                int tagCount = ((JArray?)jo["tags"])?.Count ?? 0;
                bool ok = name == "Frank" && score == 95 && tagCount == 2;
                Console.WriteLine($"[{testNum}] JObject.Parse: {(ok ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] JObject.Parse: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // Test 8: JObject manipulation + ToString
        testNum++;
        {
            try
            {
                var jo = new JObject();
                jo["id"] = 42;
                jo["label"] = "test";
                jo["active"] = true;
                string json = jo.ToString(Formatting.None);
                bool ok = json.Contains("\"id\":42") && json.Contains("\"label\":\"test\"") && json.Contains("\"active\":true");
                Console.WriteLine($"[{testNum}] JObject create: {(ok ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] JObject create: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // Test 9: Default value handling
        testNum++;
        {
            try
            {
                var settings = new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore };
                var person = new Person { Name = "", Age = 0 };
                string json = JsonConvert.SerializeObject(person, settings);
                bool noAge = !json.Contains("Age");
                Console.WriteLine($"[{testNum}] DefaultValue.Ignore: {(noAge ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] DefaultValue.Ignore: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // Test 10: Nested collection (List of objects with lists)
        testNum++;
        {
            try
            {
                var employees = new List<Employee>
                {
                    new Employee { Name = "Grace", Age = 32, Skills = new List<string> { "Java", "Go" } },
                    new Employee { Name = "Hank", Age = 29, Skills = new List<string> { "Rust", "C++" } }
                };
                string json = JsonConvert.SerializeObject(employees);
                var d = JsonConvert.DeserializeObject<List<Employee>>(json);
                bool ok = d != null && d.Count == 2
                    && d[0].Skills[0] == "Java"
                    && d[1].Skills[1] == "C++"
                    && d[1].Name == "Hank";
                Console.WriteLine($"[{testNum}] Nested collections: {(ok ? "OK" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{testNum}] Nested collections: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        Console.WriteLine("=== Done ===");
    }
}
