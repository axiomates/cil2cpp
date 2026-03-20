using Serilog;
using Serilog.Events;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== SerilogTest ===");

        // 1. Create logger with console sink
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "{Level:u3}: {Message:lj}{NewLine}")
            .CreateLogger();
        Console.WriteLine("[1] Logger created");

        // 2. Structured template — string value
        logger.Information("Hello {Name}", "Serilog");
        Console.WriteLine("[2] ok");

        // 3. Numeric formatting
        logger.Warning("Count is {Count}", 42);
        Console.WriteLine("[3] ok");

        // 4. Exception logging
        logger.Error(new Exception("test error"), "Error occurred");
        Console.WriteLine("[4] ok");

        // 5. Multiple properties (exercises InlineArray/SegmentedArrayBuilder)
        logger.Information("{A} {B} {C}", new object[] { "x", "y", "z" });
        Console.WriteLine("[5] ok");

        // 6. Six+ tokens (text + properties mixed — InlineArray boundary)
        logger.Information("X {A} {B} {C}", new object[] { "x", "y", "z" });
        Console.WriteLine("[6] ok");

        // 7. Debug level
        logger.Debug("Debug message: {Detail}", "verbose");
        Console.WriteLine("[7] ok");

        Log.CloseAndFlush();
        Console.WriteLine("=== Done ===");
    }
}
