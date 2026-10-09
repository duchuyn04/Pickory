namespace Pickory.Modules.Catalog.Specs;

public sealed record SpecValidationResult(IReadOnlyList<SpecError> Errors)
{
    public bool Valid => Errors.Count == 0;
}
