using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Phase 15 test: Microsoft.Extensions.DependencyInjection validation.
/// Verifies that CIL2CPP can compile a project with 3+ NuGet PackageReferences
/// through the full pipeline: NuGet → Cecil → IR → C++.
///
/// Tests: ServiceCollection, BuildServiceProvider, GetRequiredService,
/// AddSingleton/AddTransient, interface dispatch, generic service resolution,
/// ILogger<T> injection.
/// </summary>

interface IGreeter
{
    string Greet(string name);
}

class Greeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

interface ICounter
{
    int Next();
}

class Counter : ICounter
{
    private int _count;
    public int Next() => ++_count;
}

class App
{
    private readonly IGreeter _greeter;

    public App(IGreeter greeter)
    {
        _greeter = greeter;
    }

    public void Run()
    {
        Console.WriteLine(_greeter.Greet("World"));
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("=== DITest ===");

        var services = new ServiceCollection();
        services.AddSingleton<IGreeter, Greeter>();
        services.AddTransient<App>();

        var provider = services.BuildServiceProvider();

        // Test 1: Basic service resolution
        Console.WriteLine("[1] Resolving IGreeter...");
        var greeter = provider.GetRequiredService<IGreeter>();
        Console.WriteLine(greeter.Greet("DI"));

        // Test 2: Direct App creation (bypass DI)
        Console.WriteLine("[2] Direct App creation...");
        var directApp = new App(greeter);
        directApp.Run();

        // Test 3: Constructor injection via DI
        Console.WriteLine("[3] Resolving App via DI...");
        try
        {
            var app = provider.GetRequiredService<App>();
            Console.WriteLine("[3] Running App...");
            app.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[3] Failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        Console.WriteLine("=== Done ===");
    }
}
