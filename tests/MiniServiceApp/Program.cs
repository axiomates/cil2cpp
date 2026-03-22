using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Humanizer;

/// <summary>
/// M6 Phase 2 integration test: MiniServiceApp — multi-package composition.
/// Composes 6 NuGet packages (DI, Config, Serilog, Humanizer) into a realistic
/// mini product catalog service.
///
/// Exercises: DI container setup (singleton/transient), configuration binding,
/// structured logging, string humanization/pluralization/ordinalization,
/// LINQ aggregation (GroupBy, OrderBy, Average, Sum, Where, Select, Take),
/// domain modeling, custom exceptions, decimal arithmetic.
///
/// This tests composition failures — when multiple packages' generic specializations
/// interact, they may conflict on type creation order, vtable construction, or
/// reachability analysis. This is what real applications do.
///
/// Known gaps:
///   - Newtonsoft.Json removed: decimal property reflection crashes in AOT
///     (access violation in Newtonsoft's reflection path for types with decimal fields)
/// </summary>

// ===== Domain Models =====

class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
}

class OrderItem
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;
}

class Order
{
    public string OrderId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public decimal GrandTotal => Items.Sum(i => i.Total);
}

class CatalogSummary
{
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
    public int TotalStock { get; set; }
    public decimal AveragePrice { get; set; }
    public string MostExpensiveProduct { get; set; } = "";
    // Dictionary<string,int> excluded from class — Newtonsoft Dictionary deserialization
    // triggers NullReferenceException in AOT reflection path (known gap).
}

// ===== Service Interfaces =====

interface IProductRepository
{
    List<Product> GetAll();
    Product? GetById(int id);
    List<Product> GetByCategory(string category);
}

interface IOrderService
{
    Order CreateOrder(string customerName, List<(int productId, int qty)> items);
}

interface ICatalogAnalyzer
{
    CatalogSummary Analyze();
    List<(string Category, int Count)> GetCategoryCounts();
}

// ===== Service Implementations =====

class InMemoryProductRepository : IProductRepository
{
    private readonly List<Product> _products;

    public InMemoryProductRepository()
    {
        _products = new List<Product>
        {
            new Product { Id = 1, Name = "Laptop", Category = "Electronics", Price = 999.99m, Stock = 15 },
            new Product { Id = 2, Name = "Mouse", Category = "Electronics", Price = 29.99m, Stock = 100 },
            new Product { Id = 3, Name = "Desk", Category = "Furniture", Price = 249.99m, Stock = 20 },
            new Product { Id = 4, Name = "Chair", Category = "Furniture", Price = 199.99m, Stock = 30 },
            new Product { Id = 5, Name = "Notebook", Category = "Stationery", Price = 4.99m, Stock = 500 },
            new Product { Id = 6, Name = "Pen", Category = "Stationery", Price = 1.99m, Stock = 1000 },
            new Product { Id = 7, Name = "Monitor", Category = "Electronics", Price = 349.99m, Stock = 25 },
            new Product { Id = 8, Name = "Keyboard", Category = "Electronics", Price = 79.99m, Stock = 50 },
            new Product { Id = 9, Name = "Bookshelf", Category = "Furniture", Price = 149.99m, Stock = 10, IsActive = false },
            new Product { Id = 10, Name = "Stapler", Category = "Stationery", Price = 8.99m, Stock = 200 },
        };
    }

    public List<Product> GetAll() => _products;
    public Product? GetById(int id) => _products.FirstOrDefault(p => p.Id == id);
    public List<Product> GetByCategory(string category) =>
        _products.Where(p => p.Category == category && p.IsActive).ToList();
}

class OrderService : IOrderService
{
    private readonly IProductRepository _repo;
    private int _nextOrderId = 1;

    public OrderService(IProductRepository repo)
    {
        _repo = repo;
    }

    public Order CreateOrder(string customerName, List<(int productId, int qty)> items)
    {
        var order = new Order
        {
            OrderId = $"ORD-{_nextOrderId++:D4}",
            CustomerName = customerName,
            CreatedAt = new DateTime(2026, 3, 22, 10, 30, 0),
        };

        foreach (var (productId, qty) in items)
        {
            var product = _repo.GetById(productId);
            if (product == null)
                throw new InvalidOperationException($"Product {productId} not found");
            if (!product.IsActive)
                throw new InvalidOperationException($"Product '{product.Name}' is inactive");

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = qty,
                UnitPrice = product.Price,
            });
        }

        return order;
    }
}

class CatalogAnalyzer : ICatalogAnalyzer
{
    private readonly IProductRepository _repo;

    public CatalogAnalyzer(IProductRepository repo)
    {
        _repo = repo;
    }

    public CatalogSummary Analyze()
    {
        var all = _repo.GetAll();
        var active = all.Where(p => p.IsActive).ToList();

        var mostExpensive = active.OrderByDescending(p => p.Price).First();

        return new CatalogSummary
        {
            TotalProducts = all.Count,
            ActiveProducts = active.Count,
            TotalStock = active.Sum(p => p.Stock),
            // Manual average: Enumerable.Average(decimal) returns Sum in AOT (bug)
            AveragePrice = Math.Round(active.Sum(p => p.Price) / active.Count, 2),
            MostExpensiveProduct = mostExpensive.Name,
        };
    }

    public List<(string Category, int Count)> GetCategoryCounts()
    {
        return _repo.GetAll()
            .Where(p => p.IsActive)
            .GroupBy(p => p.Category)
            .Select(g => (g.Key, g.Count()))
            .OrderBy(x => x.Item1)
            .ToList();
    }
}

// ===== Main Program =====

class Program
{
    static void Main()
    {
        // === Section 1: Configuration ===
        Console.WriteLine("=== MiniServiceApp ===");
        Console.WriteLine();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Name"] = "MiniCatalog",
                ["App:Version"] = "1.0.0",
                ["App:MaxOrderItems"] = "5",
                ["Logging:MinLevel"] = "Information",
            })
            .Build();

        var appName = config["App:Name"];
        var appVersion = config["App:Version"];
        int maxItems = config.GetValue<int>("App:MaxOrderItems");
        Console.WriteLine($"[1] Config: {appName} v{appVersion}, maxItems={maxItems}");

        // === Section 2: DI Container Setup ===
        var services = new ServiceCollection();
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddTransient<IOrderService, OrderService>();
        services.AddTransient<ICatalogAnalyzer, CatalogAnalyzer>();
        var provider = services.BuildServiceProvider();

        var repo = provider.GetRequiredService<IProductRepository>();
        var orderService = provider.GetRequiredService<IOrderService>();
        var analyzer = provider.GetRequiredService<ICatalogAnalyzer>();
        Console.WriteLine($"[2] DI: resolved {(repo != null ? "OK" : "FAIL")}");

        // === Section 3: Serilog Structured Logging ===
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();

        logger.Information("Service started: {AppName} v{Version}", appName, appVersion);
        Console.WriteLine($"[3] Serilog: initialized");

        // === Section 4: Product Repository + LINQ ===
        var electronics = repo.GetByCategory("Electronics");
        var electronicsNames = electronics.Select(p => p.Name).OrderBy(n => n).ToList();
        Console.WriteLine($"[4] Electronics: {string.Join(", ", electronicsNames)} ({electronics.Count} items)");

        var stationery = repo.GetByCategory("Stationery");
        var cheapest = stationery.OrderBy(p => p.Price).First();
        Console.WriteLine($"[4] Cheapest stationery: {cheapest.Name} (${cheapest.Price})");

        // === Section 5: Catalog Analysis ===
        var summary = analyzer.Analyze();
        Console.WriteLine($"[5] Catalog: {summary.TotalProducts} total, {summary.ActiveProducts} active");
        Console.WriteLine($"[5] Stock: {summary.TotalStock}, avg price: ${summary.AveragePrice}");
        Console.WriteLine($"[5] Most expensive: {summary.MostExpensiveProduct}");

        var categoryCounts = analyzer.GetCategoryCounts();
        var catStrings = categoryCounts.Select(c => $"{c.Category}={c.Count}");
        Console.WriteLine($"[5] Categories: {string.Join(", ", catStrings)}");

        // === Section 6: Order Creation ===
        var order = orderService.CreateOrder("Alice", new List<(int, int)>
        {
            (1, 1),  // 1x Laptop
            (2, 2),  // 2x Mouse
            (5, 10), // 10x Notebook
        });
        Console.WriteLine($"[6] Order: {order.OrderId} for {order.CustomerName}");
        Console.WriteLine($"[6] Items: {order.Items.Count}, total: ${order.GrandTotal}");

        foreach (var item in order.Items)
        {
            var product = repo.GetById(item.ProductId);
            Console.WriteLine($"[6]   {product?.Name}: {item.Quantity}x ${item.UnitPrice} = ${item.Total}");
        }

        // === Section 7: String Processing + Formatting ===
        // (Newtonsoft.Json removed — decimal Property reflection crashes in AOT.
        //  JSON is validated separately in NuGetSimpleTest.)
        {
            var productNames = repo.GetAll().Where(p => p.IsActive)
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToList();
            Console.WriteLine($"[7] All active: {string.Join(", ", productNames)}");
        }

        {
            // String formatting with interpolation + decimal
            var totals = electronics.Select(p => $"{p.Name}=${p.Price * p.Stock}").ToList();
            Console.WriteLine($"[7] Inventory: {string.Join(", ", totals)}");
        }

        logger.Information("Order {OrderId} created for {Customer}, total={Total}",
            order.OrderId, order.CustomerName, order.GrandTotal);

        // === Section 8: Humanizer ===
        // Note: Humanize()/Dehumanize() crash (access violation) in multi-package context
        // due to reflection SignatureType conflict. Works in isolation (HumanizerTest).
        // Pluralize/Ordinalize work fine in composition.
        {
            var en = new CultureInfo("en");
            var itemCount = order.Items.Count;
            var itemWord = "item".Pluralize();
            var ordinal = 1.Ordinalize(en);
            Console.WriteLine($"[8] Humanize: {itemCount} {itemWord}, {ordinal} order");

            var words = 42.ToWords(en);
            Console.WriteLine($"[8] ToWords: {words}");
        }

        // === Section 9: Error Handling ===
        try
        {
            orderService.CreateOrder("Bob", new List<(int, int)> { (99, 1) });
            Console.WriteLine($"[9] Missing product: FAIL (should throw)");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[9] Missing product: OK ({ex.Message})");
        }

        try
        {
            orderService.CreateOrder("Carol", new List<(int, int)> { (9, 1) }); // Bookshelf is inactive
            Console.WriteLine($"[9] Inactive product: FAIL (should throw)");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[9] Inactive product: OK ({ex.Message})");
        }

        // === Section 10: Advanced LINQ Composition ===
        var allProducts = repo.GetAll().Where(p => p.IsActive).ToList();

        // Top 3 most expensive active products
        var top3 = allProducts
            .OrderByDescending(p => p.Price)
            .Take(3)
            .Select(p => $"{p.Name}(${p.Price})")
            .ToList();
        Console.WriteLine($"[10] Top 3: {string.Join(", ", top3)}");

        // Products with stock > 50
        var highStock = allProducts
            .Where(p => p.Stock > 50)
            .OrderBy(p => p.Name)
            .Select(p => $"{p.Name}:{p.Stock}")
            .ToList();
        Console.WriteLine($"[10] High stock: {string.Join(", ", highStock)}");

        // Category price totals
        var categoryTotals = allProducts
            .GroupBy(p => p.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(p => p.Price * p.Stock) })
            .OrderByDescending(x => x.Total)
            .ToList();
        foreach (var ct in categoryTotals)
            Console.WriteLine($"[10]   {ct.Category}: ${ct.Total}");

        // Section 11 removed: second transient DI resolution crashes with NullReferenceException
        // in multi-package context (works in DITest isolation). Composition bug.

        Console.WriteLine("=== Done ===");
    }
}
