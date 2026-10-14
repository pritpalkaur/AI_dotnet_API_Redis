using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProductApi.Data;
using ProductApi.Dtos;
using ProductApi.Models;

namespace ProductApi.Services;

public record Cached<T>(T Value, bool FromCache);

public class DuplicateSkuException(string sku) : Exception($"A product with SKU '{sku}' already exists.");

public interface IProductService
{
    Task<Cached<PagedResponse<ProductResponse>>> ListAsync(int page, int pageSize, string? category, CancellationToken ct);
    Task<Cached<ProductResponse>?> GetAsync(int id, CancellationToken ct);
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct);
    Task<ProductResponse?> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}

/// <summary>
/// Cache-aside over SQL Server. SQL is the source of truth; Redis holds
///   product:{id}                          – single product
///   products:list:{version}:{filter/page} – list pages
/// List pages are invalidated by rotating products:list:version on every write,
/// so no key scanning is needed and every API instance sees the change at once.
/// </summary>
public class ProductService(AppDbContext db, IAppCache cache) : IProductService
{
    private static readonly TimeSpan ItemTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(2);
    private const string ListVersionKey = "products:list:version";

    private static string ItemKey(int id) => $"product:{id}";

    public async Task<Cached<PagedResponse<ProductResponse>>> ListAsync(int page, int pageSize, string? category, CancellationToken ct)
    {
        var version = await cache.GetAsync<string>(ListVersionKey, ct);
        if (version is null)
        {
            version = Guid.NewGuid().ToString("N");
            await cache.SetAsync(ListVersionKey, version, TimeSpan.FromDays(1), ct);
        }

        var key = $"products:list:{version}:{category?.ToLowerInvariant() ?? "*"}:{page}:{pageSize}";
        var hit = await cache.GetAsync<PagedResponse<ProductResponse>>(key, ct);
        if (hit is not null) return new(hit, true);

        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => ProductResponse.From(p))
            .ToListAsync(ct);

        var result = new PagedResponse<ProductResponse>(items, page, pageSize, total);
        await cache.SetAsync(key, result, ListTtl, ct);
        return new(result, false);
    }

    public async Task<Cached<ProductResponse>?> GetAsync(int id, CancellationToken ct)
    {
        var hit = await cache.GetAsync<ProductResponse>(ItemKey(id), ct);
        if (hit is not null) return new(hit, true);

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;

        var dto = ProductResponse.From(product);
        await cache.SetAsync(ItemKey(id), dto, ItemTtl, ct);
        return new(dto, false);
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Sku = request.Sku.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description,
            Category = request.Category?.Trim(),
            Price = request.Price,
            Quantity = request.Quantity,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Products.Add(product);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateSkuException(product.Sku);
        }

        var dto = ProductResponse.From(product);
        await cache.SetAsync(ItemKey(product.Id), dto, ItemTtl, ct);
        await InvalidateListsAsync(ct);
        return dto;
    }

    public async Task<ProductResponse?> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;

        product.Name = request.Name.Trim();
        product.Description = request.Description;
        product.Category = request.Category?.Trim();
        product.Price = request.Price;
        product.Quantity = request.Quantity;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Remove rather than overwrite so a concurrent stale read can't win the race.
        await cache.RemoveAsync(ItemKey(id), ct);
        await InvalidateListsAsync(ct);
        return ProductResponse.From(product);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return false;

        await cache.RemoveAsync(ItemKey(id), ct);
        await InvalidateListsAsync(ct);
        return true;
    }

    private Task InvalidateListsAsync(CancellationToken ct) =>
        cache.SetAsync(ListVersionKey, Guid.NewGuid().ToString("N"), TimeSpan.FromDays(1), ct);

    // 2601 = duplicate key row in unique index, 2627 = unique constraint violation
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
