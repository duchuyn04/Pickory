namespace Pickory.Modules.Catalog.Attributes;

public sealed record AttributeValidationResult(IReadOnlyList<AttributeError> Errors)
{
    public bool Valid => Errors.Count == 0;
}
