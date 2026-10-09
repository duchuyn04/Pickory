namespace Pickory.Modules.Catalog.Products;

public sealed record PriceValidationResult(string? Error)
{
    public bool Valid => Error is null;
}
