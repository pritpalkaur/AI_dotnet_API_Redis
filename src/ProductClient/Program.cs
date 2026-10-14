using System.Globalization;
using System.Net;
using System.Net.Http.Json;

// Usage:
//   dotnet run                         -> summary report of all products
//   dotnet run -- list [category]      -> table of products
//   dotnet run -- get <id>             -> one product
//   dotnet run -- add <sku> <name> <price> <qty> [category]
// Set PRODUCT_API_URL to point somewhere other than http://localhost:5170.

var baseUrl = Environment.GetEnvironmentVariable("PRODUCT_API_URL") ?? "http://localhost:5170";
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

try
{
    var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "summary";
    switch (command)
    {
        case "summary":
            PrintSummary(await FetchAllAsync(http, null));
            break;
        case "list":
            PrintTable(await FetchAllAsync(http, args.ElementAtOrDefault(1)));
            break;
        case "get" when args.Length == 2 && int.TryParse(args[1], out var id):
            await GetAsync(http, id);
            break;
        case "add" when args.Length >= 5:
            await AddAsync(http, args);
            break;
        default:
            Console.WriteLine("Commands: summary | list [category] | get <id> | add <sku> <name> <price> <qty> [category]");
            return 1;
    }
    return 0;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach the API at {baseUrl}: {ex.Message}");
    return 2;
}

static async Task<List<Product>> FetchAllAsync(HttpClient http, string? category)
{
    var all = new List<Product>();
    for (var page = 1; ; page++)
    {
        var url = $"api/products?page={page}&pageSize=100";
        if (!string.IsNullOrWhiteSpace(category)) url += $"&category={Uri.EscapeDataString(category)}";

        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Paged>() ?? throw new InvalidOperationException("Empty response");
        Console.Error.WriteLine($"[page {page}: X-Cache {CacheHeader(response)}]");

        all.AddRange(body.Items);
        if (body.Items.Count == 0 || all.Count >= body.TotalCount) return all;
    }
}

static async Task GetAsync(HttpClient http, int id)
{
    using var response = await http.GetAsync($"api/products/{id}");
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        Console.WriteLine($"Product {id} not found.");
        return;
    }
    response.EnsureSuccessStatusCode();
    var p = (await response.Content.ReadFromJsonAsync<Product>())!;

    Console.WriteLine($"""
        #{p.Id}  {p.Name}   (X-Cache: {CacheHeader(response)})
          SKU:         {p.Sku}
          Category:    {p.Category ?? "-"}
          Price:       {p.Price:C}
          Quantity:    {p.Quantity}
          Stock value: {p.Price * p.Quantity:C}
          Description: {p.Description ?? "-"}
          Created:     {p.CreatedAtUtc:u}
          Updated:     {p.UpdatedAtUtc:u}
        """);
}

static async Task AddAsync(HttpClient http, string[] args)
{
    var request = new
    {
        sku = args[1],
        name = args[2],
        price = decimal.Parse(args[3], CultureInfo.InvariantCulture),
        quantity = int.Parse(args[4], CultureInfo.InvariantCulture),
        category = args.ElementAtOrDefault(5),
    };
    using var response = await http.PostAsJsonAsync("api/products", request);
    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"Insert failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        return;
    }
    var created = (await response.Content.ReadFromJsonAsync<Product>())!;
    Console.WriteLine($"Created product #{created.Id} at {response.Headers.Location}");
}

static void PrintTable(List<Product> products)
{
    Console.WriteLine($"{"Id",5}  {"SKU",-12} {"Name",-30} {"Category",-15} {"Price",10} {"Qty",6}");
    Console.WriteLine(new string('-', 83));
    foreach (var p in products)
        Console.WriteLine($"{p.Id,5}  {Trunc(p.Sku, 12),-12} {Trunc(p.Name, 30),-30} {Trunc(p.Category ?? "-", 15),-15} {p.Price,10:N2} {p.Quantity,6}");
    Console.WriteLine($"{products.Count} product(s)");
}

static void PrintSummary(List<Product> products)
{
    Console.WriteLine("=== Product inventory summary ===");
    if (products.Count == 0)
    {
        Console.WriteLine("No products yet. Try: dotnet run -- add KB-001 \"Keyboard\" 89.99 25 Peripherals");
        return;
    }

    Console.WriteLine($"Products:        {products.Count}");
    Console.WriteLine($"Units in stock:  {products.Sum(p => p.Quantity)}");
    Console.WriteLine($"Inventory value: {products.Sum(p => p.Price * p.Quantity):C}");
    Console.WriteLine($"Average price:   {products.Average(p => p.Price):C}");
    Console.WriteLine();

    Console.WriteLine("By category:");
    foreach (var g in products.GroupBy(p => p.Category ?? "(none)").OrderByDescending(g => g.Sum(p => p.Price * p.Quantity)))
        Console.WriteLine($"  {g.Key,-20} {g.Count(),4} products {g.Sum(p => p.Quantity),7} units {g.Sum(p => p.Price * p.Quantity),14:C}");

    var lowStock = products.Where(p => p.Quantity < 5).OrderBy(p => p.Quantity).ToList();
    Console.WriteLine();
    Console.WriteLine(lowStock.Count == 0 ? "Low stock (<5): none" : "Low stock (<5):");
    foreach (var p in lowStock)
        Console.WriteLine($"  #{p.Id} {p.Name} ({p.Sku}) - {p.Quantity} left");
}

static string CacheHeader(HttpResponseMessage r) =>
    r.Headers.TryGetValues("X-Cache", out var v) ? v.First() : "n/a";

static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

record Product(int Id, string Sku, string Name, string? Description, string? Category,
    decimal Price, int Quantity, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

record Paged(List<Product> Items, int Page, int PageSize, int TotalCount);
