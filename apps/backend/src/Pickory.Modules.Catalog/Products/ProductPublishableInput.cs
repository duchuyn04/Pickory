using System.Text.Json.Nodes;
using Pickory.Modules.Catalog.Attributes;

namespace Pickory.Modules.Catalog.Products;

// Dữ liệu sản phẩm cần kiểm tra trước khi chuyển sang trạng thái `active`.
public sealed record ProductPublishableInput(
    JsonNode? Specs,
    IReadOnlyList<AttributeDefinition> AttributeDefinitions,
    int ImageCount,
    IReadOnlyList<SkuPublishableInput> Skus);
