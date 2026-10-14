using System.ComponentModel.DataAnnotations;
using ProductApi.Models;

namespace ProductApi.Dtos;

public record CreateProductRequest(
    [Required, StringLength(50, MinimumLength = 1)] string Sku,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(2000)] string? Description,
    [StringLength(100)] string? Category,
    [Range(typeof(decimal), "0", "999999999")] decimal Price,
    [Range(0, int.MaxValue)] int Quantity);

public record UpdateProductRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(2000)] string? Description,
    [StringLength(100)] string? Category,
    [Range(typeof(decimal), "0", "999999999")] decimal Price,
    [Range(0, int.MaxValue)] int Quantity);

public record ProductResponse(
    int Id,
    string Sku,
    string Name,
    string? Description,
    string? Category,
    decimal Price,
    int Quantity,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ProductResponse From(Product p) =>
        new(p.Id, p.Sku, p.Name, p.Description, p.Category, p.Price, p.Quantity, p.CreatedAtUtc, p.UpdatedAtUtc);
}

public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
