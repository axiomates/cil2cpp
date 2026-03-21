using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

// ===== Domain Models =====

enum HealthStatus { Healthy, Degraded, Unhealthy, Unknown }

class EndpointConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int TimeoutMs { get; set; } = 5000;
    public int ExpectedStatusCode { get; set; } = 200;
    public string[] Tags { get; set; } = Array.Empty<string>();
}

class HealthCheckResult
{
    public string EndpointName { get; set; } = "";
    public HealthStatus Status { get; set; }
    public int ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; }

    public override string ToString()
    {
        var status = Status.ToString().ToUpper();
        if (ErrorMessage != null)
            return $"{EndpointName}: {status} ({ResponseTimeMs}ms) — {ErrorMessage}";
        return $"{EndpointName}: {status} ({ResponseTimeMs}ms)";
    }
}

class HealthCheckException : Exception
{
    public string EndpointName { get; }
    public HealthCheckException(string endpoint, string message)
        : base(message) { EndpointName = endpoint; }
}

// ===== Health Checker Service =====

class HealthChecker
{
    private readonly List<EndpointConfig> _endpoints;
    private readonly Dictionary<string, List<HealthCheckResult>> _history;
    private readonly Random _rng;

    public HealthChecker(List<EndpointConfig> endpoints)
    {
        _endpoints = endpoints;
        _history = new Dictionary<string, List<HealthCheckResult>>();
        _rng = new Random(42); // deterministic for reproducible output
    }

    // Simulates HTTP health check without actual network calls.
    // Uses deterministic RNG to produce consistent results.
    public async Task<HealthCheckResult> CheckEndpointAsync(EndpointConfig endpoint)
    {
        await Task.Yield(); // simulate async

        var result = new HealthCheckResult
        {
            EndpointName = endpoint.Name,
            CheckedAt = DateTime.UtcNow
        };

        // Deterministic simulation: use endpoint name hash + RNG
        var nameHash = endpoint.Name.Length;
        var latency = 10 + (_rng.Next(200));

        if (endpoint.Url.Contains("fail"))
        {
            result.Status = HealthStatus.Unhealthy;
            result.ResponseTimeMs = latency + 500;
            result.ErrorMessage = "Connection refused";
        }
        else if (endpoint.Url.Contains("slow"))
        {
            result.Status = latency > 150 ? HealthStatus.Degraded : HealthStatus.Healthy;
            result.ResponseTimeMs = latency + 300;
            result.ErrorMessage = result.Status == HealthStatus.Degraded ? "Slow response" : null;
        }
        else
        {
            result.Status = HealthStatus.Healthy;
            result.ResponseTimeMs = latency;
        }

        // Store history
        if (!_history.ContainsKey(endpoint.Name))
            _history[endpoint.Name] = new List<HealthCheckResult>();
        _history[endpoint.Name].Add(result);

        return result;
    }

    public async Task<List<HealthCheckResult>> CheckAllAsync()
    {
        var results = new List<HealthCheckResult>();
        foreach (var ep in _endpoints)
        {
            var result = await CheckEndpointAsync(ep);
            results.Add(result);
        }
        return results;
    }

    public Dictionary<string, List<HealthCheckResult>> GetHistory() => _history;
    public List<EndpointConfig> GetEndpoints() => _endpoints;
}

// ===== Report Generator =====

class HealthReport
{
    public List<HealthCheckResult> Results { get; }
    public DateTime GeneratedAt { get; }

    public HealthReport(List<HealthCheckResult> results)
    {
        Results = results;
        GeneratedAt = DateTime.UtcNow;
    }

    public int TotalChecks => Results.Count;
    public int HealthyCount => Results.Count(r => r.Status == HealthStatus.Healthy);
    public int DegradedCount => Results.Count(r => r.Status == HealthStatus.Degraded);
    public int UnhealthyCount => Results.Count(r => r.Status == HealthStatus.Unhealthy);

    public double AverageResponseMs => Results.Count > 0
        ? Results.Average(r => (double)r.ResponseTimeMs) : 0;

    public HealthStatus OverallStatus
    {
        get
        {
            if (UnhealthyCount > 0) return HealthStatus.Unhealthy;
            if (DegradedCount > 0) return HealthStatus.Degraded;
            if (HealthyCount > 0) return HealthStatus.Healthy;
            return HealthStatus.Unknown;
        }
    }

    public string GenerateMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Health Report — {OverallStatus}");
        sb.AppendLine($"Checks: {TotalChecks}, Avg: {AverageResponseMs:F0}ms");
        sb.AppendLine();
        foreach (var r in Results.OrderBy(r => r.Status).ThenBy(r => r.EndpointName))
        {
            var icon = r.Status switch
            {
                HealthStatus.Healthy => "OK",
                HealthStatus.Degraded => "WARN",
                HealthStatus.Unhealthy => "FAIL",
                _ => "??"
            };
            sb.AppendLine($"- [{icon}] {r}");
        }
        return sb.ToString().TrimEnd();
    }
}

// ===== Main Program =====

class Program
{
    static async Task Main()
    {
        int testNum = 0;

        // ===== Test 1: Configuration loading =====
        testNum++;
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:Name"] = "HealthChecker",
            ["App:Version"] = "1.0.0",
            ["App:CheckIntervalSec"] = "30",
            ["App:MaxRetries"] = "3",
            ["Endpoints:0:Name"] = "API Gateway",
            ["Endpoints:0:Url"] = "http://api.example.com/health",
            ["Endpoints:0:TimeoutMs"] = "3000",
            ["Endpoints:0:ExpectedStatusCode"] = "200",
            ["Endpoints:0:Tags:0"] = "api",
            ["Endpoints:0:Tags:1"] = "critical",
            ["Endpoints:1:Name"] = "Auth Service",
            ["Endpoints:1:Url"] = "http://auth.example.com/ping",
            ["Endpoints:1:TimeoutMs"] = "2000",
            ["Endpoints:1:ExpectedStatusCode"] = "200",
            ["Endpoints:1:Tags:0"] = "auth",
            ["Endpoints:2:Name"] = "Database",
            ["Endpoints:2:Url"] = "http://db.example.com/slow/status",
            ["Endpoints:2:TimeoutMs"] = "5000",
            ["Endpoints:2:ExpectedStatusCode"] = "200",
            ["Endpoints:2:Tags:0"] = "database",
            ["Endpoints:2:Tags:1"] = "critical",
            ["Endpoints:3:Name"] = "Cache",
            ["Endpoints:3:Url"] = "http://cache.example.com/health",
            ["Endpoints:3:TimeoutMs"] = "1000",
            ["Endpoints:3:ExpectedStatusCode"] = "200",
            ["Endpoints:3:Tags:0"] = "cache",
            ["Endpoints:4:Name"] = "Legacy Service",
            ["Endpoints:4:Url"] = "http://legacy.example.com/fail/health",
            ["Endpoints:4:TimeoutMs"] = "5000",
            ["Endpoints:4:ExpectedStatusCode"] = "200",
            ["Endpoints:4:Tags:0"] = "legacy",
            ["Endpoints:4:Tags:1"] = "deprecated",
            ["Endpoints:5:Name"] = "CDN",
            ["Endpoints:5:Url"] = "http://cdn.example.com/health",
            ["Endpoints:5:TimeoutMs"] = "2000",
            ["Endpoints:5:ExpectedStatusCode"] = "200",
            ["Endpoints:5:Tags:0"] = "cdn",
            ["Endpoints:6:Name"] = "Search Engine",
            ["Endpoints:6:Url"] = "http://search.example.com/slow/status",
            ["Endpoints:6:TimeoutMs"] = "4000",
            ["Endpoints:6:ExpectedStatusCode"] = "200",
            ["Endpoints:6:Tags:0"] = "search",
        });
        var config = configBuilder.Build();

        var appName = config["App:Name"];
        var appVersion = config["App:Version"];
        var interval = config.GetValue<int>("App:CheckIntervalSec");
        var maxRetries = config.GetValue<int>("App:MaxRetries");
        Console.WriteLine($"[{testNum}] Config: {appName} v{appVersion}, interval={interval}s, retries={maxRetries}");

        // ===== Test 2: Endpoint loading from config =====
        testNum++;
        var endpoints = new List<EndpointConfig>();
        var endpointsSection = config.GetSection("Endpoints");
        foreach (var child in endpointsSection.GetChildren())
        {
            var ep = new EndpointConfig
            {
                Name = child["Name"] ?? "unknown",
                Url = child["Url"] ?? "",
                TimeoutMs = int.TryParse(child["TimeoutMs"], out var t) ? t : 5000,
                ExpectedStatusCode = int.TryParse(child["ExpectedStatusCode"], out var s) ? s : 200,
            };
            var tagsSection = child.GetSection("Tags");
            var tags = new List<string>();
            foreach (var tag in tagsSection.GetChildren())
            {
                if (tag.Value != null) tags.Add(tag.Value);
            }
            ep.Tags = tags.ToArray();
            endpoints.Add(ep);
        }
        Console.WriteLine($"[{testNum}] Loaded {endpoints.Count} endpoints:");
        foreach (var ep in endpoints)
            Console.WriteLine($"    {ep.Name} → {ep.Url} (timeout={ep.TimeoutMs}ms, tags={string.Join(",", ep.Tags)})");

        // ===== Test 3: Serilog configuration =====
        testNum++;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();
        Log.Information("HealthChecker initialized: {AppName} v{Version}", appName, appVersion);
        Console.WriteLine($"[{testNum}] Serilog initialized");

        // ===== Test 4: Run health checks =====
        testNum++;
        var checker = new HealthChecker(endpoints);
        var results = await checker.CheckAllAsync();
        Console.WriteLine($"[{testNum}] Health check results ({results.Count}):");
        foreach (var r in results)
            Console.WriteLine($"    {r}");

        // ===== Test 5: Health report =====
        testNum++;
        var report = new HealthReport(results);
        Console.WriteLine($"[{testNum}] Report: overall={report.OverallStatus}, healthy={report.HealthyCount}, " +
            $"degraded={report.DegradedCount}, unhealthy={report.UnhealthyCount}, avg={report.AverageResponseMs:F0}ms");

        // ===== Test 6: Tag-based filtering =====
        testNum++;
        var criticalEndpoints = endpoints.Where(e => e.Tags.Contains("critical")).ToList();
        var criticalResults = results.Where(r => criticalEndpoints.Any(e => e.Name == r.EndpointName)).ToList();
        Console.WriteLine($"[{testNum}] Critical endpoints ({criticalResults.Count}):");
        foreach (var r in criticalResults)
            Console.WriteLine($"    {r}");

        // ===== Test 7: Tag aggregation =====
        testNum++;
        var tagGroups = endpoints
            .SelectMany(e => e.Tags.Select(t => new { Tag = t, Endpoint = e.Name }))
            .GroupBy(x => x.Tag)
            .OrderBy(g => g.Key)
            .ToList();
        Console.WriteLine($"[{testNum}] Tags ({tagGroups.Count}):");
        foreach (var g in tagGroups)
            Console.WriteLine($"    {g.Key}: {string.Join(", ", g.Select(x => x.Endpoint))}");

        // ===== Test 8: Status grouping =====
        testNum++;
        var statusGroups = results
            .GroupBy(r => r.Status)
            .OrderBy(g => g.Key)
            .ToList();
        Console.WriteLine($"[{testNum}] By status:");
        foreach (var g in statusGroups)
            Console.WriteLine($"    {g.Key}: {g.Count()} — {string.Join(", ", g.Select(r => r.EndpointName))}");

        // ===== Test 9: Multiple check rounds =====
        testNum++;
        var round2 = await checker.CheckAllAsync();
        var round3 = await checker.CheckAllAsync();
        var history = checker.GetHistory();
        Console.WriteLine($"[{testNum}] History after 3 rounds:");
        foreach (var kv in history.OrderBy(kv => kv.Key))
        {
            var avg = kv.Value.Average(r => (double)r.ResponseTimeMs);
            var healthyPct = (double)kv.Value.Count(r => r.Status == HealthStatus.Healthy) / kv.Value.Count * 100;
            Console.WriteLine($"    {kv.Key}: {kv.Value.Count} checks, avg={avg:F0}ms, healthy={healthyPct:F0}%");
        }

        // ===== Test 10: Response time percentiles =====
        testNum++;
        var allTimes = results.Select(r => r.ResponseTimeMs).OrderBy(t => t).ToList();
        var p50 = allTimes[allTimes.Count / 2];
        var p90 = allTimes[(int)(allTimes.Count * 0.9)];
        var max = allTimes.Max();
        var min = allTimes.Min();
        Console.WriteLine($"[{testNum}] Latency: min={min}ms, p50={p50}ms, p90={p90}ms, max={max}ms");

        // ===== Test 11: Markdown report generation =====
        testNum++;
        var markdown = report.GenerateMarkdown();
        var mdLines = markdown.Split('\n').Length;
        Console.WriteLine($"[{testNum}] Markdown report: {mdLines} lines, starts with '{markdown.Substring(0, Math.Min(50, markdown.Length))}'");

        // ===== Test 12: Exception handling =====
        testNum++;
        try
        {
            throw new HealthCheckException("BadEndpoint", "DNS resolution failed");
        }
        catch (HealthCheckException ex)
        {
            Console.WriteLine($"[{testNum}] Caught HealthCheckException: endpoint={ex.EndpointName}, msg={ex.Message}");
        }

        // ===== Test 13: Nullable and pattern matching =====
        testNum++;
        HealthCheckResult? nullResult = null;
        var firstHealthy = results.FirstOrDefault(r => r.Status == HealthStatus.Healthy);
        var firstUnhealthy = results.FirstOrDefault(r => r.Status == HealthStatus.Unhealthy);
        Console.WriteLine($"[{testNum}] Nullable: null={nullResult?.EndpointName ?? "(null)"}, " +
            $"healthy={firstHealthy?.EndpointName ?? "(none)"}, " +
            $"unhealthy={firstUnhealthy?.EndpointName ?? "(none)"}");

        // ===== Test 14: Enum operations =====
        testNum++;
        var statuses = Enum.GetValues<HealthStatus>();
        var statusNames = string.Join(", ", statuses.Select(s => $"{s}={(int)s}"));
        Console.WriteLine($"[{testNum}] HealthStatus values: {statusNames}");

        // ===== Test 15: Dictionary operations =====
        testNum++;
        var endpointLookup = endpoints.ToDictionary(e => e.Name, e => e);
        var hasApi = endpointLookup.ContainsKey("API Gateway");
        var hasMissing = endpointLookup.ContainsKey("NonExistent");
        endpointLookup.TryGetValue("Cache", out var cacheEp);
        Console.WriteLine($"[{testNum}] Lookup: hasApi={hasApi}, hasMissing={hasMissing}, " +
            $"cache={cacheEp?.Name ?? "(null)"}, cacheTimeout={cacheEp?.TimeoutMs ?? 0}");

        // ===== Test 16: LINQ aggregation =====
        testNum++;
        var totalTimeout = endpoints.Sum(e => e.TimeoutMs);
        var avgTimeout = endpoints.Average(e => (double)e.TimeoutMs);
        var maxTimeout = endpoints.Max(e => e.TimeoutMs);
        var minTimeout = endpoints.Min(e => e.TimeoutMs);
        Console.WriteLine($"[{testNum}] Timeouts: sum={totalTimeout}, avg={avgTimeout:F0}, max={maxTimeout}, min={minTimeout}");

        // ===== Test 17: Async with Task.WhenAll pattern =====
        testNum++;
        var tasks = endpoints.Take(3).Select(ep => checker.CheckEndpointAsync(ep)).ToArray();
        var parallelResults = await Task.WhenAll(tasks);
        Console.WriteLine($"[{testNum}] Parallel check: {parallelResults.Length} results, " +
            $"all done={parallelResults.All(r => r.Status != HealthStatus.Unknown)}");

        // ===== Test 18: String operations =====
        testNum++;
        var summary = new StringBuilder();
        summary.Append($"System: {appName}");
        summary.Append($" | Status: {report.OverallStatus}");
        summary.Append($" | Endpoints: {report.TotalChecks}");
        summary.Append($" | Healthy: {report.HealthyCount}/{report.TotalChecks}");
        var summaryStr = summary.ToString();
        Console.WriteLine($"[{testNum}] Summary: len={summaryStr.Length}, contains_name={summaryStr.Contains(appName!)}");
        Console.WriteLine($"    {summaryStr}");

        // ===== Test 19: Serilog structured logging =====
        testNum++;
        foreach (var r in results.Take(3))
        {
            if (r.Status == HealthStatus.Healthy)
                Log.Information("Check {Endpoint}: {Status} in {Latency}ms", r.EndpointName, r.Status, r.ResponseTimeMs);
            else if (r.Status == HealthStatus.Degraded)
                Log.Warning("Check {Endpoint}: {Status} in {Latency}ms", r.EndpointName, r.Status, r.ResponseTimeMs);
            else
                Log.Error("Check {Endpoint}: {Status} — {Error}", r.EndpointName, r.Status, r.ErrorMessage);
        }
        Console.WriteLine($"[{testNum}] Serilog messages logged");

        // ===== Test 20: Final summary =====
        testNum++;
        var unhealthyNames = results.Where(r => r.Status == HealthStatus.Unhealthy)
            .Select(r => r.EndpointName).ToList();
        var degradedNames = results.Where(r => r.Status == HealthStatus.Degraded)
            .Select(r => r.EndpointName).ToList();
        Console.WriteLine($"[{testNum}] Final: total={results.Count}, " +
            $"unhealthy=[{string.Join(", ", unhealthyNames)}], " +
            $"degraded=[{string.Join(", ", degradedNames)}]");

        Log.CloseAndFlush();
        Console.WriteLine("=== HealthChecker complete ===");
    }
}
