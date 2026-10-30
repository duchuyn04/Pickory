using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-09: `GET /api/products` có lọc, sắp xếp, phân trang và facets, trên bộ dữ liệu cố định.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PublicProductListTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly List<string> _productIds = new();
    private string _parentSlug = null!;
    private string _childSlug = null!;
    private string _parentCategoryId = null!;
    private string _childCategoryId = null!;
    private readonly List<string> _brandIds = new();

    public PublicProductListTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    private string BrandSlug(char letter) => $"hang-{char.ToLowerInvariant(letter)}-{_suffix}";

    // Id cố định theo số thứ tự để kỳ vọng thứ tự hòa (khóa phụ `Id`) tính được trong test.
    private string ProductId(int number) => $"p{number:00}{_suffix}";

    private sealed record Seed(
        int Number, bool InChild, char Brand, ProductStatus Status, string Ram, double Screen, bool Touch,
        int Price, bool InStock, int AgeDays, string Cpu);

    // P8 là draft và P9 là hidden, nên khách không thấy hai sản phẩm này.
    // Nhóm P1, P10, P11 hòa giá và nhóm P7, P10, P11 hòa `CreatedAt`, để kiểm khóa phụ `Id`.
    private static readonly Seed[] Seeds =
    [
        new(1, false, 'A', ProductStatus.Active, "16", 15.6, true, 20_000_000, true, 1, "Intel"),
        new(2, false, 'B', ProductStatus.Active, "32", 17.3, true, 35_000_000, true, 2, "AMD"),
        new(3, false, 'A', ProductStatus.Discontinued, "8", 13.3, false, 10_000_000, false, 3, "Intel"),
        new(4, true, 'A', ProductStatus.Active, "16", 16.0, true, 30_000_000, true, 4, "AMD"),
        new(5, true, 'B', ProductStatus.Active, "32", 15.6, true, 45_000_000, false, 5, "Intel"),
        new(6, true, 'C', ProductStatus.Active, "16", 17.3, false, 25_000_000, true, 6, "AMD"),
        new(7, true, 'C', ProductStatus.Discontinued, "16", 15.6, true, 28_000_000, true, 10, "Intel"),
        new(8, true, 'B', ProductStatus.Draft, "16", 15.6, true, 22_000_000, true, 8, "Intel"),
        new(9, false, 'A', ProductStatus.Hidden, "16", 15.6, true, 21_000_000, true, 9, "AMD"),
        new(10, false, 'C', ProductStatus.Active, "8", 14.0, false, 20_000_000, true, 10, "Intel"),
        new(11, true, 'A', ProductStatus.Active, "8", 13.3, false, 20_000_000, true, 10, "AMD"),
    ];

    private static readonly int[] InsertionOrder = [10, 11, 9, 8, 7, 6, 5, 4, 3, 2, 1];

    private static readonly int[] VisibleNumbers = [1, 2, 3, 4, 5, 6, 7, 10, 11];
    private static readonly int[] ChildVisibleNumbers = [4, 5, 6, 7, 11];

    public async Task InitializeAsync()
    {
        await using var context = _database.CreateCatalogContext();
        var now = DateTime.UtcNow;

        AttributeDefinition[] parentAttributes =
        [
            new() { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = AttributeTypes.Enum, Options = ["8", "16", "32"], Unit = "GB", Filterable = true },
            new() { Key = "screen_inch", Label = "Màn hình", Group = "Màn hình", Type = AttributeTypes.Number, Unit = "inch", Filterable = true },
            new() { Key = "touch", Label = "Cảm ứng", Group = "Màn hình", Type = AttributeTypes.Boolean, Filterable = true },
            new() { Key = "cpu_brand", Label = "Hãng CPU", Group = "Cấu hình", Type = AttributeTypes.Text, Filterable = true },
            new() { Key = "weight_kg", Label = "Cân nặng", Group = "Thân máy", Type = AttributeTypes.Number, Unit = "kg", Filterable = false },
        ];
        var parent = NewCategory("laptop", null, parentAttributes);
        var child = NewCategory("laptop-gaming", parent.Id, []);
        _parentSlug = parent.Slug;
        _childSlug = child.Slug;
        _parentCategoryId = parent.Id;
        _childCategoryId = child.Id;
        context.Categories.Add(parent);
        await context.SaveChangesAsync();
        context.Categories.Add(child);

        var brands = new Dictionary<char, Brand>();
        foreach (var letter in new[] { 'A', 'B', 'C' })
        {
            var brand = new Brand
            {
                Id = Guid.NewGuid().ToString("N"), Name = $"Hãng {letter}", Slug = BrandSlug(letter),
                CreatedAt = now, UpdatedAt = now,
            };
            brands[letter] = brand;
            _brandIds.Add(brand.Id);
            context.Brands.Add(brand);
        }

        // Thêm P10 và P11 trước P7 và P1. Nhờ vậy duyệt xuôi hay ngược theo vị trí lưu đều khác thứ tự `Id`.
        foreach (var seed in Seeds.OrderBy(seed => Array.IndexOf(InsertionOrder, seed.Number)))
        {
            var product = new Product
            {
                Id = ProductId(seed.Number),
                CategoryId = seed.InChild ? child.Id : parent.Id,
                BrandId = brands[seed.Brand].Id,
                Name = $"Laptop mẫu {seed.Number}",
                Slug = $"laptop-mau-{seed.Number}-{_suffix}",
                ShortDesc = "Mô tả ngắn",
                Description = JsonDocument.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}"""),
                Specs = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["ram_gb"] = seed.Ram, ["screen_inch"] = seed.Screen, ["touch"] = seed.Touch, ["cpu_brand"] = seed.Cpu, ["weight_kg"] = 1.8,
                })),
                Options = JsonDocument.Parse("[]"),
                WarrantyMonths = 12,
                Status = seed.Status,
                MinPrice = seed.Price,
                InStock = seed.InStock,
                CreatedAt = now.AddDays(-seed.AgeDays),
                UpdatedAt = now,
            };
            // Ảnh đầu tiên (vị trí 0) là ảnh đại diện. Chèn ảnh vị trí 1 trước để kiểm sắp theo vị trí.
            if (seed.Number == 1)
            {
                product.Images.Add(new ProductImage
                {
                    Id = Guid.NewGuid().ToString("N"), ProductId = product.Id, PublicId = $"pickory/products/{_suffix}-second",
                    Alt = "Ảnh thứ hai", Position = 1, CreatedAt = now,
                });
                product.Images.Add(new ProductImage
                {
                    Id = Guid.NewGuid().ToString("N"), ProductId = product.Id, PublicId = $"pickory/products/{_suffix}-first",
                    Alt = "Ảnh đại diện", Position = 0, CreatedAt = now,
                });
            }

            context.Products.Add(product);
            _productIds.Add(product.Id);
        }

        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = _database.CreateCatalogContext();
        // Bỏ qua bộ lọc soft delete để dọn cả sản phẩm, danh mục và thương hiệu đã bị xóa mềm trong test.
        await context.Products.IgnoreQueryFilters().Where(product => _productIds.Contains(product.Id)).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(category => category.Id == _childCategoryId).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(category => category.Id == _parentCategoryId).ExecuteDeleteAsync();
        await context.Brands.IgnoreQueryFilters().Where(brand => _brandIds.Contains(brand.Id)).ExecuteDeleteAsync();
    }

    private Category NewCategory(string slugPrefix, string? parentId, IReadOnlyList<AttributeDefinition> attributes) => new()
    {
        Id = Guid.NewGuid().ToString("N"), ParentId = parentId, Name = $"Danh mục {slugPrefix} {_suffix}",
        Slug = $"{slugPrefix}-{_suffix}", Position = 0,
        Attributes = JsonDocument.Parse(JsonSerializer.Serialize(attributes, AttributeDefinition.JsonOptions)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private async Task<JsonElement> GetOkAsync(string query)
    {
        var response = await _client.GetAsync($"/api/products?{query}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Mong đợi 200, nhận {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data");
    }

    private async Task<JsonElement> GetErrorAsync(string query, HttpStatusCode status, string code)
    {
        var response = await _client.GetAsync($"/api/products?{query}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Mong đợi {status}, nhận {response.StatusCode}: {body}");
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        return error;
    }

    private static string[] ItemIds(JsonElement data) =>
        data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();

    private string[] Ids(params int[] numbers) => numbers.Select(ProductId).ToArray();

    private static JsonElement FacetAttribute(JsonElement data, string key) =>
        data.GetProperty("facets").GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("key").GetString() == key);

    private static Dictionary<string, int> ValueCounts(JsonElement attribute) =>
        attribute.GetProperty("values").EnumerateArray().ToDictionary(
            value => value.GetProperty("value").ToString(), value => value.GetProperty("count").GetInt32());

    // Giữ nguyên thứ tự mà API trả, vì `ValueCounts` đổi sang `Dictionary` nên mất thứ tự.
    private static string[] ValueOrder(JsonElement attribute) =>
        attribute.GetProperty("values").EnumerateArray().Select(value => value.GetProperty("value").ToString()).ToArray();

    private Dictionary<string, int> BrandCounts(JsonElement data) =>
        data.GetProperty("facets").GetProperty("brands").EnumerateArray().ToDictionary(
            brand => brand.GetProperty("slug").GetString()!, brand => brand.GetProperty("count").GetInt32());

    [Fact]
    public async Task Combined_filters_return_exactly_the_matching_products()
    {
        var query = $"category={_parentSlug}&brand={BrandSlug('A')},{BrandSlug('B')}&priceMin=15000000&priceMax=40000000"
            + "&inStock=true&attr.ram_gb=16,32&attr.screen_inch_min=15&attr.screen_inch_max=17&attr.touch=true&sort=price_asc";

        var data = await GetOkAsync(query);

        // P2 quá cỡ màn hình, P5 quá giá, P11 sai RAM.
        Assert.Equal(Ids(1, 4), ItemIds(data));
        Assert.Equal(2, data.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("brand={A}", new[] { 1, 3, 4, 11 })]
    [InlineData("brand={A},{C}", new[] { 1, 3, 4, 6, 7, 10, 11 })]
    [InlineData("brand=khong-ton-tai", new int[0])]
    [InlineData("priceMin=30000000", new[] { 2, 4, 5 })]
    [InlineData("priceMax=12000000", new[] { 3 })]
    [InlineData("priceMin=20000000&priceMax=20000000", new[] { 1, 10, 11 })]
    [InlineData("inStock=true", new[] { 1, 2, 4, 6, 7, 10, 11 })]
    [InlineData("inStock=false", new[] { 1, 2, 3, 4, 5, 6, 7, 10, 11 })]
    [InlineData("attr.ram_gb=8", new[] { 3, 10, 11 })]
    [InlineData("attr.screen_inch_min=17", new[] { 2, 6 })]
    [InlineData("attr.screen_inch_min=17.3", new[] { 2, 6 })]
    [InlineData("attr.screen_inch_min=17.31", new int[0])]
    [InlineData("attr.screen_inch_min=13.3&attr.screen_inch_max=13.3", new[] { 3, 11 })]
    [InlineData("attr.screen_inch_max=13.3", new[] { 3, 11 })]
    [InlineData("attr.touch=false", new[] { 3, 6, 10, 11 })]
    [InlineData("attr.cpu_brand=AMD", new[] { 2, 4, 6, 11 })]
    [InlineData("attr.cpu_brand=AMD,Intel", new[] { 1, 2, 3, 4, 5, 6, 7, 10, 11 })]
    [InlineData("attr.cpu_brand=amd", new int[0])]
    public async Task Each_filter_narrows_the_parent_category_on_its_own(string filter, int[] expectedNumbers)
    {
        // Mẫu `{A}` và `{C}` là chữ cái hãng. Thay bằng slug thật của bộ dữ liệu.
        var resolved = filter.Replace("{A}", BrandSlug('A')).Replace("{C}", BrandSlug('C'));

        var data = await GetOkAsync($"category={_parentSlug}&{resolved}&sort=price_asc&pageSize=48");

        var expected = expectedNumbers.Select(ProductId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, ItemIds(data).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Facets_ignore_the_filter_of_their_own_group_and_apply_every_other_filter()
    {
        var data = await GetOkAsync($"category={_parentSlug}&attr.ram_gb=16&brand={BrandSlug('A')}&attr.screen_inch_min=15");

        // Danh sách áp mọi bộ lọc: hãng A, RAM 16, màn hình từ 15 inch.
        Assert.Equal(Ids(1, 4), ItemIds(data));

        // RAM bỏ bộ lọc RAM, áp hãng A và màn hình từ 15 inch: chỉ P1 và P4 (đều RAM 16).
        var ramCounts = ValueCounts(FacetAttribute(data, "ram_gb"));
        Assert.Equal(new Dictionary<string, int> { ["16"] = 2 }, ramCounts);

        // Hãng đếm trên tập đã lọc RAM 16 và màn hình từ 15 inch, nhưng không lọc hãng.
        Assert.Equal(
            new Dictionary<string, int> { [BrandSlug('A')] = 2, [BrandSlug('C')] = 2 },
            BrandCounts(data));

        // Màn hình bỏ bộ lọc màn hình, áp hãng A và RAM 16: P1 có 15,6 và P4 có 16,0.
        var screen = FacetAttribute(data, "screen_inch");
        Assert.Equal(15.6m, screen.GetProperty("min").GetDecimal());
        Assert.Equal(16.0m, screen.GetProperty("max").GetDecimal());
    }

    [Fact]
    public async Task Selecting_one_ram_value_still_shows_the_count_of_the_other_ram_values()
    {
        var data = await GetOkAsync($"category={_parentSlug}&attr.ram_gb=16");

        Assert.Equal(Ids(1, 4, 6, 7), ItemIds(data).Order(StringComparer.Ordinal).ToArray());
        // RAM 8 có P3, P10, P11 và RAM 16 có P1, P4, P6, P7. RAM 32 có P2, P5.
        Assert.Equal(
            new Dictionary<string, int> { ["8"] = 3, ["16"] = 4, ["32"] = 2 },
            ValueCounts(FacetAttribute(data, "ram_gb")));
    }

    [Fact]
    public async Task Facets_apply_the_price_and_in_stock_filters()
    {
        var data = await GetOkAsync($"category={_parentSlug}&inStock=true&priceMax=25000000&attr.ram_gb=16");

        // Còn hàng và giá đến 25 triệu: P1, P6, P10, P11. Lọc thêm RAM 16 còn P1 và P6.
        Assert.Equal(Ids(1, 6), ItemIds(data).Order(StringComparer.Ordinal).ToArray());
        // Hãng giữ giá, còn hàng và RAM 16, chỉ bỏ bộ lọc hãng: P1 (hãng A) và P6 (hãng C).
        // Số đếm 1 chứng minh giá và còn hàng có áp, vì bỏ chúng thì A và C đều đếm 2.
        Assert.Equal(
            new Dictionary<string, int> { [BrandSlug('A')] = 1, [BrandSlug('C')] = 1 },
            BrandCounts(data));
        // RAM bỏ bộ lọc RAM nhưng giữ giá và còn hàng: P1, P6 (RAM 16) và P10, P11 (RAM 8). Không có RAM 32.
        Assert.Equal(
            new Dictionary<string, int> { ["8"] = 2, ["16"] = 2 },
            ValueCounts(FacetAttribute(data, "ram_gb")));
    }

    [Fact]
    public async Task Number_facet_range_applies_the_price_and_in_stock_filters()
    {
        var data = await GetOkAsync($"category={_parentSlug}&inStock=true&priceMax=20000000");

        // Còn hàng và giá đến 20 triệu: P1 (15,6), P10 (14,0), P11 (13,3). Bỏ giá thì P6 và P2 đưa max lên 17,3.
        var screen = FacetAttribute(data, "screen_inch");
        Assert.Equal(13.3m, screen.GetProperty("min").GetDecimal());
        Assert.Equal(15.6m, screen.GetProperty("max").GetDecimal());
    }

    [Fact]
    public async Task Facet_values_keep_a_fixed_order_for_the_interface()
    {
        var data = await GetOkAsync($"category={_parentSlug}");

        // `enum` theo thứ tự lựa chọn của danh mục. Thứ tự bảng chữ cái sẽ là 16, 32, 8.
        Assert.Equal(["8", "16", "32"], ValueOrder(FacetAttribute(data, "ram_gb")));
        // `boolean` luôn là false rồi true, dù true có nhiều sản phẩm hơn.
        Assert.Equal(["False", "True"], ValueOrder(FacetAttribute(data, "touch")));
        // `text` theo bảng chữ cái, dù Intel có nhiều sản phẩm hơn AMD.
        Assert.Equal(["AMD", "Intel"], ValueOrder(FacetAttribute(data, "cpu_brand")));
        // Hãng theo tên. Thứ tự theo số đếm sẽ là A, C, B.
        Assert.Equal(
            [BrandSlug('A'), BrandSlug('B'), BrandSlug('C')],
            data.GetProperty("facets").GetProperty("brands").EnumerateArray().Select(brand => brand.GetProperty("slug").GetString()!).ToArray());
    }

    [Fact]
    public async Task Text_facet_counts_values_and_ignores_its_own_filter()
    {
        var data = await GetOkAsync($"category={_parentSlug}&attr.cpu_brand=AMD");

        Assert.Equal(Ids(2, 4, 6, 11), ItemIds(data).Order(StringComparer.Ordinal).ToArray());
        // Facets CPU bỏ bộ lọc CPU: Intel có P1, P3, P5, P7, P10 và AMD có P2, P4, P6, P11.
        Assert.Equal(
            new Dictionary<string, int> { ["AMD"] = 4, ["Intel"] = 5 },
            ValueCounts(FacetAttribute(data, "cpu_brand")));
        var cpu = FacetAttribute(data, "cpu_brand");
        Assert.Equal("text", cpu.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, cpu.GetProperty("unit").ValueKind);
    }

    [Fact]
    public async Task Brand_facet_applies_attribute_filters_but_not_the_brand_filter()
    {
        var data = await GetOkAsync($"category={_parentSlug}&brand={BrandSlug('A')}&attr.ram_gb=16");

        Assert.Equal(Ids(1, 4), ItemIds(data).Order(StringComparer.Ordinal).ToArray());
        // Hãng B không có sản phẩm RAM 16 hiển thị (P8 là draft), nên không xuất hiện trong facets.
        Assert.Equal(
            new Dictionary<string, int> { [BrandSlug('A')] = 2, [BrandSlug('C')] = 2 },
            BrandCounts(data));
    }

    [Fact]
    public async Task Facet_entries_carry_key_label_type_unit_and_skip_zero_counts()
    {
        var data = await GetOkAsync($"category={_parentSlug}&attr.touch=true");

        var attributes = data.GetProperty("facets").GetProperty("attributes").EnumerateArray().ToArray();
        // Chỉ thuộc tính `filterable` có facet: `weight_kg` không nằm trong danh sách.
        Assert.Equal(["ram_gb", "screen_inch", "touch", "cpu_brand"], attributes.Select(item => item.GetProperty("key").GetString()!).ToArray());

        var ram = FacetAttribute(data, "ram_gb");
        Assert.Equal("RAM", ram.GetProperty("label").GetString());
        Assert.Equal("enum", ram.GetProperty("type").GetString());
        Assert.Equal("GB", ram.GetProperty("unit").GetString());

        var screen = FacetAttribute(data, "screen_inch");
        Assert.Equal("inch", screen.GetProperty("unit").GetString());
        Assert.Equal(JsonValueKind.Null, screen.GetProperty("values").ValueKind);

        // Giá trị `touch` đếm trên tập không lọc `touch`: 5 sản phẩm có cảm ứng, 4 sản phẩm không.
        var touch = FacetAttribute(data, "touch");
        Assert.Equal(JsonValueKind.Null, touch.GetProperty("min").ValueKind);
        Assert.Equal(
            new Dictionary<string, int> { ["True"] = 5, ["False"] = 4 },
            ValueCounts(touch));
    }

    [Fact]
    public async Task Facet_without_matching_products_omits_zero_counts_and_has_no_number_range()
    {
        var data = await GetOkAsync($"category={_parentSlug}&brand={BrandSlug('B')}&attr.ram_gb=8");

        Assert.Empty(ItemIds(data));
        // RAM đếm trên hãng B mà không lọc RAM: chỉ có P2 và P5 (RAM 32). Giá trị RAM 8 và 16 có đếm 0 nên bị bỏ.
        Assert.Equal(new Dictionary<string, int> { ["32"] = 2 }, ValueCounts(FacetAttribute(data, "ram_gb")));
        // Hãng đếm trên RAM 8 mà không lọc hãng: A có P3 và P11, C có P10. Hãng B có đếm 0 nên bị bỏ.
        Assert.Equal(new Dictionary<string, int> { [BrandSlug('A')] = 2, [BrandSlug('C')] = 1 }, BrandCounts(data));
        // Tập (hãng B, RAM 8) rỗng, nên màn hình không có min và max.
        Assert.Equal(JsonValueKind.Null, FacetAttribute(data, "screen_inch").GetProperty("min").ValueKind);
    }

    [Fact]
    public async Task Draft_and_hidden_products_are_not_listed_and_not_counted()
    {
        var data = await GetOkAsync($"category={_parentSlug}&pageSize=48");

        var ids = ItemIds(data);
        Assert.DoesNotContain(ProductId(8), ids);
        Assert.DoesNotContain(ProductId(9), ids);
        Assert.Equal(Ids(VisibleNumbers).Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        Assert.Equal(VisibleNumbers.Length, data.GetProperty("total").GetInt32());
        // Hãng A có P1, P3, P4, P11 và hãng B có P2, P5. Hãng C có P6, P7, P10.
        Assert.Equal(
            new Dictionary<string, int> { [BrandSlug('A')] = 4, [BrandSlug('B')] = 2, [BrandSlug('C')] = 3 },
            BrandCounts(data));
    }

    [Fact]
    public async Task Discontinued_products_are_listed_with_their_status()
    {
        var data = await GetOkAsync($"category={_parentSlug}&attr.ram_gb=8&sort=price_asc");

        var discontinued = data.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == ProductId(3));
        Assert.Equal("discontinued", discontinued.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("attr.gpu=rtx")]
    [InlineData("attr.weight_kg_max=2")]
    [InlineData("attr.weight_kg_min=1")]
    public async Task Unknown_or_non_filterable_attribute_returns_400_UNKNOWN_FILTER_with_the_key(string filter)
    {
        var error = await GetErrorAsync($"category={_parentSlug}&{filter}", HttpStatusCode.BadRequest, "UNKNOWN_FILTER");

        var expectedKey = filter.Split('=')[0]["attr.".Length..];
        Assert.Contains(error.GetProperty("details").EnumerateArray(), detail => detail.GetProperty("key").GetString() == expectedKey);
    }

    [Fact]
    public async Task Attribute_filter_without_category_returns_400_UNKNOWN_FILTER()
    {
        var error = await GetErrorAsync("attr.ram_gb=16", HttpStatusCode.BadRequest, "UNKNOWN_FILTER");

        Assert.Contains(error.GetProperty("details").EnumerateArray(), detail => detail.GetProperty("key").GetString() == "ram_gb");
    }

    [Fact]
    public async Task Child_category_inherits_the_filterable_attributes_of_its_parent()
    {
        var data = await GetOkAsync($"category={_childSlug}&attr.ram_gb=16");

        Assert.Equal(Ids(4, 6, 7), ItemIds(data).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["ram_gb", "screen_inch", "touch", "cpu_brand"],
            data.GetProperty("facets").GetProperty("attributes").EnumerateArray().Select(item => item.GetProperty("key").GetString()!).ToArray());
    }

    [Fact]
    public async Task Page_size_above_48_returns_400_and_48_is_accepted()
    {
        await GetErrorAsync($"category={_parentSlug}&pageSize=49", HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var data = await GetOkAsync($"category={_parentSlug}&pageSize=48");
        Assert.Equal(48, data.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("sort=cheapest")]
    [InlineData("priceMin=5&priceMax=1")]
    [InlineData("inStock=yes")]
    [InlineData("attr.touch=maybe")]
    public async Task Invalid_parameters_return_400_VALIDATION_ERROR(string filter)
    {
        await GetErrorAsync($"category={_parentSlug}&{filter}", HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Default_sort_is_newest_first()
    {
        var data = await GetOkAsync($"category={_parentSlug}");

        // P7, P10 và P11 hòa `CreatedAt`, nên khóa phụ `Id` tăng dần xếp P7, P10 rồi P11.
        Assert.Equal(Ids(1, 2, 3, 4, 5, 6, 7, 10, 11), ItemIds(data));
        Assert.Equal(24, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Sort_price_ascending_orders_by_min_price_then_id()
    {
        var data = await GetOkAsync($"category={_parentSlug}&sort=price_asc");

        // Giá 20 triệu có ba sản phẩm P1, P10, P11, xếp theo `Id` tăng dần.
        Assert.Equal(Ids(3, 1, 10, 11, 6, 7, 4, 2, 5), ItemIds(data));
    }

    [Fact]
    public async Task Sort_price_descending_orders_by_min_price_then_id()
    {
        var data = await GetOkAsync($"category={_parentSlug}&sort=price_desc");

        Assert.Equal(Ids(5, 2, 4, 7, 6, 1, 10, 11, 3), ItemIds(data));
    }

    [Theory]
    [InlineData("newest")]
    [InlineData("price_asc")]
    [InlineData("price_desc")]
    public async Task Consecutive_pages_neither_repeat_nor_skip_products(string sort)
    {
        var full = ItemIds(await GetOkAsync($"category={_parentSlug}&sort={sort}&pageSize=48"));

        var paged = new List<string>();
        for (var page = 1; page <= 5; page++)
        {
            var data = await GetOkAsync($"category={_parentSlug}&sort={sort}&pageSize=2&page={page}");
            Assert.Equal(VisibleNumbers.Length, data.GetProperty("total").GetInt32());
            paged.AddRange(ItemIds(data));
        }

        Assert.Equal(full.Length, paged.Distinct().Count());
        Assert.Equal(full, paged);
    }

    [Fact]
    public async Task Page_beyond_the_last_returns_no_items_but_keeps_the_total()
    {
        var data = await GetOkAsync($"category={_parentSlug}&pageSize=24&page=2");

        Assert.Empty(ItemIds(data));
        Assert.Equal(VisibleNumbers.Length, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Parent_category_returns_the_products_of_its_child_categories()
    {
        var parent = await GetOkAsync($"category={_parentSlug}&pageSize=48");
        var child = await GetOkAsync($"category={_childSlug}&pageSize=48");

        Assert.Equal(Ids(VisibleNumbers).Order(StringComparer.Ordinal), ItemIds(parent).Order(StringComparer.Ordinal));
        Assert.Equal(Ids(ChildVisibleNumbers).Order(StringComparer.Ordinal), ItemIds(child).Order(StringComparer.Ordinal));
        Assert.Contains(ProductId(4), ItemIds(parent));
    }

    [Fact]
    public async Task Unknown_category_slug_returns_404_CATEGORY_NOT_FOUND()
    {
        await GetErrorAsync($"category=khong-ton-tai-{_suffix}", HttpStatusCode.NotFound, "CATEGORY_NOT_FOUND");
    }

    [Fact]
    public async Task Item_has_summary_fields_and_the_first_image_as_thumbnail()
    {
        var data = await GetOkAsync($"category={_parentSlug}&sort=price_asc&pageSize=48");

        var items = data.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!);
        var first = items[ProductId(1)];
        Assert.Equal("Laptop mẫu 1", first.GetProperty("name").GetString());
        Assert.Equal($"laptop-mau-1-{_suffix}", first.GetProperty("slug").GetString());
        Assert.Equal(20_000_000, first.GetProperty("minPrice").GetInt32());
        Assert.True(first.GetProperty("inStock").GetBoolean());
        Assert.Equal("active", first.GetProperty("status").GetString());
        Assert.Equal(BrandSlug('A'), first.GetProperty("brand").GetProperty("slug").GetString());
        Assert.Equal("Hãng A", first.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal($"pickory/products/{_suffix}-first", first.GetProperty("image").GetProperty("publicId").GetString());
        Assert.Equal("Ảnh đại diện", first.GetProperty("image").GetProperty("alt").GetString());
        // Trình duyệt không biết tên kho ảnh, nên API trả sẵn URL gốc để giao diện thêm tham số biến đổi.
        Assert.Equal(
            $"https://res.cloudinary.com/{FakeImageStorage.Credentials.CloudName}/image/upload/pickory/products/{_suffix}-first",
            first.GetProperty("image").GetProperty("url").GetString());
        // Danh sách chỉ mang tóm tắt. Mô tả, thông số và SKU thuộc trang chi tiết.
        Assert.False(first.TryGetProperty("description", out _));
        Assert.False(first.TryGetProperty("specs", out _));
        Assert.False(first.TryGetProperty("skus", out _));
        // Sản phẩm không có ảnh trả `image` là null.
        Assert.Equal(JsonValueKind.Null, items[ProductId(2)].GetProperty("image").ValueKind);
    }

    [Fact]
    public async Task Without_category_the_list_works_and_has_no_attribute_facets()
    {
        var data = await GetOkAsync("pageSize=1");

        Assert.Empty(data.GetProperty("facets").GetProperty("attributes").EnumerateArray());
        Assert.True(data.GetProperty("total").GetInt32() >= VisibleNumbers.Length);
    }

    [Fact]
    public async Task Endpoint_is_public_and_needs_no_session()
    {
        var response = await _client.GetAsync($"/api/products?category={_parentSlug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
