namespace Pickory.Modules.AiGateway.Entities;

public sealed class AiEmbeddingCache
{
    public string Id { get; set; } = null!;
    public required string Key { get; set; }
    public required string Model { get; set; }
    // `reference-schema.sql` cho phép NULL. Không đổi cột thành `vector` hoặc bắt buộc giá trị.
    public double[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; }
}
