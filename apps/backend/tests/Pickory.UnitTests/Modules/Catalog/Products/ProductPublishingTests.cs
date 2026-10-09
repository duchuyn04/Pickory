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

    private static readonly SkuPublishableInput ValidSku = new(Code: "LAP-01", Enabled: true, Price: 20_000_000, CompareAtPrice: null, WeightGrams: 2000, LengthCm: 35, WidthCm: 25, HeightCm: 2);

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
        Assert.Contains(result.Reasons, reason => reason.Contains("Giá bán"));
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

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    // Giá không dương làm `ValidatePrice` từ chối, và lý do ghi mã SKU để admin tìm đúng biến thể.
    public void CheckPublishable_RejectsNonPositivePriceWithSkuCode(int price)
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { Price = price }]));

        Assert.False(result.Publishable);
        Assert.Contains(result.Reasons, reason => reason.StartsWith("SKU LAP-01:") && reason.Contains("Giá bán"));
    }

    [Fact]
    // Giá niêm yết không cao hơn giá bán làm `ValidatePrice` từ chối.
    public void CheckPublishable_RejectsCompareAtPriceNotAbovePrice()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { CompareAtPrice = 20_000_000 }]));

        Assert.False(result.Publishable);
        Assert.Contains(result.Reasons, reason => reason.StartsWith("SKU LAP-01:") && reason.Contains("Giá niêm yết"));
    }

    [Fact]
    public void CheckPublishable_AcceptsCompareAtPriceAbovePrice()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku with { CompareAtPrice = 25_000_000 }]));

        Assert.True(result.Publishable);
    }

    [Fact]
    // Lý do thiếu cân nặng hoặc kích thước ghi mã SKU đúng với biến thể bị thiếu.
    public void CheckPublishable_ShippingReasonNamesTheSkuCode()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus:
        [
            ValidSku,
            ValidSku with { Code = "LAP-02", WeightGrams = null },
        ]));

        var reason = Assert.Single(result.Reasons);
        Assert.Equal("SKU LAP-02: thiếu cân nặng hoặc kích thước đóng gói.", reason);
    }

    [Fact]
    // Mỗi SKU lỗi có lý do riêng, nên hai SKU lỗi giá cho hai lý do mang hai mã khác nhau.
    public void CheckPublishable_ReportsEachInvalidSkuSeparately()
    {
        var result = ProductPublishing.CheckPublishable(ValidProduct(skus:
        [
            ValidSku with { Code = "A-1", Price = 0 },
            ValidSku with { Code = "B-2", Price = 0 },
        ]));

        Assert.Contains(result.Reasons, reason => reason.StartsWith("SKU A-1:"));
        Assert.Contains(result.Reasons, reason => reason.StartsWith("SKU B-2:"));
    }

    [Fact]
    // SKU đang tắt không bán cho khách, nên giá và kích thước sai của nó không chặn đăng bán.
    public void CheckPublishable_IgnoresDisabledSkuPriceAndDimensions()
    {
        var brokenDisabledSku = new SkuPublishableInput("OFF-1", Enabled: false, Price: 0, CompareAtPrice: 1,
            WeightGrams: null, LengthCm: null, WidthCm: null, HeightCm: null);

        var result = ProductPublishing.CheckPublishable(ValidProduct(skus: [ValidSku, brokenDisabledSku]));

        Assert.True(result.Publishable);
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
