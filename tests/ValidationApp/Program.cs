using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// ============================================================
// ValidationApp — Feature Composition Test
//
// Tests features working TOGETHER in realistic patterns,
// not individual features in isolation.
// Each section is independently try/catch'd for partial results.
// ============================================================

// --- Domain model: interfaces + generics + inheritance ---

interface IEntity
{
    int Id { get; }
    string Name { get; }
}

interface IRepository<T> where T : IEntity
{
    void Add(T item);
    T? FindById(int id);
    IEnumerable<T> GetAll();
    IEnumerable<T> Where(Func<T, bool> predicate);
    int Count { get; }
}

class Entity : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public override string ToString() => $"{GetType().Name}({Id}, {Name})";
}

class Product : Entity
{
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

class Order : Entity
{
    public List<OrderLine> Lines { get; set; } = new();
    public decimal Total => Lines.Sum(l => l.Quantity * l.UnitPrice);
}

class OrderLine
{
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

// Generic repository: generics + collections + LINQ + interfaces
class InMemoryRepository<T> : IRepository<T> where T : IEntity
{
    private readonly Dictionary<int, T> _store = new();

    public void Add(T item) => _store[item.Id] = item;
    public T? FindById(int id) => _store.TryGetValue(id, out var item) ? item : default;
    public IEnumerable<T> GetAll() => _store.Values;
    public IEnumerable<T> Where(Func<T, bool> predicate) => _store.Values.Where(predicate);
    public int Count => _store.Count;
}

// Custom exceptions: inheritance + string formatting
class AppException : Exception
{
    public string Code { get; }
    public AppException(string code, string message) : base(message) { Code = code; }
}

class NotFoundException : AppException
{
    public NotFoundException(string entity, int id)
        : base("NOT_FOUND", $"{entity} with ID {id} not found") { }
}

class ValidationException : AppException
{
    public List<string> Errors { get; }
    public ValidationException(List<string> errors)
        : base("VALIDATION", $"{errors.Count} validation error(s)")
    {
        Errors = errors;
    }
}

// Event system: delegates + events + lambdas
class EventBus
{
    public event Action<string>? OnEvent;
    private readonly List<string> _log = new();

    public void Publish(string eventName)
    {
        _log.Add(eventName);
        OnEvent?.Invoke(eventName);
    }

    public IReadOnlyList<string> GetLog() => _log;
}

// Builder pattern: fluent API + generics + nullable
class QueryBuilder<T> where T : IEntity
{
    private readonly IRepository<T> _repo;
    private Func<T, bool>? _filter;
    private Func<T, object>? _orderBy;
    private int? _limit;

    public QueryBuilder(IRepository<T> repo) => _repo = repo;

    public QueryBuilder<T> Filter(Func<T, bool> predicate)
    {
        _filter = predicate;
        return this;
    }

    public QueryBuilder<T> OrderBy(Func<T, object> keySelector)
    {
        _orderBy = keySelector;
        return this;
    }

    public QueryBuilder<T> Take(int count)
    {
        _limit = count;
        return this;
    }

    public List<T> Execute()
    {
        IEnumerable<T> result = _filter != null ? _repo.Where(_filter) : _repo.GetAll();
        if (_orderBy != null)
            result = result.OrderBy(_orderBy);
        if (_limit.HasValue)
            result = result.Take(_limit.Value);
        return result.ToList();
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("=== ValidationApp ===");

        // Section 1: Repository + LINQ + Generics + String Interpolation
        try
        {
            var products = new InMemoryRepository<Product>();
            products.Add(new Product { Id = 1, Name = "Laptop", Price = 999.99m, Category = "Electronics" });
            products.Add(new Product { Id = 2, Name = "Mouse", Price = 29.99m, Category = "Electronics" });
            products.Add(new Product { Id = 3, Name = "Desk", Price = 249.99m, Category = "Furniture" });
            products.Add(new Product { Id = 4, Name = "Chair", Price = 199.99m, Category = "Furniture" });
            products.Add(new Product { Id = 5, Name = "Monitor", Price = 449.99m, Category = "Electronics" });

            // LINQ + lambda + generics composition
            var electronics = products.Where(p => p.Category == "Electronics").ToList();
            var totalElectronics = electronics.Sum(p => p.Price);
            Console.WriteLine($"[1] Electronics: {electronics.Count} items, total={totalElectronics}");

            // QueryBuilder: fluent API + nullable + generics
            var topProducts = new QueryBuilder<Product>(products)
                .Filter(p => p.Price > 100)
                .OrderBy(p => p.Name)
                .Take(3)
                .Execute();
            var names = string.Join(", ", topProducts.Select(p => p.Name));
            Console.WriteLine($"[1] Top products: {names}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[1] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 2: Custom exceptions + inheritance + catch hierarchy
        try
        {
            var repo = new InMemoryRepository<Product>();
            try
            {
                var item = repo.FindById(99);
                if (item == null)
                    throw new NotFoundException("Product", 99);
                Console.WriteLine("[2] FAIL: should have thrown");
            }
            catch (NotFoundException nfe)
            {
                Console.WriteLine($"[2] Caught: {nfe.Code} - {nfe.Message}");
            }

            // Validation exception with list of errors
            try
            {
                var errors = new List<string> { "Name required", "Price must be positive" };
                throw new ValidationException(errors);
            }
            catch (AppException ae) when (ae.Code == "VALIDATION")
            {
                var ve = (ValidationException)ae;
                Console.WriteLine($"[2] Validation: {ve.Errors.Count} errors - {string.Join("; ", ve.Errors)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[2] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 3: Events + delegates + lambda closures
        try
        {
            var bus = new EventBus();
            var received = new List<string>();
            bus.OnEvent += name => received.Add($"Handler1:{name}");
            bus.OnEvent += name => received.Add($"Handler2:{name}");

            bus.Publish("OrderCreated");
            bus.Publish("OrderShipped");

            Console.WriteLine($"[3] Events: {bus.GetLog().Count} published, {received.Count} handled");
            Console.WriteLine($"[3] Log: {string.Join(", ", bus.GetLog())}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[3] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 4: Order with computed properties + LINQ aggregation
        try
        {
            var orders = new InMemoryRepository<Order>();
            var order1 = new Order
            {
                Id = 1, Name = "Order-001",
                Lines = new List<OrderLine>
                {
                    new() { ProductName = "Laptop", Quantity = 1, UnitPrice = 999.99m },
                    new() { ProductName = "Mouse", Quantity = 2, UnitPrice = 29.99m },
                }
            };
            var order2 = new Order
            {
                Id = 2, Name = "Order-002",
                Lines = new List<OrderLine>
                {
                    new() { ProductName = "Desk", Quantity = 1, UnitPrice = 249.99m },
                }
            };
            orders.Add(order1);
            orders.Add(order2);

            // Computed property + LINQ
            var allTotals = orders.GetAll().Select(o => o.Total).ToList();
            var grandTotal = allTotals.Sum();
            Console.WriteLine($"[4] Orders: {orders.Count}, totals=[{string.Join(", ", allTotals)}]");
            Console.WriteLine($"[4] Grand total: {grandTotal}");

            // Nested LINQ: flatten order lines
            var allLines = orders.GetAll().SelectMany(o => o.Lines).ToList();
            var totalItems = allLines.Sum(l => l.Quantity);
            Console.WriteLine($"[4] Total line items: {allLines.Count}, total quantity: {totalItems}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[4] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 5: Async + Task composition + exception handling
        try
        {
            RunAsyncTests().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[5] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 6: Dictionary + GroupBy + complex LINQ chains
        try
        {
            var items = new List<(string Category, string Name, int Score)>
            {
                ("A", "Alpha", 90),
                ("B", "Beta", 85),
                ("A", "Gamma", 75),
                ("B", "Delta", 95),
                ("A", "Epsilon", 80),
            };

            // GroupBy + aggregate + LINQ chaining
            var grouped = items
                .GroupBy(x => x.Category)
                .Select(g => new { Category = g.Key, Avg = g.Average(x => x.Score), Count = g.Count() })
                .OrderBy(g => g.Category)
                .ToList();

            foreach (var g in grouped)
                Console.WriteLine($"[6] {g.Category}: avg={g.Avg}, count={g.Count}");

            // Dictionary from LINQ
            var dict = items.ToDictionary(x => x.Name, x => x.Score);
            var maxEntry = dict.MaxBy(kv => kv.Value);
            Console.WriteLine($"[6] Max: {maxEntry.Key}={maxEntry.Value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[6] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 7: String operations + StringBuilder + formatting
        try
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= 5; i++)
                sb.Append($"[{i}]");
            Console.WriteLine($"[7] Builder: {sb}");

            // String.Join + LINQ + Select
            var words = new[] { "hello", "world", "from", "validation" };
            var upper = string.Join(" ", words.Select(w => w.ToUpper()));
            Console.WriteLine($"[7] Upper: {upper}");

            // Padding and formatting
            var formatted = $"{"Name",-10}{"Score",8}";
            Console.WriteLine($"[7] Format: [{formatted}]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[7] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 8: Nullable<T> + pattern matching + value types
        try
        {
            int? nullableVal = 42;
            int? nullableEmpty = null;

            string Describe(int? v) => v switch
            {
                null => "empty",
                < 0 => "negative",
                0 => "zero",
                > 0 and < 100 => $"small({v})",
                _ => $"large({v})",
            };

            Console.WriteLine($"[8] {Describe(nullableVal)}, {Describe(nullableEmpty)}, {Describe(-5)}, {Describe(0)}, {Describe(999)}");

            // Nullable with LINQ
            var values = new int?[] { 1, null, 3, null, 5 };
            var nonNull = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            Console.WriteLine($"[8] NonNull: [{string.Join(", ", nonNull)}]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[8] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 9: IDisposable + using + resource management
        try
        {
            var log = new List<string>();
            using (var res = new TrackedResource("R1", log))
            {
                res.DoWork();
            }
            // Nested using
            using (var outer = new TrackedResource("Outer", log))
            {
                using (var inner = new TrackedResource("Inner", log))
                {
                    inner.DoWork();
                }
                outer.DoWork();
            }
            Console.WriteLine($"[9] Resource log: {string.Join(", ", log)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[9] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        // Section 10: Generic constraints + interface dispatch + covariance
        try
        {
            IReadOnlyList<Product> products = new List<Product>
            {
                new() { Id = 1, Name = "A", Price = 10m },
                new() { Id = 2, Name = "B", Price = 20m },
                new() { Id = 3, Name = "C", Price = 30m },
            };

            // Generic method with constraint
            static string Summarize<T>(IEnumerable<T> items) where T : IEntity
                => string.Join(", ", items.Select(i => i.Name));

            Console.WriteLine($"[10] Summary: {Summarize(products)}");

            // Interface covariance: IEnumerable<Product> → IEnumerable<IEntity>
            IEnumerable<IEntity> entities = products;
            var idSum = entities.Sum(e => e.Id);
            Console.WriteLine($"[10] ID sum: {idSum}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[10] FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("=== Done ===");
    }

    // Async section: Task composition + exception handling
    static async Task RunAsyncTests()
    {
        // Sequential async computations (avoids parallel state machine race)
        var r1 = await ComputeAsync("Fast", 10);
        var r2 = await ComputeAsync("Medium", 20);
        var r3 = await ComputeAsync("Slow", 30);
        Console.WriteLine($"[5] Async results: [{r1}, {r2}, {r3}]");

        // Async with exception handling
        try
        {
            await FailingAsync();
            Console.WriteLine("[5] FAIL: should have thrown");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[5] Caught async: {ex.Message}");
        }

        // Task.FromResult + LINQ
        var ids = new[] { 1, 2, 3 };
        var tasks = ids.Select(id => Task.FromResult("Item-" + id));
        var items = await Task.WhenAll(tasks);
        Console.WriteLine($"[5] Async LINQ: [{string.Join(", ", items)}]");
    }

    static async Task<string> ComputeAsync(string name, int value)
    {
        await Task.Yield();
        return name + "=" + (value * 2);
    }

    static async Task FailingAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("async failure test");
    }
}

// IDisposable resource tracker
class TrackedResource : IDisposable
{
    private readonly string _name;
    private readonly List<string> _log;

    public TrackedResource(string name, List<string> log)
    {
        _name = name;
        _log = log;
        _log.Add($"{_name}:created");
    }

    public void DoWork() => _log.Add($"{_name}:work");

    public void Dispose() => _log.Add($"{_name}:disposed");
}
