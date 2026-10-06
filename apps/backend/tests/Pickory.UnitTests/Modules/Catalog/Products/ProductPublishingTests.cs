using System.Text.Json.Nodes;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Port từ `domain/status.test.ts` của bản Node (CAT-02).
public sealed class ProductPublishingTests
{
    private static readonly AttributeDefinition[] Definitions =
    [
        new()
        {
            Key = "ram_gb",
            Label = "RAM",
            Group = "Bộ nhớ",
            Type = AttributeTypes.Number,
            Required = true,
            Filterable = true,
        },
    ];

    private static readonly SkuPublishableInput ValidSku = new(Enabled: true, Price: 20_000_000, WeightGrams: 2000, LengthCm: 35, WidthCm: 25, HeightCm: 2);

    private static ProductPublishableInput ValidProduct(
        JsonNode? specs = null,
        int imageCount = 1,
        IReadOnlyList<SkuPublishableInput>? skus = null)
    {
        return new ProductPublishableInput(
            specs ?? JsonNode.Parse("""{"ram_gb":16}"""),
            Definitions,
            imageCount,
            skus ?? [ValidSku]);
    }

    [Fact]
    // Sản phẩm có ảnh, đủ thông số bắt buộc và SKU hợp lệ thì đăng bán được.
    public void CheckPublishable_AcceptsCompleteProduct()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct());

        Assert.True(result.Publishable);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    // Thiếu thông số bắt buộc thì không đăng bán được.
    public void CheckPublishable_RequiresSpecs()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(specs: new JsonObject()));

        Assert.False(result.Publishable);
        Assert.Contains(result.Reasons, reason => reason.Contains("thông số"));
    }

    [Fact]
    // Sản phẩm phải có ít nhất một ảnh.
    public void CheckPublishable_RequiresImage()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(imageCount: 0));

        Assert.False(result.Publishable);
        Assert.Contains(result.Reasons, reason => reason.Contains("ảnh"));
    }

    [Fact]
    // Sản phẩm phải có ít nhất một SKU đang bật.
    public void CheckPublishable_RequiresEnabledSku()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { Enabled = false }]));

        Assert.False(result.Publishable);
        Assert.Contains(result.Reasons, reason => reason.Contains("biến thể đang bật"));
    }

    [Fact]
    // SKU đang bật phải có giá lớn hơn 0.
    public void CheckPublishable_RequiresPositivePrice()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { Price = 0 }]));

        Assert.False(result.Publishable);
        Assert.Contains(result.Reasons, reason => reason.Contains("giá"));
    }

    [Fact]
    // SKU đang bật phải có cân nặng và kích thước đóng gói lớn hơn 0 để tính phí vận chuyển.
    public void CheckPublishable_RequiresShippingDimensions()
    {
        var missingWeight = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { WeightGrams = null }]));
        var zeroLength = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { LengthCm = 0 }]));

        Assert.False(missingWeight.Publishable);
        Assert.Contains(missingWeight.Reasons, reason => reason.Contains("cân nặng"));
        Assert.False(zeroLength.Publishable);
        Assert.Contains(zeroLength.Reasons, reason => reason.Contains("kích thước"));
    }

    [Fact]
    // Vi phạm nhiều điều kiện thì báo đủ mọi lý do, không dừng ở lý do đầu tiên.
    public void CheckPublishable_ReportsEveryReason()
    {
        var result = ProductPublishing.CheckPublishable(new ProductPublishableInput(new JsonObject(), Definitions, 0, []));

        Assert.False(result.Publishable);
        Assert.True(result.Reasons.Count >= 3);
    }

    [Theory]
    [InlineData(ProductStatus.Draft, ProductStatus.Active, true)]
    [InlineData(ProductStatus.Draft, ProductStatus.Hidden, false)]
    [InlineData(ProductStatus.Draft, ProductStatus.Discontinued, false)]
    [InlineData(ProductStatus.Active, ProductStatus.Hidden, true)]
    [InlineData(ProductStatus.Active, ProductStatus.Discontinued, true)]
    [InlineData(ProductStatus.Hidden, ProductStatus.Active, true)]
    [InlineData(ProductStatus.Hidden, ProductStatus.Discontinued, true)]
    [InlineData(ProductStatus.Discontinued, ProductStatus.Active, true)]
    [InlineData(ProductStatus.Discontinued, ProductStatus.Hidden, true)]
    [InlineData(ProductStatus.Active, ProductStatus.Draft, false)]
    [InlineData(ProductStatus.Hidden, ProductStatus.Draft, false)]
    [InlineData(ProductStatus.Discontinued, ProductStatus.Draft, false)]
    [InlineData(ProductStatus.Active, ProductStatus.Active, true)]
    [InlineData(ProductStatus.Draft, ProductStatus.Draft, true)]
    // Bảng chuyển trạng thái giống bản Node. Sản phẩm đã phát hành không quay về bản nháp.
    public void CanTransition_FollowsNodeTable(ProductStatus from, ProductStatus to, bool expected)
    {
        Assert.Equal(expected, ProductPublishing.CanTransition(from, to));
    }
}
