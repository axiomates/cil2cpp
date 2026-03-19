using Polly;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== PollyTest ===");

        // Test 1: ResiliencePipeline.Empty — exercises GVM dispatch (ExecuteCore<T,S>)
        Console.WriteLine("[1] Testing empty pipeline...");
        var pipeline = ResiliencePipeline.Empty;
        int counter = 0;
        pipeline.Execute(() => { counter++; });
        Console.WriteLine($"Empty pipeline: OK (counter={counter})");

        // Test 2: Execute with return value
        Console.WriteLine("[2] Testing execute with return value...");
        string result = pipeline.Execute(() => "hello-polly");
        Console.WriteLine($"Execute result: {result}");

        // Test 3: Execute with CancellationToken
        Console.WriteLine("[3] Testing execute with CancellationToken...");
        pipeline.Execute(ct => { counter++; });
        Console.WriteLine($"CancellationToken: OK (counter={counter})");

        // Test 4: Execute<T> with CancellationToken
        Console.WriteLine("[4] Testing Execute<T> with CancellationToken...");
        int val = pipeline.Execute(ct => 42);
        Console.WriteLine($"Execute<int>: {val}");

        Console.WriteLine("=== Done ===");
    }
}
