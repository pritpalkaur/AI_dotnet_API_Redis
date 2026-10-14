using Microsoft.AspNetCore.Mvc;
using ProductApi.Dtos;
using ProductApi.Services;

namespace ProductApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProductsController(IProductService products) : ControllerBase
{
    /// <summary>List products (paged, optionally filtered by category).</summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1–100).</param>
    /// <param name="category">Exact category match, e.g. Peripherals. Leave empty for all.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<PagedResponse<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ProductResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await products.ListAsync(page, pageSize, category, ct);
        SetCacheHeader(result.FromCache);
        return Ok(result.Value);
    }

    /// <summary>Get one product by id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Get(int id, CancellationToken ct)
    {
        var result = await products.GetAsync(id, ct);
        if (result is null) return NotFound();

        SetCacheHeader(result.FromCache);
        return Ok(result.Value);
    }

    /// <summary>Insert a new product.</summary>
    /// <remarks>SKU must be unique; a duplicate returns 409 Conflict.</remarks>
    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken ct)
    {
        try
        {
            var created = await products.CreateAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (DuplicateSkuException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>Replace a product's editable fields (SKU is immutable).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Update(int id, UpdateProductRequest request, CancellationToken ct)
    {
        var updated = await products.UpdateAsync(id, request, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Delete a product.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await products.DeleteAsync(id, ct) ? NoContent() : NotFound();

    // Lets clients (and you, while learning) see whether Redis served the response.
    private void SetCacheHeader(bool fromCache) => Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";
}
