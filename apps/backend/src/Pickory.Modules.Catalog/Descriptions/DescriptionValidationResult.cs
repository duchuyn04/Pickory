namespace Pickory.Modules.Catalog.Descriptions;

public sealed record DescriptionValidationResult(IReadOnlyList<string> Errors)
{
    public bool Valid => Errors.Count == 0;
}
