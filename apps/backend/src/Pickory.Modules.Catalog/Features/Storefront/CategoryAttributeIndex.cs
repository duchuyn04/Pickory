using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Persistence;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Bảng danh mục nạp một lần cho mỗi lần đọc. Chi tiết, so sánh và `ICatalogReader` dùng chung để có cùng thuộc tính hiệu lực.
public sealed class CategoryAttributeIndex
{
    private readonly Dictionary<string, CategoryNode> _nodeById;
    private readonly Dictionary<string, string?> _parentById;
    private readonly Dictionary<string, IReadOnlyList<AttributeDefinition>> _effectiveById = new(StringComparer.Ordinal);

    private CategoryAttributeIndex(Dictionary<string, CategoryNode> nodeById)
    {
        _nodeById = nodeById;
        _parentById = nodeById.ToDictionary(pair => pair.Key, pair => pair.Value.ParentId, StringComparer.Ordinal);
    }

    public static async Task<CategoryAttributeIndex> LoadAsync(CatalogDbContext database, CancellationToken cancellationToken)
    {
        // Bảng danh mục nhỏ (cây tối đa 2 cấp). Nạp một lần để tìm cha mà không cần truy vấn đệ quy.
        var rows = await database.Categories
            .AsNoTracking()
            .Select(category => new { category.Id, category.ParentId, category.Name, category.Slug, category.Attributes })
            .ToListAsync(cancellationToken);

        var nodeById = rows.ToDictionary(
            row => row.Id,
            row => new CategoryNode(
                row.Id, row.ParentId, row.Name, row.Slug,
                JsonSerializer.Deserialize<List<AttributeDefinition>>(
                    row.Attributes.RootElement.GetRawText(), AttributeDefinition.JsonOptions) ?? []),
            StringComparer.Ordinal);
        return new CategoryAttributeIndex(nodeById);
    }

    public CategoryRef? FindCategory(string categoryId) =>
        _nodeById.TryGetValue(categoryId, out var node) ? new CategoryRef(node.Id, node.Name, node.Slug) : null;

    // Trả danh mục cha nếu có. Cha mồ côi (không còn trong bảng) coi như không có cha.
    public CategoryRef? FindParent(string categoryId)
    {
        if (!_nodeById.TryGetValue(categoryId, out var node) || node.ParentId is null)
        {
            return null;
        }

        return FindCategory(node.ParentId);
    }

    // Thuộc tính của danh mục và của mọi danh mục cha, cha đứng trước. Danh mục không tồn tại trả danh sách rỗng.
    public IReadOnlyList<AttributeDefinition> EffectiveAttributes(string categoryId)
    {
        if (_effectiveById.TryGetValue(categoryId, out var cached))
        {
            return cached;
        }

        IReadOnlyList<AttributeDefinition> effective = [];
        if (_nodeById.ContainsKey(categoryId))
        {
            foreach (var ancestorId in CategoryTreeWalker.AncestorChain(categoryId, _parentById))
            {
                effective = CategoryAttributes.Resolve(effective, _nodeById[ancestorId].OwnAttributes);
            }
        }

        _effectiveById[categoryId] = effective;
        return effective;
    }

    public IReadOnlyList<AttributeDefinitionInfo> EffectiveDefinitions(string categoryId) =>
        EffectiveAttributes(categoryId).Select(ToInfo).ToList();

    private static AttributeDefinitionInfo ToInfo(AttributeDefinition definition) => new(
        definition.Key, definition.Label, definition.Group, definition.Type, definition.Unit,
        definition.Options, definition.Required, definition.Filterable);

    private sealed record CategoryNode(
        string Id, string? ParentId, string Name, string Slug, IReadOnlyList<AttributeDefinition> OwnAttributes);
}
