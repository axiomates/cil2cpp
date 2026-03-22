using Polly;

/// <summary>
/// Phase D.2 test: Polly 8.x resilience pipeline validation.
/// Exercises: empty pipeline, Execute variants with return values and CancellationTokens,
/// multiple execute calls, multi-type dispatch.
///
/// Known gaps (compiler bugs discovered during M6 Phase 2):
///   - ResiliencePipelineBuilder.Build() throws IndexOutOfRangeException
///     (even empty builder). This blocks: AddRetry, AddTimeout, AddFallback,
///     pipeline composition, and all builder-based patterns.
///     Root cause: array bounds issue in Polly's internal validation or
///     component factory code path when compiled through CIL2CPP.
///   - Async ExecuteAsync (not tested, may have separate issues)
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== PollyTest ===");

        int testNum = 0;

        // Test 1: ResiliencePipeline.Empty — exercises GVM dispatch (ExecuteCore<T,S>)
        testNum++;
        {
            var pipeline = ResiliencePipeline.Empty;
            int counter = 0;
            pipeline.Execute(() => { counter++; });
            Console.WriteLine($"[{testNum}] Empty pipeline: {(counter == 1 ? "OK" : "FAIL")}");
        }

        // Test 2: Execute with return value
        testNum++;
        {
            var pipeline = ResiliencePipeline.Empty;
            string result = pipeline.Execute(() => "hello-polly");
            Console.WriteLine($"[{testNum}] Execute<string>: {(result == "hello-polly" ? "OK" : "FAIL")}");
        }

        // Test 3: Execute with CancellationToken overload
        testNum++;
        {
            var pipeline = ResiliencePipeline.Empty;
            int counter = 0;
            pipeline.Execute(ct => { counter++; });
            Console.WriteLine($"[{testNum}] Execute(ct): {(counter == 1 ? "OK" : "FAIL")}");
        }

        // Test 4: Execute<T> with CancellationToken
        testNum++;
        {
            var pipeline = ResiliencePipeline.Empty;
            int val = pipeline.Execute(ct => 42);
            Console.WriteLine($"[{testNum}] Execute<int>(ct): {(val == 42 ? "OK" : "FAIL")}");
        }

        // Test 5: Multiple Execute calls on same pipeline
        testNum++;
        {
            var pipeline = ResiliencePipeline.Empty;
            int sum = 0;
            for (int i = 0; i < 5; i++)
                sum += pipeline.Execute(ct => i + 1);
            Console.WriteLine($"[{testNum}] Multi-execute: {(sum == 15 ? "OK" : "FAIL")} (sum={sum})");
        }

        // Test 6: Execute with different return types
        testNum++;
        {
            var pipeline = ResiliencePipeline.Empty;
            bool boolResult = pipeline.Execute(() => true);
            double doubleResult = pipeline.Execute(() => 3.14);
            string strResult = pipeline.Execute(() => "test");
            bool ok = boolResult && Math.Abs(doubleResult - 3.14) < 0.001 && strResult == "test";
            Console.WriteLine($"[{testNum}] Multi-type: {(ok ? "OK" : "FAIL")}");
        }

        Console.WriteLine("=== Done ===");
    }
}
