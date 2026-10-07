using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Roles;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// Kiểm tra luồng quản lý sản phẩm từ phân quyền tới toàn vẹn dữ liệu.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ProductManagementTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private string _adminCookie = null!;
    private string _salesCookie = null!;

    private readonly List<string> _createdProductIds = new();
    private readonly List<string> _createdCategoryIds = new();
    private readonly List<string> _createdBrandIds = new();
    private readonly List<string> _createdMovementIds = new();

    public ProductManagementTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        // Tắt tự động nạp cookie để bài test kiểm soát phiên người dùng chính xác.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Khởi tạo vai trò hệ thống để cấp quyền cho tài khoản kiểm thử.
        await IdentityTestData.SeedRolesAsync(_database);

        var (_, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, "prod-admin");
        _adminCookie = $"pk_session={adminToken}";

        var (_, salesToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.SalesCode, "prod-sales");
        _salesCookie = $"pk_session={salesToken}";
    }

    public async Task DisposeAsync()
    {
        // Kiểm bất biến trước khi dọn, vì dọn dữ liệu xóa luôn movement.
        await StockInvariant.AssertHoldsAsync(_database);
        await using var catalogDatabase = _database.CreateCatalogContext();

        // Tìm toàn bộ sản phẩm thuộc các danh mục hoặc thương hiệu đã tạo trong bài test.
        var relevantProductIds = await catalogDatabase.Products
            .Where(product => _createdProductIds.Contains(product.Id) ||
                        _createdCategoryIds.Contains(product.CategoryId) ||
                        _createdBrandIds.Contains(product.BrandId))
            .Select(product => product.Id)
            .ToListAsync();

        if (relevantProductIds.Count > 0)
        {
            var relevantSkuIds = await catalogDatabase.Skus
                .Where(sku => relevantProductIds.Contains(sku.ProductId))
                .Select(sku => sku.Id)
                .ToListAsync();

            // Xóa biến động tồn kho của các biến thể (SKU) liên quan để tránh lỗi khóa ngoại.
            var movements = await catalogDatabase.InventoryMovements
                .Where(movement => _createdMovementIds.Contains(movement.Id) || relevantSkuIds.Contains(movement.SkuId))
                .ToListAsync();
            if (movements.Count > 0)
            {
                catalogDatabase.InventoryMovements.RemoveRange(movements);
                await catalogDatabase.SaveChangesAsync();
            }

            // Xóa các sản phẩm kiểm thử cùng toàn bộ SKU và lịch sử slug đi kèm.
            var products = await catalogDatabase.Products
                .Include(product => product.Skus)
                .Include(product => product.SlugHistory)
                .Where(product => relevantProductIds.Contains(product.Id))
                .ToListAsync();
            catalogDatabase.Products.RemoveRange(products);
            await catalogDatabase.SaveChangesAsync();
        }

        // Xóa danh mục kiểm thử sau khi các sản phẩm liên kết đã được dọn sạch.
        if (_createdCategoryIds.Count > 0)
        {
            var categories = await catalogDatabase.Categories
                .Where(category => _createdCategoryIds.Contains(category.Id))
                .ToListAsync();
            catalogDatabase.Categories.RemoveRange(categories);
            await catalogDatabase.SaveChangesAsync();
        }

        // Xóa thương hiệu kiểm thử sau khi các sản phẩm liên kết đã được dọn sạch.
        if (_createdBrandIds.Count > 0)
        {
            var brands = await catalogDatabase.Brands
                .Where(brand => _createdBrandIds.Contains(brand.Id))
                .ToListAsync();
            catalogDatabase.Brands.RemoveRange(brands);
            await catalogDatabase.SaveChangesAsync();
        }
    }

    [Fact]
    // Danh mục cha có danh mục con không được phép chứa sản phẩm trực tiếp.
    public async Task CreateProduct_NonLeafCategory_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parentCategory = await CreateCategoryInDbAsync($"Cha {suffix}", null, []);
        await CreateCategoryInDbAsync($"Con {suffix}", parentCategory.Id, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = CreateDefaultProductPayload(parentCategory.Id, brand.Id, $"Sản Phẩm {suffix}");

        var eventsBefore = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Quy tắc nghiệp vụ yêu cầu sản phẩm chỉ gắn vào danh mục lá.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var errorCode = json.RootElement.GetProperty("error").GetProperty("code").GetString();
        Assert.True(errorCode is "CATEGORY_NOT_LEAF" or "INVALID_CATEGORY");

        // Đảm bảo không có bản ghi nào bị ghi vào cơ sở dữ liệu khi xác thực lỗi.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm {suffix}");
        Assert.False(exists);

        // Kiểm tra không phát sự kiện nào khi yêu cầu bị từ chối.
        var eventsAfter = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        Assert.Equal(eventsBefore, eventsAfter);
    }

    [Fact]
    // Mã danh mục không tồn tại trong hệ thống phải bị từ chối với mã lỗi 400.
    public async Task CreateProduct_NonExistentCategory_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");
        var nonExistentCategoryId = Guid.NewGuid().ToString("N");

        var payload = CreateDefaultProductPayload(nonExistentCategoryId, brand.Id, $"Sản Phẩm {suffix}");
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // API phải chặn khóa ngoại không hợp lệ trước khi thao tác cơ sở dữ liệu.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("INVALID_CATEGORY", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra cơ sở dữ liệu không ghi nhận sản phẩm với danh mục sai.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Mã thương hiệu không tồn tại trong hệ thống phải bị từ chối với mã lỗi 400.
    public async Task CreateProduct_NonExistentBrand_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var nonExistentBrandId = Guid.NewGuid().ToString("N");

        var payload = CreateDefaultProductPayload(category.Id, nonExistentBrandId, $"Sản Phẩm {suffix}");
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // API phải từ chối tham chiếu thương hiệu không tồn tại.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("INVALID_BRAND", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra cơ sở dữ liệu không ghi nhận sản phẩm với thương hiệu sai.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Thông số kỹ thuật sai định dạng hoặc thiếu trường bắt buộc phải trả mã lỗi SPECS_INVALID.
    public async Task CreateProduct_InvalidSpecs_Returns400SpecsInvalid_DetailsAndNoDbWrite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Laptop {suffix}", null,
        [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = "number", Unit = "GB", Required = true, Filterable = true },
            new AttributeDefinition { Key = "cpu_model", Label = "CPU", Group = "Cấu hình", Type = "text", Required = false, Filterable = false }
        ]);
        var brand = await CreateBrandInDbAsync($"Dell {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Laptop Dell {suffix}",
            shortDesc = "Mô tả ngắn hợp lệ",
            description = BuildValidDescriptionDocument(),
            specs = new
            {
                unknown_key = "giá trị lạ",
                cpu_model = 12345
            },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 15000000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var eventsBefore = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Phải trả mã 400 kèm danh sách chi tiết các khóa thuộc tính bị lỗi.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("SPECS_INVALID", error.GetProperty("code").GetString());

        // Kiểm tra mảng details chứa danh sách lỗi chi tiết theo hợp đồng.
        var details = error.GetProperty("details").EnumerateArray().ToList();
        Assert.NotEmpty(details);
        Assert.Contains(details, item => item.GetProperty("key").GetString() == "unknown_key");
        Assert.Contains(details, item => item.GetProperty("key").GetString() == "ram_gb");

        // Kiểm tra cơ sở dữ liệu không lưu bản ghi nào khi dữ liệu bị từ chối.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Laptop Dell {suffix}");
        Assert.False(exists);

        // Đảm bảo không phát sự kiện khi xác thực dữ liệu thất bại.
        var eventsAfter = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        Assert.Equal(eventsBefore, eventsAfter);
    }

    [Fact]
    // Danh mục con kế thừa thuộc tính bắt buộc của danh mục cha khi tạo sản phẩm thành công.
    public async Task CreateProduct_LeafCategory_AcceptsInheritedSpecs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parentCategory = await CreateCategoryInDbAsync($"Cha Gốc {suffix}", null,
        [
            new AttributeDefinition { Key = "brand_origin", Label = "Xuất xứ", Group = "Chung", Type = "text", Required = true, Filterable = true }
        ]);
        var leafCategory = await CreateCategoryInDbAsync($"Con Lá {suffix}", parentCategory.Id,
        [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = "number", Required = true, Filterable = true }
        ]);
        var brand = await CreateBrandInDbAsync($"Hãng {suffix}");

        var payload = new
        {
            categoryId = leafCategory.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Kế Thừa {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new
            {
                brand_origin = "Việt Nam",
                ram_gb = 16
            },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Tạo thành công khi cung cấp đầy đủ thông số kế thừa và riêng biệt.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var productId = json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);
    }

    [Fact]
    // Thiếu thuộc tính bắt buộc kế thừa từ danh mục cha phải trả mã lỗi SPECS_INVALID.
    public async Task CreateProduct_LeafCategory_MissingInheritedRequiredSpec_ReturnsSpecsInvalid()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parentCategory = await CreateCategoryInDbAsync($"Cha Gốc {suffix}", null,
        [
            new AttributeDefinition { Key = "brand_origin", Label = "Xuất xứ", Group = "Chung", Type = "text", Required = true, Filterable = true }
        ]);
        var leafCategory = await CreateCategoryInDbAsync($"Con Lá {suffix}", parentCategory.Id,
        [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = "number", Required = true, Filterable = true }
        ]);
        var brand = await CreateBrandInDbAsync($"Hãng {suffix}");

        var payload = new
        {
            categoryId = leafCategory.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Thiếu Xuất Xứ {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new
            {
                ram_gb = 16
            },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Trả mã 400 kèm khóa thuộc tính cha bị thiếu trong danh sách lỗi chi tiết.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("SPECS_INVALID", error.GetProperty("code").GetString());
        var details = error.GetProperty("details").EnumerateArray().ToList();
        Assert.Contains(details, item => item.GetProperty("key").GetString() == "brand_origin");
    }

    [Fact]
    // Tài liệu mô tả chứa nút script lạ phải bị từ chối bằng mã lỗi DESCRIPTION_INVALID.
    public async Task CreateProduct_DescriptionWithScriptNode_Returns400DescriptionInvalid()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Script {suffix}",
            shortDesc = "Mô tả ngắn",
            description = new
            {
                type = "doc",
                content = new[]
                {
                    new { type = "script", content = Array.Empty<object>() }
                }
            },
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var eventsBefore = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Schema mô tả nghiêm ngặt ngăn chặn mã độc nhúng vào tài liệu.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("DESCRIPTION_INVALID", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra sản phẩm không được ghi vào cơ sở dữ liệu.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Script {suffix}");
        Assert.False(exists);

        // Đảm bảo không có sự kiện nào được phát ra.
        var eventsAfter = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        Assert.Equal(eventsBefore, eventsAfter);
    }

    [Fact]
    // Tài liệu mô tả chứa nút image phải bị từ chối bằng mã lỗi DESCRIPTION_INVALID.
    public async Task CreateProduct_DescriptionWithImageNode_Returns400DescriptionInvalid()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Image {suffix}",
            shortDesc = "Mô tả ngắn",
            description = new
            {
                type = "doc",
                content = new[]
                {
                    new
                    {
                        type = "image",
                        attrs = new { src = "https://example.com/test.jpg" }
                    }
                }
            },
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Tài liệu mô tả không cho phép nút hình ảnh theo quyết định kiến trúc.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("DESCRIPTION_INVALID", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra cơ sở dữ liệu không lưu sản phẩm khi tài liệu mô tả sai.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Image {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Độ dài văn bản mô tả vượt quá giới hạn hai mươi nghìn ký tự phải bị từ chối.
    public async Task CreateProduct_DescriptionExceedingLengthLimit_Returns400DescriptionInvalid()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var longText = new string('A', 20_050);
        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Dài {suffix}",
            shortDesc = "Mô tả ngắn",
            description = new
            {
                type = "doc",
                content = new[]
                {
                    new
                    {
                        type = "paragraph",
                        content = new[]
                        {
                            new { type = "text", text = longText }
                        }
                    }
                }
            },
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Chặn văn bản quá dài nhằm bảo vệ tài nguyên tính toán và bộ nhớ.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("DESCRIPTION_INVALID", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra cơ sở dữ liệu không lưu sản phẩm có mô tả quá dài.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Dài {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Số lượng nút trong tài liệu mô tả vượt quá năm trăm nút phải trả mã lỗi 400.
    public async Task CreateProduct_DescriptionExceedingNodeCountLimit_Returns400DescriptionInvalid()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Tạo danh sách năm trăm lẻ một đoạn văn để vượt giới hạn năm trăm nút.
        var paragraphs = Enumerable.Range(1, 501)
            .Select(index => new
            {
                type = "paragraph",
                content = new[] { new { type = "text", text = $"Đoạn văn số {index}" } }
            })
            .ToArray();

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Nhiều Nút {suffix}",
            shortDesc = "Mô tả ngắn",
            description = new
            {
                type = "doc",
                content = paragraphs
            },
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Quá giới hạn nút tài liệu phải nhận mã lỗi 400 DESCRIPTION_INVALID.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("DESCRIPTION_INVALID", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra cơ sở dữ liệu không ghi nhận sản phẩm có số nút vượt hạn mức.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Nhiều Nút {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Giá niêm yết thấp hơn hoặc bằng giá bán thực tế phải bị từ chối với mã lỗi 400.
    public async Task CreateProduct_InvalidCompareAtPrice_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Giá Lỗi {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    compareAtPrice = (int?)90000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Giá niêm yết không được phép thấp hơn giá bán thực tế.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra cơ sở dữ liệu không ghi nhận sản phẩm giá sai.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Giá Lỗi {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Giá niêm yết bằng đúng giá bán thực tế phải bị từ chối với mã 400.
    public async Task CreateProduct_CompareAtPriceEqualsPrice_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Bằng Giá {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    compareAtPrice = (int?)100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Giá niêm yết phải lớn hơn hẳn giá bán thực tế.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    // Giá bán bằng không hoặc có phần thập phân phải bị từ chối với mã lỗi 400.
    public async Task CreateProduct_ZeroOrDecimalPrice_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Kiểm tra giá bán bằng 0 bị từ chối.
        var zeroPayload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Giá Không {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 0,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };
        var zeroReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", zeroPayload, _adminCookie);
        var zeroRes = await _client.SendAsync(zeroReq);
        Assert.Equal(HttpStatusCode.BadRequest, zeroRes.StatusCode);

        // Kiểm tra giá bán có phần thập phân bị từ chối.
        var decimalPayload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Giá Lẻ {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 99.5,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };
        var decimalReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", decimalPayload, _adminCookie);
        var decimalRes = await _client.SendAsync(decimalReq);
        Assert.Equal(HttpStatusCode.BadRequest, decimalRes.StatusCode);
    }

    [Fact]
    // Giá bán là số âm phải bị từ chối với mã 400.
    public async Task CreateProduct_NegativeOrDecimalPrice_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Âm {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = -50000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Giá tiền đồng Việt Nam bắt buộc phải là số nguyên dương lớn hơn không.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra cơ sở dữ liệu không lưu sản phẩm giá âm.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Âm {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Gửi số đo cân nặng hoặc kích thước là số âm hoặc bằng không phải trả mã 400.
    public async Task CreateProduct_NonPositivePackageMeasurement_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Đo Lỗi {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)-10,
                    lengthCm = (int?)0,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Kích thước và khối lượng bắt buộc phải là số dương khi được gửi.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra cơ sở dữ liệu không lưu sản phẩm có số đo không hợp lệ.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Đo Lỗi {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Biến thể đang bật được phép bỏ trống số đo khi lưu nháp, việc kiểm tra đủ số đo chỉ áp dụng khi đăng bán.
    public async Task CreateProduct_EnabledSku_AllowsNullPackageMeasurementsWhenDraft()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Thiếu Số Đo {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)null,
                    lengthCm = (int?)null,
                    widthCm = (int?)null,
                    heightCm = (int?)null
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Lưu nháp thành công kể cả khi biến thể đang bật chưa nhập số đo đóng gói.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var createdId = json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(createdId);

        var variantElement = json.RootElement.GetProperty("data").GetProperty("variants")[0];
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("weightGrams").ValueKind);
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("lengthCm").ValueKind);
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("widthCm").ValueKind);
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("heightCm").ValueKind);

        // Kiểm tra cơ sở dữ liệu lưu các trường số đo là null.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var savedSku = await catalogDatabase.Skus.SingleAsync(sku => sku.ProductId == createdId);
        Assert.Null(savedSku.WeightGrams);
        Assert.Null(savedSku.LengthCm);
        Assert.Null(savedSku.WidthCm);
        Assert.Null(savedSku.HeightCm);
    }

    [Fact]
    // Cập nhật biến thể đang bật cho phép đặt số đo là null hoặc bỏ trống khi lưu nháp.
    public async Task UpdateProduct_EnabledSku_AllowsNullPackageMeasurementsWhenDraft()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Sửa Đo {suffix}",
        [
            new SkuSeed($"MEAS-{suffix}", new Dictionary<string, string>(), 100000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 120000,
                    enabled = true,
                    weightGrams = (int?)null,
                    lengthCm = (int?)null,
                    widthCm = (int?)null,
                    heightCm = (int?)null
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Cập nhật thành công và phản hồi trả về số đo là null.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var variantElement = json.RootElement.GetProperty("data").GetProperty("variants")[0];
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("weightGrams").ValueKind);
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("lengthCm").ValueKind);
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("widthCm").ValueKind);
        Assert.Equal(JsonValueKind.Null, variantElement.GetProperty("heightCm").ValueKind);

        // Kiểm tra cơ sở dữ liệu cập nhật các trường số đo về null.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedSku = await catalogDatabase.Skus.SingleAsync(sku => sku.ProductId == product.Id);
        Assert.Null(updatedSku.WeightGrams);
        Assert.Null(updatedSku.LengthCm);
        Assert.Null(updatedSku.WidthCm);
        Assert.Null(updatedSku.HeightCm);
    }

    [Fact]
    // Biến thể đã tắt được phép bỏ trống toàn bộ số đo cân nặng và kích thước.
    public async Task CreateProduct_DisabledSku_CanOmitPackageMeasurements()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Tắt Số Đo {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = false,
                    weightGrams = (int?)null,
                    lengthCm = (int?)null,
                    widthCm = (int?)null,
                    heightCm = (int?)null
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Biến thể tắt không tính phí vận chuyển nên không bắt buộc bốn số đo.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var productId = json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);
    }

    [Fact]
    // Danh sách biến thể không khớp với tổ hợp các trục tùy chọn phải bị từ chối bằng mã 400.
    public async Task CreateProduct_MismatchedVariants_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Áo Thun {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đỏ", "Xanh" } }
            },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đỏ" },
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Thiếu tổ hợp biến thể theo quy tắc nhân Descartes phải trả mã lỗi 400.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra cơ sở dữ liệu không lưu sản phẩm khi thiếu tổ hợp biến thể.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Áo Thun {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Gửi tổ hợp biến thể trùng lặp hoặc thừa tùy chọn lạ phải bị từ chối bằng mã 400.
    public async Task CreateProduct_DuplicateOrExtraCombinations_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Trường hợp trùng lặp tổ hợp biến thể trong cùng yêu cầu tạo mới.
        var duplicatePayload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Áo Trùng Tổ Hợp {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen", "Trắng" } }
            },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 120000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var dupRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", duplicatePayload, _adminCookie);
        var dupResponse = await _client.SendAsync(dupRequest);
        Assert.Equal(HttpStatusCode.BadRequest, dupResponse.StatusCode);

        // Trường hợp biến thể chứa giá trị tùy chọn lạ không nằm trong danh sách khai báo.
        var extraPayload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Áo Thừa Tùy Chọn {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen" } }
            },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Vàng" },
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var extraRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", extraPayload, _adminCookie);
        var extraResponse = await _client.SendAsync(extraRequest);
        Assert.Equal(HttpStatusCode.BadRequest, extraResponse.StatusCode);

        // Kiểm tra cơ sở dữ liệu không lưu bản ghi nào từ hai yêu cầu sai trên.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name.Contains(suffix));
        Assert.False(exists);
    }

    [Fact]
    // Gửi mảng rỗng hoặc phần tử null không hợp lệ phải trả mã lỗi 400.
    public async Task CreateProduct_NullOrEmptyCollectionsOrElements_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var badPayloads = new object[]
        {
            // Danh sách biến thể rỗng khi có khai báo tùy chọn.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 1 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = new[] { new { name = "Màu", values = new[] { "Đỏ" } } },
                warrantyMonths = 12, variants = Array.Empty<object>()
            },
            // Thuộc tính biến thể là null.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 2 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = Array.Empty<object>(), warrantyMonths = 12, variants = (object?)null
            },
            // Danh sách biến thể chứa phần tử null.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 3 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = Array.Empty<object>(), warrantyMonths = 12, variants = new object?[] { null }
            },
            // Biến thể có thuộc tính optionValues là null.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 4 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = Array.Empty<object>(), warrantyMonths = 12,
                variants = new[] { new { optionValues = (object?)null, price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 } }
            },
            // Danh sách tùy chọn chứa phần tử null.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 5 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = new object?[] { null }, warrantyMonths = 12,
                variants = new[] { new { optionValues = new Dictionary<string, string>(), price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 } }
            },
            // Tùy chọn có danh sách giá trị là null.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 6 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = new[] { new { name = "Màu", values = (object?)null } }, warrantyMonths = 12,
                variants = new[] { new { optionValues = new Dictionary<string, string>(), price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 } }
            },
            // Tùy chọn có danh sách giá trị chứa phần tử null.
            new
            {
                categoryId = category.Id, brandId = brand.Id, name = $"Rỗng 7 {suffix}",
                shortDesc = "Mô tả", description = BuildValidDescriptionDocument(), specs = new { },
                options = new[] { new { name = "Màu", values = new object?[] { null } } }, warrantyMonths = 12,
                variants = new[] { new { optionValues = new Dictionary<string, string>(), price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 } }
            }
        };

        foreach (var badPayload in badPayloads)
        {
            var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", badPayload, _adminCookie);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // Kiểm tra cơ sở dữ liệu không lưu bất kỳ bản ghi nào từ các yêu cầu sai trên.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name.Contains(suffix));
        Assert.False(exists);
    }

    [Fact]
    // Gửi trường status hoặc stock trong yêu cầu tạo mới phải bị từ chối bằng mã 400.
    public async Task CreateProduct_AttemptToWriteStatusOrStock_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Yêu cầu cố tình gửi trường status không được phép.
        var statusPayload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Trạng Thái {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            status = "active",
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var statusReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", statusPayload, _adminCookie);
        var statusRes = await _client.SendAsync(statusReq);
        Assert.Equal(HttpStatusCode.BadRequest, statusRes.StatusCode);

        // Yêu cầu cố tình gửi trường stock không được phép.
        var stockPayload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Tồn Kho {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), price = 100000, stock = 500, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var stockReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", stockPayload, _adminCookie);
        var stockRes = await _client.SendAsync(stockReq);
        Assert.Equal(HttpStatusCode.BadRequest, stockRes.StatusCode);

        // Kiểm tra cơ sở dữ liệu không lưu sản phẩm từ các yêu cầu ghi đè trái phép.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name.Contains(suffix));
        Assert.False(exists);
    }

    [Fact]
    // Gửi trường status hoặc stock trong PATCH bị từ chối bằng mã 400 và giữ nguyên dữ liệu.
    public async Task UpdateProduct_AttemptToWriteStatusOrStock_Returns400_PreservesExistingState()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Khóa {suffix}",
        [
            new SkuSeed($"LOCK-{suffix}", new Dictionary<string, string>(), 100000, 15, true)
        ]);
        _createdProductIds.Add(product.Id);

        // Cố tình gửi status trong PATCH phải bị từ chối bằng mã 400.
        var statusPatch = new { status = "active" };
        var statusReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", statusPatch, _adminCookie);
        var statusRes = await _client.SendAsync(statusReq);
        Assert.Equal(HttpStatusCode.BadRequest, statusRes.StatusCode);

        // Cố tình gửi stock trong PATCH phải bị từ chối bằng mã 400.
        var stockPatch = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    stock = 999,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };
        var stockReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", stockPatch, _adminCookie);
        var stockRes = await _client.SendAsync(stockReq);
        Assert.Equal(HttpStatusCode.BadRequest, stockRes.StatusCode);

        // Kiểm tra trạng thái và tồn kho trong cơ sở dữ liệu được bảo toàn nguyên vẹn.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var currentProduct = await catalogDatabase.Products.Include(candidate => candidate.Skus).FirstAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(ProductStatus.Draft, currentProduct.Status);
        Assert.Equal(15, currentProduct.Skus.First().Stock);
    }

    [Fact]
    // Quản trị viên nhập mã SKU trùng với SKU đã có trong hệ thống phải nhận mã lỗi 409.
    public async Task CreateProduct_DuplicateSkuCode_Returns409SkuCodeAlreadyExists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");
        var existingSkuCode = $"DUPLICATE-SKU-{suffix}".ToUpperInvariant();

        // Tạo sẵn một sản phẩm với mã SKU định trước trong cơ sở dữ liệu.
        var existingProduct = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Gốc {suffix}",
        [
            new SkuSeed(existingSkuCode, new Dictionary<string, string>(), 100000, 0, true)
        ]);
        _createdProductIds.Add(existingProduct.Id);

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Sản Phẩm Trùng Mã {suffix}",
            shortDesc = "Mô tả ngắn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    code = existingSkuCode,
                    price = 200000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Chặn trùng lặp mã SKU trên phạm vi toàn hệ thống với mã xung đột 409.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("SKU_CODE_ALREADY_EXISTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra cơ sở dữ liệu không ghi đè hoặc thêm sản phẩm trùng mã.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var exists = await catalogDatabase.Products.AnyAsync(product => product.Name == $"Sản Phẩm Trùng Mã {suffix}");
        Assert.False(exists);
    }

    [Fact]
    // Hai biến thể trong cùng một yêu cầu gửi cùng một mã SKU do quản trị viên nhập phải trả 409.
    public async Task CreateProduct_DuplicateExplicitCodesWithinRequest_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");
        var duplicateCode = $"REQ-DUP-{suffix}".ToUpperInvariant();

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Áo Trùng Mã {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[] { new { name = "Màu", values = new[] { "Đỏ", "Xanh" } } },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đỏ" },
                    code = duplicateCode,
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Xanh" },
                    code = duplicateCode,
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Trùng mã SKU tường minh trong cùng payload phải nhận mã lỗi 409.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("SKU_CODE_ALREADY_EXISTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Mã SKU tự sinh được viết hoa và loại bỏ dấu tiếng Việt hoàn toàn.
    public async Task CreateProduct_GeneratedSkuCodes_AreUppercaseAndAccentFree()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Điện Thoại {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[] { new { name = "Màu", values = new[] { "Đỏ" } } },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đỏ" },
                    price = 5000000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var productId = json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        var skuCode = json.RootElement.GetProperty("data").GetProperty("variants")[0].GetProperty("code").GetString()!;

        // Mã SKU sinh ra phải viết hoa và không chứa ký tự có dấu như đ.
        Assert.Equal(skuCode.ToUpperInvariant(), skuCode);
        Assert.DoesNotContain("đ", skuCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    // Trùng mã SKU tự sinh sau khi bỏ dấu sẽ tự động thêm hậu tố số để đảm bảo tính duy nhất.
    public async Task CreateProduct_GeneratedSkuCodes_AccentCollision_AddsNumericSuffix()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Áo {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[] { new { name = "Màu", values = new[] { "Đỏ", "Do" } } },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đỏ" },
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Do" },
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        var variants = data.GetProperty("variants").EnumerateArray().ToList();
        var firstSkuCode = variants[0].GetProperty("code").GetString()!;
        var secondSkuCode = variants[1].GetProperty("code").GetString()!;

        // Hai mã SKU sinh ra không được trùng nhau và mã thứ hai nhận hậu tố số.
        Assert.NotEqual(firstSkuCode, secondSkuCode);
        Assert.True(secondSkuCode.EndsWith("-2") || firstSkuCode.EndsWith("-2"));
    }

    [Fact]
    // Mã tường minh được giữ chỗ trước khiến mã tự sinh bị đẩy sang hậu tố số khi trùng.
    public async Task CreateProduct_ExplicitSkuCode_IsReservedBeforeGeneratedCodes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Nón {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[] { new { name = "Size", values = new[] { "S", "M" } } },
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Size"] = "S" },
                    code = (string?)null,
                    price = 50000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Size"] = "M" },
                    code = (string?)$"NON-{suffix}-S".ToUpperInvariant(),
                    price = 50000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Tạo thành công cả hai biến thể với mã SKU không xung đột nhau.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        var variants = data.GetProperty("variants").EnumerateArray().ToList();
        var sizeSCode = variants[0].GetProperty("code").GetString()!;
        var sizeMCode = variants[1].GetProperty("code").GetString()!;
        Assert.NotEqual(sizeSCode, sizeMCode);
    }

    [Fact]
    // Tên sản phẩm chỉ chứa ký hiệu đặc biệt sinh slug dự phòng san-pham kèm hậu tố số khi trùng.
    public async Task CreateProduct_SymbolsOnlyName_GeneratesFallbackSlugSanPham()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var firstPayload = CreateDefaultProductPayload(category.Id, brand.Id, "!@#$%^&*()");
        var firstRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", firstPayload, _adminCookie);
        var firstResponse = await _client.SendAsync(firstRequest);

        // Lần đầu tên không chứa chữ số nào sẽ dùng slug mặc định san-pham.
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        using var firstJson = await ActorHttp.ReadJsonAsync(firstResponse);
        var firstData = firstJson.RootElement.GetProperty("data");
        var firstId = firstData.GetProperty("id").GetString()!;
        _createdProductIds.Add(firstId);
        Assert.Equal("san-pham", firstData.GetProperty("slug").GetString());

        var secondPayload = CreateDefaultProductPayload(category.Id, brand.Id, "()_+{}|:<>?");
        var secondRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", secondPayload, _adminCookie);
        var secondResponse = await _client.SendAsync(secondRequest);

        // Lần thứ hai trùng slug mặc định sẽ tự động thêm hậu tố số hai.
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        using var secondJson = await ActorHttp.ReadJsonAsync(secondResponse);
        var secondData = secondJson.RootElement.GetProperty("data");
        var secondId = secondData.GetProperty("id").GetString()!;
        _createdProductIds.Add(secondId);
        Assert.Equal("san-pham-2", secondData.GetProperty("slug").GetString());
    }

    [Fact]
    // Tạo sản phẩm thành công mặc định trạng thái draft, tồn kho bằng không và phát đúng một sự kiện.
    public async Task CreateProduct_Success_DefaultsDraftAndZeroStock_CalculatesMinPrice_EmitsCreatedEvent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Tai Nghe Bluetooth {suffix}",
            shortDesc = "Tai nghe không dây",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen", "Trắng" } }
            },
            warrantyMonths = 24,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 300000,
                    compareAtPrice = (int?)350000,
                    enabled = true,
                    weightGrams = (int?)150,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Trắng" },
                    price = 250000,
                    compareAtPrice = (int?)null,
                    enabled = true,
                    weightGrams = (int?)150,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Trả mã 201 cùng dữ liệu sản phẩm mới tạo.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        // Trạng thái sản phẩm mới luôn là bản nháp và không còn hàng khi chưa nhập kho.
        Assert.Equal("draft", data.GetProperty("status").GetString());
        Assert.Equal(250000, data.GetProperty("minPrice").GetInt32());
        Assert.False(data.GetProperty("inStock").GetBoolean());
        Assert.Equal(24, data.GetProperty("warrantyMonths").GetInt32());

        // Kiểm tra biến thể chứa đủ các phần tử với tồn kho mặc định bằng không.
        var variants = data.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(2, variants.Count);
        Assert.All(variants, item => Assert.Equal(0, item.GetProperty("stock").GetInt32()));

        // Kiểm tra cơ sở dữ liệu đã lưu sản phẩm ở trạng thái bản nháp.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var product = await catalogDatabase.Products.Include(product => product.Skus).FirstOrDefaultAsync(product => product.Id == productId);
        Assert.NotNull(product);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(250000, product.MinPrice);
        Assert.False(product.InStock);
        Assert.Equal(2, product.Skus.Count);

        // Đảm bảo phát đúng một sự kiện tạo sản phẩm sau khi giao dịch cơ sở dữ liệu hoàn tất.
        var createdEvents = _factory.DomainEvents.OfType<ProductChangedEvent>()
            .Where(domainEvent => domainEvent.ProductId == productId)
            .ToList();
        Assert.Single(createdEvents);
        Assert.Equal(ProductChange.Created, createdEvents[0].Change);

        // Tạo mới không được ghi nhật ký thay đổi giá của SKU.
        await using var identity = _database.CreateIdentityContext();
        var priceAudits = await identity.AuditLogs
            .Where(log => log.Action == "catalog.sku.price_change" && product.Skus.Select(sku => sku.Id).Contains(log.TargetId))
            .ToListAsync();
        Assert.Empty(priceAudits);
    }

    [Fact]
    // Sự kiện chỉ phát sau commit: handler mở kết nối riêng đọc được sản phẩm đã lưu.
    public async Task CreateProduct_EmitsEventAfterCommit_HandlerObservesPersistedState()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var productName = $"Tai Nghe Cam Ket {suffix}";
        var eventHandled = false;
        var persistedInSeparateDb = false;

        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDomainEventHandler<ProductChangedEvent>>(new DelegateProductChangedHandler(async @event =>
                {
                    eventHandled = true;
                    // Mở kết nối cơ sở dữ liệu riêng biệt để xác nhận dữ liệu đã commit thực sự.
                    await using var separateDb = _database.CreateCatalogContext();
                    var product = await separateDb.Products
                        .Include(product => product.Skus)
                        .FirstOrDefaultAsync(product => product.Id == @event.ProductId);
                    if (product is not null && product.Name == productName && product.Skus.Count == 1)
                    {
                        persistedInSeparateDb = true;
                    }
                }));
            });
        });

        using var customClient = customFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var payload = CreateDefaultProductPayload(category.Id, brand.Id, productName);
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await customClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var productId = json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        // Xác nhận handler đã chạy và quan sát được trạng thái đã commit tại thời điểm nhận sự kiện.
        Assert.True(eventHandled);
        Assert.True(persistedInSeparateDb);
    }

    [Fact]
    // Sản phẩm không có tùy chọn tạo một biến thể duy nhất với giá trị tùy chọn rỗng.
    public async Task CreateProduct_WithoutOptions_CreatesSingleVariantWithEmptyOptionValues()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Cáp Sạc Nhanh {suffix}",
            shortDesc = "Cáp type C",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 6,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 80000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Sản phẩm không có tùy chọn vẫn phải có một biến thể để giữ thông tin giá và tồn kho.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        var variants = data.GetProperty("variants").EnumerateArray().ToList();
        Assert.Single(variants);
        var optionValues = variants[0].GetProperty("optionValues");
        Assert.Equal(JsonValueKind.Object, optionValues.ValueKind);
        Assert.Empty(optionValues.EnumerateObject());
    }

    [Fact]
    // Tạo sản phẩm lưu đầy đủ bốn số đo đóng gói và phản hồi trả về chính xác.
    public async Task CreateProduct_PositivePackageMeasurements_ArePersistedAndReturned()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var payload = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = $"Hộp Đựng {suffix}",
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)350,
                    lengthCm = (int?)25,
                    widthCm = (int?)20,
                    heightCm = (int?)15
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var variant = json.RootElement.GetProperty("data").GetProperty("variants")[0];
        Assert.Equal(350, variant.GetProperty("weightGrams").GetInt32());
        Assert.Equal(25, variant.GetProperty("lengthCm").GetInt32());
        Assert.Equal(20, variant.GetProperty("widthCm").GetInt32());
        Assert.Equal(15, variant.GetProperty("heightCm").GetInt32());

        var productId = json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(productId);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var sku = await catalogDatabase.Skus.FirstAsync(sku => sku.ProductId == productId);
        Assert.Equal(350, sku.WeightGrams);
        Assert.Equal(25, sku.LengthCm);
        Assert.Equal(20, sku.WidthCm);
        Assert.Equal(15, sku.HeightCm);
    }

    [Fact]
    // Hai yêu cầu tạo đồng thời cùng tên nhưng khác mã SKU tường minh tạo hai slug duy nhất.
    public async Task CreateProduct_ConcurrentPostSameNameDistinctExplicitCodes_GeneratesUniqueSlugsAndPreservesCodes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");
        var commonName = $"Loa Kéo {suffix}";
        var firstExplicitCode = $"SPK-A-{suffix}".ToUpperInvariant();
        var secondExplicitCode = $"SPK-B-{suffix}".ToUpperInvariant();

        var payloadA = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = commonName,
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), code = firstExplicitCode, price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var payloadB = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = commonName,
            shortDesc = "Mô tả",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), code = secondExplicitCode, price = 100000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var firstRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payloadA, _adminCookie);
        var secondRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payloadB, _adminCookie);

        var responses = await Task.WhenAll(_client.SendAsync(firstRequest), _client.SendAsync(secondRequest));
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);

        using var firstJson = await ActorHttp.ReadJsonAsync(responses[0]);
        using var secondJson = await ActorHttp.ReadJsonAsync(responses[1]);

        var firstProductId = firstJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var secondProductId = secondJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        _createdProductIds.Add(firstProductId);
        _createdProductIds.Add(secondProductId);

        var firstSlug = firstJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;
        var secondSlug = secondJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        // Hai slug sinh ra phải độc lập và không trùng lặp nhau.
        Assert.NotEqual(firstSlug, secondSlug);
        Assert.NotEqual(firstProductId, secondProductId);

        // Thao tác PATCH tiếp nối trên cả hai sản phẩm đều thành công.
        var firstPatch = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{firstProductId}", new { shortDesc = "Sửa A" }, _adminCookie);
        var secondPatch = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{secondProductId}", new { shortDesc = "Sửa B" }, _adminCookie);
        var patchResponses = await Task.WhenAll(_client.SendAsync(firstPatch), _client.SendAsync(secondPatch));
        Assert.Equal(HttpStatusCode.OK, patchResponses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, patchResponses[1].StatusCode);
    }

    [Fact]
    // Hai yêu cầu PATCH đồng thời thêm cùng tổ hợp biến thể với mã khác nhau duy trì tính nhất quán.
    public async Task UpdateProduct_ConcurrentPatchAddingSameCombinationDistinctExplicitCodes_PreservesUniqueSkusAndSlugs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Tai Nghe {suffix}",
        [
            new SkuSeed($"EAR-{suffix}-DEN", new Dictionary<string, string> { ["Màu"] = "Đen" }, 200000, 5, true)
        ],
        options: new object[] { new { name = "Màu", values = new[] { "Đen" } } });
        _createdProductIds.Add(product.Id);

        var firstPatch = new
        {
            options = new[] { new { name = "Màu", values = new[] { "Đen", "Trắng" } } },
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" }, code = (string?)null, price = 200000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 },
                new { optionValues = new Dictionary<string, string> { ["Màu"] = "Trắng" }, code = (string?)$"EAR-{suffix}-W1", price = 250000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var secondPatch = new
        {
            options = new[] { new { name = "Màu", values = new[] { "Đen", "Trắng" } } },
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" }, code = (string?)null, price = 200000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 },
                new { optionValues = new Dictionary<string, string> { ["Màu"] = "Trắng" }, code = (string?)$"EAR-{suffix}-W2", price = 250000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var firstRequest = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", firstPatch, _adminCookie);
        var secondRequest = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", secondPatch, _adminCookie);

        var responses = await Task.WhenAll(_client.SendAsync(firstRequest), _client.SendAsync(secondRequest));

        // Cả hai yêu cầu đều được tuần tự hóa an toàn mà không xung đột khóa cơ sở dữ liệu.
        Assert.All(responses, response => Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);

        // Kiểm tra sau khi hai yêu cầu kết thúc, cơ sở dữ liệu có đúng một SKU cho màu trắng.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var currentProduct = await catalogDatabase.Products.Include(candidate => candidate.Skus).FirstAsync(candidate => candidate.Id == product.Id);
        var whiteSkus = currentProduct.Skus.Where(sku => sku.Enabled && sku.Code.StartsWith($"EAR-{suffix}-W")).ToList();
        Assert.Single(whiteSkus);

        // Yêu cầu PATCH tiếp theo vẫn hoạt động bình thường trên sản phẩm.
        var followUpReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}",
            new { shortDesc = "Mô tả sau đồng thời" }, _adminCookie);
        var followUpRes = await _client.SendAsync(followUpReq);
        Assert.Equal(HttpStatusCode.OK, followUpRes.StatusCode);
    }

    [Fact]
    // Thêm giá trị tùy chọn sinh SKU mới và giữ nguyên mã cũ khi đổi giá.
    public async Task UpdateProduct_AddOptionValue_GeneratesNewSku_PreservesOldSkuAttributes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var oldSkuCode = $"OLD-SKU-{suffix}".ToUpperInvariant();
        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Áo Khoác {suffix}",
        [
            new SkuSeed(oldSkuCode, new Dictionary<string, string> { ["Màu"] = "Đen" }, 200000, 35, true)
        ],
        options: new object[] { new { name = "Màu", values = new[] { "Đen" } } });
        _createdProductIds.Add(product.Id);

        var oldSkuId = product.Skus.First().Id;

        var patchPayload = new
        {
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen", "Trắng" } }
            },
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 220000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Trắng" },
                    price = 250000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Cập nhật thành công cấu trúc biến thể.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var variants = data.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(2, variants.Count);

        // Kiểm tra SKU cũ giữ nguyên định danh, mã định danh và số lượng tồn kho.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedProduct = await catalogDatabase.Products.Include(candidate => candidate.Skus).FirstAsync(candidate => candidate.Id == product.Id);
        var oldSku = updatedProduct.Skus.First(sku => sku.Id == oldSkuId);
        Assert.Equal(oldSkuCode, oldSku.Code);
        Assert.Equal(35, oldSku.Stock);
        Assert.Equal(220000, oldSku.Price);

        // Kiểm tra SKU mới sinh ra có tồn kho mặc định bằng không.
        var newSku = updatedProduct.Skus.First(sku => sku.Id != oldSkuId);
        Assert.Equal(0, newSku.Stock);
        Assert.Equal(250000, newSku.Price);

        // Đếm toàn bộ sự kiện của sản phẩm và kiểm tra phát đúng duy nhất một sự kiện cập nhật.
        var productEvents = _factory.DomainEvents.OfType<ProductChangedEvent>()
            .Where(domainEvent => domainEvent.ProductId == product.Id)
            .ToList();
        Assert.Single(productEvents);
        Assert.Equal(ProductChange.Updated, productEvents[0].Change);
    }

    [Fact]
    // Thêm giá trị tùy chọn khi giá cũ không đổi thì SKU cũ giữ nguyên giá và tồn kho.
    public async Task UpdateProduct_AddOptionValue_PreservesOldSkuPriceAndStock_WhenPriceNotModified()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var oldSkuCode = $"PRESERVE-SKU-{suffix}".ToUpperInvariant();
        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Áo Sơ Mi {suffix}",
        [
            new SkuSeed(oldSkuCode, new Dictionary<string, string> { ["Màu"] = "Đen" }, 200000, 35, true)
        ],
        options: new object[] { new { name = "Màu", values = new[] { "Đen" } } });
        _createdProductIds.Add(product.Id);

        var oldSkuId = product.Skus.First().Id;

        var patchPayload = new
        {
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen", "Trắng" } }
            },
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 200000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Trắng" },
                    price = 250000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Cập nhật thành công và bảo toàn giá cũ.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Kiểm tra SKU cũ giữ nguyên giá bán và mức tồn kho ban đầu.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedProduct = await catalogDatabase.Products.Include(candidate => candidate.Skus).FirstAsync(candidate => candidate.Id == product.Id);
        var oldSku = updatedProduct.Skus.First(sku => sku.Id == oldSkuId);
        Assert.Equal(oldSkuCode, oldSku.Code);
        Assert.Equal(35, oldSku.Stock);
        Assert.Equal(200000, oldSku.Price);
    }

    [Fact]
// Bỏ giá trị tùy chọn bảo toàn đúng biến động và bất biến tồn kho của SKU bị tắt.
    public async Task UpdateProduct_RemoveOptionValue_DisablesSku_PreservesMovementAndStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Bàn Phím {suffix}",
        [
            new SkuSeed($"KB-DEN-{suffix}", new Dictionary<string, string> { ["Màu"] = "Đen" }, 500000, 10, true),
            new SkuSeed($"KB-TRANG-{suffix}", new Dictionary<string, string> { ["Màu"] = "Trắng" }, 550000, 8, true)
        ],
        options: new object[] { new { name = "Màu", values = new[] { "Đen", "Trắng" } } });
        _createdProductIds.Add(product.Id);

        var whiteSkuId = product.Skus.First(sku => sku.Code == $"KB-TRANG-{suffix}").Id;

        // Đọc bản ghi biến động tồn kho đã được seed từ trước trong cơ sở dữ liệu.
        InventoryMovement initialMovement;
        await using (var catalogDatabase = _database.CreateCatalogContext())
        {
            var sku = await catalogDatabase.Skus.Include(sku => sku.Movements).FirstAsync(sku => sku.Id == whiteSkuId);
            initialMovement = sku.Movements.Single();
        }

        var patchPayload = new
        {
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen" } }
            },
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 500000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Cập nhật thành công việc loại bỏ tùy chọn màu trắng.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Kiểm tra SKU màu trắng chuyển sang trạng thái tắt nhưng không bị xóa.
        await using var verifyCatalogDatabase = _database.CreateCatalogContext();
        var disabledSku = await verifyCatalogDatabase.Skus.Include(sku => sku.Movements).FirstOrDefaultAsync(sku => sku.Id == whiteSkuId);
        Assert.NotNull(disabledSku);
        Assert.False(disabledSku.Enabled);
        Assert.Equal(8, disabledSku.Stock);

        // Kiểm tra đúng bản ghi biến động ban đầu được bảo toàn nguyên vẹn.
        var currentMovement = disabledSku.Movements.Single();
        Assert.Equal(initialMovement.Id, currentMovement.Id);
        Assert.Equal(initialMovement.Delta, currentMovement.Delta);
        Assert.Equal(initialMovement.Type, currentMovement.Type);
        Assert.Equal(initialMovement.Reason, currentMovement.Reason);

        // Bất biến tồn kho bằng tổng các delta của bản ghi biến động.
        Assert.Equal(disabledSku.Stock, disabledSku.Movements.Sum(movement => movement.Delta));
    }

    [Fact]
    // Thêm lại tổ hợp tùy chọn đã bị tắt thì SKU cũ được bật lại và giữ nguyên mã cũ.
    public async Task UpdateProduct_ReactivateRetiredSku_ReenablesOldSku_PreservesCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var originalCode = $"RETIRED-SKU-{suffix}".ToUpperInvariant();
        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Chuột {suffix}",
        [
            new SkuSeed($"ACTIVE-{suffix}", new Dictionary<string, string> { ["Màu"] = "Đen" }, 200000, 5, true),
            new SkuSeed(originalCode, new Dictionary<string, string> { ["Màu"] = "Trắng" }, 220000, 3, false)
        ],
        options: new object[] { new { name = "Màu", values = new[] { "Đen" } } });
        _createdProductIds.Add(product.Id);

        var retiredSkuId = product.Skus.First(sku => sku.Code == originalCode).Id;

        var patchPayload = new
        {
            options = new[]
            {
                new { name = "Màu", values = new[] { "Đen", "Trắng" } }
            },
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Đen" },
                    price = 200000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                },
                new
                {
                    optionValues = new Dictionary<string, string> { ["Màu"] = "Trắng" },
                    price = 230000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Phục hồi tổ hợp biến thể thành công.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Kiểm tra SKU cũ được bật lại với cùng định danh và cùng mã định danh.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var reenbaledSku = await catalogDatabase.Skus.FirstOrDefaultAsync(sku => sku.Id == retiredSkuId);
        Assert.NotNull(reenbaledSku);
        Assert.True(reenbaledSku.Enabled);
        Assert.Equal(originalCode, reenbaledSku.Code);
        Assert.Equal(230000, reenbaledSku.Price);
    }

    [Fact]
    // Gửi mã SKU mới trên tổ hợp cũ sẽ đổi mã còn gửi trống sẽ giữ nguyên mã cũ.
    public async Task UpdateProduct_SuppliedSkuCodeOnOldCombination_UpdatesCode_BlankPreservesOldCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var initialCode = $"INIT-CODE-{suffix}".ToUpperInvariant();
        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Màn Hình {suffix}",
        [
            new SkuSeed(initialCode, new Dictionary<string, string>(), 4000000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);

        var explicitNewCode = $"NEW-CUSTOM-CODE-{suffix}".ToUpperInvariant();
        var firstPatchPayload = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    code = explicitNewCode,
                    price = 4200000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        // Gửi mã mới yêu cầu cập nhật lại mã SKU của biến thể hiện tại.
        var firstRequest = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", firstPatchPayload, _adminCookie);
        var firstResponse = await _client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        await using (var catalogDatabase = _database.CreateCatalogContext())
        {
            var sku = await catalogDatabase.Skus.FirstAsync(sku => sku.ProductId == product.Id);
            Assert.Equal(explicitNewCode, sku.Code);
        }

        var secondPatchPayload = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    code = (string?)null,
                    price = 4300000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        // Gửi mã rỗng hoặc null phải giữ nguyên mã SKU hiện có thay vì sinh đè.
        var secondRequest = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", secondPatchPayload, _adminCookie);
        var secondResponse = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        await using (var catalogDatabase = _database.CreateCatalogContext())
        {
            var sku = await catalogDatabase.Skus.FirstAsync(sku => sku.ProductId == product.Id);
            Assert.Equal(explicitNewCode, sku.Code);
        }
    }

    [Fact]
    // Thay đổi giá bán của biến thể ghi đúng một dòng nhật ký kiểm toán giá trước và sau.
    public async Task UpdateProduct_ChangePrice_RecordsAuditPriceChangeWithBeforeAndAfter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Loa {suffix}",
        [
            new SkuSeed($"SPK-{suffix}", new Dictionary<string, string>(), 1000000, 20, true)
        ]);
        _createdProductIds.Add(product.Id);

        var skuId = product.Skus.First().Id;

        var patchPayload = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 1200000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Cập nhật giá thành công với mã 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Kiểm tra nhật ký kiểm toán ghi đúng một dòng thay đổi giá với ảnh chụp trước và sau.
        await using var identity = _database.CreateIdentityContext();
        var audits = await identity.AuditLogs
            .Where(log => log.TargetId == skuId && log.Action == "catalog.sku.price_change")
            .ToListAsync();
        Assert.Single(audits);

        var auditLog = audits[0];
        Assert.NotNull(auditLog.Before);
        Assert.NotNull(auditLog.After);

        var beforePrice = auditLog.Before!.RootElement.GetProperty("price").GetInt32();
        var afterPrice = auditLog.After!.RootElement.GetProperty("price").GetInt32();
        Assert.Equal(1000000, beforePrice);
        Assert.Equal(1200000, afterPrice);
    }

    [Fact]
    // Gặp lỗi ghi nhật ký kiểm toán trong giao dịch thì toàn bộ thay đổi cơ sở dữ liệu bị hoàn nguyên.
    public async Task UpdateProduct_RollbackOnAuditWriterFailure_PreservesDatabaseAndEmitsZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var initialName = $"Màn Hình Gaming {suffix}";
        var product = await SeedProductInDbAsync(category.Id, brand.Id, initialName,
        [
            new SkuSeed($"SCR-{suffix}", new Dictionary<string, string>(), 3000000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);
        var skuId = product.Skus.First().Id;

        // Tạo máy khách với dịch vụ ghi nhật ký kiểm toán cố ý gây lỗi khi thực thi.
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditLogWriter>();
                services.AddSingleton<IAuditLogWriter, FailingAuditLogWriter>();
            });
        });
        using var customClient = customFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var eventsRecorded = customFactory.Services.GetRequiredService<RecordedDomainEvents>();
        var eventsBefore = eventsRecorded.OfType<ProductChangedEvent>().Count;

        var patchPayload = new
        {
            name = $"Màn Hình Sửa Tên {suffix}",
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 3500000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await customClient.SendAsync(request);

        // Lỗi ghi nhật ký kiểm toán trong giao dịch dẫn đến mã lỗi 500 từ hệ thống.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Kiểm tra tên và giá sản phẩm trong cơ sở dữ liệu không hề bị thay đổi.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var currentProduct = await catalogDatabase.Products.Include(candidate => candidate.Skus).FirstAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(initialName, currentProduct.Name);
        Assert.Equal(3000000, currentProduct.Skus.First().Price);

        // Kiểm tra không có bản ghi lịch sử slug nào được tạo thêm khi giao dịch thất bại.
        var historyExists = await catalogDatabase.ProductSlugHistories.AnyAsync(history => history.ProductId == product.Id);
        Assert.False(historyExists);

        // Kiểm tra không có bản ghi nhật ký kiểm toán nào được lưu vào cơ sở dữ liệu.
        await using var identity = _database.CreateIdentityContext();
        var audits = await identity.AuditLogs.Where(log => log.TargetId == skuId).ToListAsync();
        Assert.Empty(audits);

        // Đảm bảo không có sự kiện nào được phát ra khi giao dịch bị hoàn nguyên.
        var eventsAfter = eventsRecorded.OfType<ProductChangedEvent>().Count;
        Assert.Equal(eventsBefore, eventsAfter);
    }

    [Fact]
    // Không đổi giá hoặc chỉ đổi giá niêm yết thì không ghi nhật ký thay đổi giá.
    public async Task UpdateProduct_UnchangedPriceOrCompareAtOnly_DoesNotRecordPriceAudit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Nồi Cơm {suffix}",
        [
            new SkuSeed($"COOK-{suffix}", new Dictionary<string, string>(), 800000, 15, true, CompareAtPrice: 900000)
        ]);
        _createdProductIds.Add(product.Id);

        var skuId = product.Skus.First().Id;

        // Chỉ đổi tên sản phẩm mà không đụng đến giá.
        var firstPatch = new { name = $"Nồi Cơm Mới {suffix}" };
        var firstReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", firstPatch, _adminCookie);
        var firstRes = await _client.SendAsync(firstReq);
        Assert.Equal(HttpStatusCode.OK, firstRes.StatusCode);

        // Chỉ đổi giá niêm yết còn giá bán thực tế giữ nguyên mức cũ.
        var secondPatch = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 800000,
                    compareAtPrice = (int?)950000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };
        var secondReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", secondPatch, _adminCookie);
        var secondRes = await _client.SendAsync(secondReq);
        Assert.Equal(HttpStatusCode.OK, secondRes.StatusCode);

        // Kiểm tra không có dòng nhật ký thay đổi giá nào được tạo ra.
        await using var identity = _database.CreateIdentityContext();
        var audits = await identity.AuditLogs
            .Where(log => log.TargetId == skuId && log.Action == "catalog.sku.price_change")
            .ToListAsync();
        Assert.Empty(audits);

        // Kiểm tra giá niêm yết mới đã được lưu thành công vào cơ sở dữ liệu.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedSku = await catalogDatabase.Skus.FirstAsync(sku => sku.Id == skuId);
        Assert.Equal(950000, updatedSku.CompareAtPrice);
    }

    [Fact]
    // Cập nhật số đo đóng gói cho biến thể đang bật được lưu vào cơ sở dữ liệu và giữ nguyên SKU.
    public async Task UpdateProduct_PackageMeasurements_AreUpdatedWithoutChangingSkuIdentity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Gối {suffix}",
        [
            new SkuSeed($"PILLOW-{suffix}", new Dictionary<string, string>(), 100000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);
        var skuId = product.Skus.First().Id;

        var patchPayload = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)500,
                    lengthCm = (int?)40,
                    widthCm = (int?)30,
                    heightCm = (int?)20
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var sku = await catalogDatabase.Skus.FirstAsync(sku => sku.ProductId == product.Id);
        Assert.Equal(skuId, sku.Id);
        Assert.Equal(500, sku.WeightGrams);
        Assert.Equal(40, sku.LengthCm);
        Assert.Equal(30, sku.WidthCm);
        Assert.Equal(20, sku.HeightCm);
    }

    [Fact]
    // Cập nhật không gửi danh sách biến thể bảo toàn nguyên vẹn số đo đóng gói đã có.
    public async Task UpdateProduct_OmittedVariants_PreservesPackageMeasurements()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Nệm {suffix}",
        [
            new SkuSeed($"MATT-{suffix}", new Dictionary<string, string>(), 2000000, 5, true, WeightGrams: 8000, LengthCm: 200, WidthCm: 160, HeightCm: 20)
        ]);
        _createdProductIds.Add(product.Id);

        var patchPayload = new { name = $"Nệm Cao Cấp {suffix}" };
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var sku = await catalogDatabase.Skus.FirstAsync(sku => sku.ProductId == product.Id);
        Assert.Equal(8000, sku.WeightGrams);
        Assert.Equal(200, sku.LengthCm);
        Assert.Equal(160, sku.WidthCm);
        Assert.Equal(20, sku.HeightCm);
    }

    [Fact]
    // Thay đổi giá bán tính lại cột minPrice lưu trong cơ sở dữ liệu.
    public async Task UpdateProduct_ChangePrice_RecalculatesPersistedPriceSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Loa Mini {suffix}",
        [
            new SkuSeed($"MINI-{suffix}", new Dictionary<string, string>(), 500000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 450000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(450000, updatedProduct.MinPrice);
    }

    [Fact]
    // Tắt SKU còn hàng cuối cùng chuyển trạng thái inStock sang false và lấy giá SKU hết hàng.
    public async Task UpdateProduct_DisableLastInStockSku_FallsBackToOutOfStockEnabledPrice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Đèn Bàn {suffix}",
        [
            new SkuSeed($"LAMP-A-{suffix}", new Dictionary<string, string> { ["Loại"] = "A" }, 300000, 10, true),
            new SkuSeed($"LAMP-B-{suffix}", new Dictionary<string, string> { ["Loại"] = "B" }, 200000, 0, true)
        ],
        options: new object[] { new { name = "Loại", values = new[] { "A", "B" } } });
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string> { ["Loại"] = "A" }, price = 300000, enabled = false, weightGrams = (int?)null, lengthCm = (int?)null, widthCm = (int?)null, heightCm = (int?)null },
                new { optionValues = new Dictionary<string, string> { ["Loại"] = "B" }, price = 200000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        Assert.False(updatedProduct.InStock);
        Assert.Equal(200000, updatedProduct.MinPrice);
    }

    [Fact]
    // Tắt toàn bộ SKU làm minPrice thành null và inStock thành false.
    public async Task UpdateProduct_DisableAllSkus_SetsNullMinPriceAndFalseInStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Ghế {suffix}",
        [
            new SkuSeed($"CHAIR-{suffix}", new Dictionary<string, string>(), 400000, 5, true)
        ]);
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), price = 400000, enabled = false, weightGrams = (int?)null, lengthCm = (int?)null, widthCm = (int?)null, heightCm = (int?)null }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        Assert.Null(updatedProduct.MinPrice);
        Assert.False(updatedProduct.InStock);
    }

    [Fact]
    // Bật lại SKU còn hàng cập nhật trạng thái inStock thành true.
    public async Task UpdateProduct_ReactivateInStockSku_RecalculatesPriceSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Kính {suffix}",
        [
            new SkuSeed($"GLASS-{suffix}", new Dictionary<string, string>(), 150000, 5, false)
        ]);
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), price = 150000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var updatedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        Assert.True(updatedProduct.InStock);
        Assert.Equal(150000, updatedProduct.MinPrice);
    }

    [Fact]
    // Đổi tên sản phẩm tránh slug hiện tại của sản phẩm khác bằng cách thêm hậu tố số.
    public async Task UpdateProduct_Rename_AvoidsOtherProductCurrentSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var baseName = $"Bàn Ăn {suffix}";
        var prodA = await SeedProductInDbAsync(category.Id, brand.Id, baseName,
        [
            new SkuSeed($"TABLE-A-{suffix}", new Dictionary<string, string>(), 1000000, 5, true)
        ]);
        _createdProductIds.Add(prodA.Id);

        var prodB = await SeedProductInDbAsync(category.Id, brand.Id, $"Bàn Riêng {suffix}",
        [
            new SkuSeed($"TABLE-B-{suffix}", new Dictionary<string, string>(), 1000000, 5, true)
        ]);
        _createdProductIds.Add(prodB.Id);

        // Đổi tên sản phẩm B trùng với tên của sản phẩm A.
        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{prodB.Id}",
            new { name = baseName }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);

        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(patchRes);
        var secondSlug = json.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        // Slug mới của B phải tránh slug hiện tại của A bằng hậu tố.
        Assert.NotEqual(prodA.Slug, secondSlug);
        Assert.StartsWith(prodA.Slug, secondSlug);
    }

    [Fact]
    // Giữ nguyên tên của sản phẩm có slug hậu tố không làm thay đổi slug hiện có.
    public async Task UpdateProduct_UnchangedName_PreservesCollisionSuffixedSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var baseName = $"Gương {suffix}";
        var prodA = await SeedProductInDbAsync(category.Id, brand.Id, baseName,
        [
            new SkuSeed($"MIR-A-{suffix}", new Dictionary<string, string>(), 200000, 5, true)
        ]);
        _createdProductIds.Add(prodA.Id);

        // Tạo B cùng tên để nhận slug hậu tố -2.
        var createReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products",
            CreateDefaultProductPayload(category.Id, brand.Id, baseName), _adminCookie);
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        using var createJson = await ActorHttp.ReadJsonAsync(createRes);
        var secondProductId = createJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var secondSlug = createJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;
        _createdProductIds.Add(secondProductId);

        // Cập nhật mô tả mà không đổi tên sản phẩm.
        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{secondProductId}",
            new { shortDesc = "Mô tả sửa" }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        using var patchJson = await ActorHttp.ReadJsonAsync(patchRes);
        var currentSlug = patchJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        // Slug giữ nguyên hậu tố số hai ban đầu.
        Assert.Equal(secondSlug, currentSlug);
    }

    [Fact]
    // Tên thay đổi nhưng slug không đổi thì không tạo bản ghi mới trong lịch sử slug.
    public async Task UpdateProduct_NameChangesButSlugDoesNotChange_DoesNotCreateHistory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm {suffix}",
        [
            new SkuSeed($"PROD-{suffix}", new Dictionary<string, string>(), 100000, 5, true)
        ]);
        _createdProductIds.Add(product.Id);

        // Đổi sang tên có cùng cách phát âm không dấu để giữ nguyên slug.
        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}",
            new { name = $"SAN PHAM {suffix}" }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        await using var catalogDatabase = _database.CreateCatalogContext();
        var historyCount = await catalogDatabase.ProductSlugHistories.CountAsync(history => history.ProductId == product.Id);
        Assert.Equal(0, historyCount);
    }

    [Fact]
    // Đổi tên lưu slug cũ vào lịch sử và cho phép chính sản phẩm đó nhận lại slug cũ.
    public async Task UpdateProduct_SlugSelfHistoryReclaim_AndAvoidsOtherProductsActiveAndHistorySlugs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var baseName = $"Chuột Gaming {suffix}";
        var firstCreateReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products",
            CreateDefaultProductPayload(category.Id, brand.Id, baseName), _adminCookie);
        var firstCreateRes = await _client.SendAsync(firstCreateReq);
        Assert.Equal(HttpStatusCode.Created, firstCreateRes.StatusCode);
        using var firstJson = await ActorHttp.ReadJsonAsync(firstCreateRes);
        var firstProductId = firstJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var originalSlug = firstJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;
        _createdProductIds.Add(firstProductId);

        // Đổi tên sản phẩm một sang tên mới để đẩy slug ban đầu vào bảng lịch sử.
        var renameReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{firstProductId}",
            new { name = $"Chuột Gaming V2 {suffix}" }, _adminCookie);
        var renameRes = await _client.SendAsync(renameReq);
        Assert.Equal(HttpStatusCode.OK, renameRes.StatusCode);

        // Kiểm tra bảng lịch sử slug đã lưu lại đường dẫn cũ của sản phẩm một.
        await using (var catalogDatabase = _database.CreateCatalogContext())
        {
            var historyRecord = await catalogDatabase.ProductSlugHistories.FirstOrDefaultAsync(history => history.ProductId == firstProductId && history.OldSlug == originalSlug);
            Assert.NotNull(historyRecord);
        }

        // Tạo sản phẩm hai trùng tên để tránh slug lịch sử của sản phẩm khác.
        var secondCreateReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products",
            CreateDefaultProductPayload(category.Id, brand.Id, baseName), _adminCookie);
        var secondCreateRes = await _client.SendAsync(secondCreateReq);
        Assert.Equal(HttpStatusCode.Created, secondCreateRes.StatusCode);
        using var secondJson = await ActorHttp.ReadJsonAsync(secondCreateRes);
        var secondProductId = secondJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var prod2Slug = secondJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;
        _createdProductIds.Add(secondProductId);

        // Sản phẩm hai không được chiếm slug trong lịch sử của sản phẩm một và phải có hậu tố số.
        Assert.NotEqual(originalSlug, prod2Slug);
        Assert.StartsWith(originalSlug, prod2Slug);

        // Đổi tên sản phẩm một về tên cũ để nhận lại slug của chính mình.
        var reclaimReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{firstProductId}",
            new { name = baseName }, _adminCookie);
        var reclaimRes = await _client.SendAsync(reclaimReq);
        Assert.Equal(HttpStatusCode.OK, reclaimRes.StatusCode);
        using var reclaimJson = await ActorHttp.ReadJsonAsync(reclaimRes);
        var reclaimedSlug = reclaimJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        // Sản phẩm một nhận lại đúng slug ban đầu của chính nó.
        Assert.Equal(originalSlug, reclaimedSlug);

        // Bản ghi lịch sử trùng lặp với slug hiện tại của chính sản phẩm phải được xóa bỏ.
        await using (var catalogDatabase = _database.CreateCatalogContext())
        {
            var historyCount = await catalogDatabase.ProductSlugHistories.CountAsync(history => history.ProductId == firstProductId && history.OldSlug == originalSlug);
            Assert.Equal(0, historyCount);
        }
    }

    [Fact]
    // Cập nhật từng phần giữ nguyên các trường không gửi trong yêu cầu cập nhật.
    public async Task UpdateProduct_PartialPatch_PreservesUnspecifiedFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Tủ Lạnh {suffix}",
        [
            new SkuSeed($"FRIDGE-{suffix}", new Dictionary<string, string>(), 12000000, 5, true)
        ]);
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            shortDesc = "Mô tả ngắn mới vừa cập nhật"
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Phản hồi thành công với mã 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");

        // Mô tả ngắn được cập nhật trong khi tên và danh mục giữ nguyên.
        Assert.Equal("Mô tả ngắn mới vừa cập nhật", data.GetProperty("shortDesc").GetString());
        Assert.Equal($"Tủ Lạnh {suffix}", data.GetProperty("name").GetString());
        Assert.Equal(category.Id, data.GetProperty("categoryId").GetString());
        Assert.Equal(brand.Id, data.GetProperty("brandId").GetString());

        // Kiểm tra biến thể và tồn kho vẫn được giữ nguyên đầy đủ.
        var variants = data.GetProperty("variants").EnumerateArray().ToList();
        Assert.Single(variants);
        Assert.Equal(5, variants[0].GetProperty("stock").GetInt32());
    }

    [Fact]
    // Đổi trục tùy chọn mà không gửi danh sách biến thể phải bị từ chối nếu không khớp tổ hợp.
    public async Task UpdateProduct_OptionsChangedWithoutVariants_FailsWhenCombinationsDoNotMatch()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Giày {suffix}",
        [
            new SkuSeed($"SHOE-40-{suffix}", new Dictionary<string, string> { ["Size"] = "40" }, 900000, 10, true)
        ],
        options: new object[] { new { name = "Size", values = new[] { "40" } } });
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            options = new[]
            {
                new { name = "Size", values = new[] { "40", "41" } }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Thiếu danh sách biến thể tương ứng cho trục tùy chọn mới phải nhận mã 400.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra cơ sở dữ liệu giữ nguyên tùy chọn cũ khi yêu cầu bị từ chối.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var unchangedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        var optionsJson = unchangedProduct.Options.RootElement.ToString();
        Assert.DoesNotContain("41", optionsJson);
    }

    [Fact]
    // Cập nhật với dữ liệu không hợp lệ bảo toàn cơ sở dữ liệu và không phát sự kiện.
    public async Task UpdateProduct_InvalidInput_PreservesDatabaseAndEmitsZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var initialName = $"Quạt Đứng {suffix}";
        var product = await SeedProductInDbAsync(category.Id, brand.Id, initialName,
        [
            new SkuSeed($"FAN-{suffix}", new Dictionary<string, string>(), 300000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);

        // Thử cập nhật với giá âm và tên mới.
        var eventsBefore = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        var badPricePayload = new
        {
            name = $"Tên Mới Sai Giá {suffix}",
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), price = -100, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5 }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", badPricePayload, _adminCookie);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra tên trong cơ sở dữ liệu không hề bị cập nhật.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var unchangedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(initialName, unchangedProduct.Name);

        // Số sự kiện không thay đổi.
        var eventsAfter = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        Assert.Equal(eventsBefore, eventsAfter);
    }

    [Fact]
    // Đổi danh mục mà không gửi thông số mới vẫn phải kiểm tra thông số cũ theo danh mục đích.
    public async Task UpdateProduct_ValidatesMergedTargetSpecsWhenSpecsNotSubmitted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var oldCategory = await CreateCategoryInDbAsync($"Điện Thoại {suffix}", null,
        [
            new AttributeDefinition { Key = "pin_mah", Label = "Dung lượng pin", Group = "Pin", Type = "number", Required = false, Filterable = true }
        ]);

        var targetCategory = await CreateCategoryInDbAsync($"Đồng Hồ {suffix}", null,
        [
            new AttributeDefinition { Key = "strap_material", Label = "Chất liệu dây", Group = "Dây", Type = "text", Required = true, Filterable = true }
        ]);

        var brand = await CreateBrandInDbAsync($"Hãng {suffix}");

        var product = await SeedProductInDbAsync(oldCategory.Id, brand.Id, $"Thiết Bị {suffix}",
        [
            new SkuSeed($"DEV-{suffix}", new Dictionary<string, string>(), 100000, 5, true)
        ],
        specs: new { pin_mah = 5000 });
        _createdProductIds.Add(product.Id);

        var patchPayload = new
        {
            categoryId = targetCategory.Id
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}", patchPayload, _adminCookie);
        var response = await _client.SendAsync(request);

        // Danh mục mới yêu cầu thuộc tính bắt buộc nên thông số cũ bị đánh dấu không hợp lệ.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("SPECS_INVALID", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra danh mục của sản phẩm trong cơ sở dữ liệu không bị thay đổi.
        await using var catalogDatabase = _database.CreateCatalogContext();
        var unchangedProduct = await catalogDatabase.Products.FirstAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(oldCategory.Id, unchangedProduct.CategoryId);
    }

    [Fact]
    // Nhân viên bán hàng xem được danh sách sản phẩm ở mọi trạng thái hệ thống.
    public async Task GetProducts_Sales_ReturnsEveryProductStatus()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var prodDraft = await SeedProductInDbAsync(category.Id, brand.Id, $"Nháp {suffix}",
            [new SkuSeed($"DRAFT-{suffix}", new Dictionary<string, string>(), 100000, 5, true)], status: ProductStatus.Draft);
        var prodActive = await SeedProductInDbAsync(category.Id, brand.Id, $"Hoạt Động {suffix}",
            [new SkuSeed($"ACT-{suffix}", new Dictionary<string, string>(), 100000, 5, true)], status: ProductStatus.Active);
        var prodHidden = await SeedProductInDbAsync(category.Id, brand.Id, $"Ẩn {suffix}",
            [new SkuSeed($"HID-{suffix}", new Dictionary<string, string>(), 100000, 5, true)], status: ProductStatus.Hidden);
        var prodDisc = await SeedProductInDbAsync(category.Id, brand.Id, $"Ngừng {suffix}",
            [new SkuSeed($"DISC-{suffix}", new Dictionary<string, string>(), 100000, 5, true)], status: ProductStatus.Discontinued);

        _createdProductIds.AddRange([prodDraft.Id, prodActive.Id, prodHidden.Id, prodDisc.Id]);

        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?search={suffix}", _salesCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var itemIds = items.Select(item => item.GetProperty("id").GetString()).ToHashSet();

        Assert.Contains(prodDraft.Id, itemIds);
        Assert.Contains(prodActive.Id, itemIds);
        Assert.Contains(prodHidden.Id, itemIds);
        Assert.Contains(prodDisc.Id, itemIds);
    }

    [Fact]
    // Xem chi tiết sản phẩm bằng vai trò bán hàng trả về đúng mọi trạng thái sản phẩm.
    public async Task GetProductById_Sales_ReturnsEveryProductStatus()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var prodDisc = await SeedProductInDbAsync(category.Id, brand.Id, $"Chi Tiết Ngừng {suffix}",
            [new SkuSeed($"DISC-DET-{suffix}", new Dictionary<string, string>(), 100000, 5, true)], status: ProductStatus.Discontinued);
        _createdProductIds.Add(prodDisc.Id);

        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products/{prodDisc.Id}", _salesCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var status = json.RootElement.GetProperty("data").GetProperty("status").GetString();
        Assert.Equal("discontinued", status);
    }

    [Fact]
    // Chi tiết sản phẩm trả về đầy đủ các trường thông số, mô tả, tùy chọn và chi tiết biến thể.
    public async Task GetProductById_ReturnsSpecsDescriptionOptionsAndCompleteVariantFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null,
            [new AttributeDefinition { Key = "color_code", Label = "Mã màu", Group = "Chung", Type = "text", Required = false, Filterable = true }]);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Đầy Đủ Chi Tiết {suffix}",
            [new SkuSeed($"FULL-{suffix}", new Dictionary<string, string> { ["Màu"] = "Đỏ" }, 150000, 10, true, CompareAtPrice: 200000, WeightGrams: 300, LengthCm: 20, WidthCm: 15, HeightCm: 10)],
            options: new object[] { new { name = "Màu", values = new[] { "Đỏ" } } },
            specs: new { color_code = "RED-01" });
        _createdProductIds.Add(product.Id);

        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products/{product.Id}", _salesCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");

        Assert.True(data.TryGetProperty("specs", out var specs) && specs.ValueKind == JsonValueKind.Object);
        Assert.True(data.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.Object);
        Assert.True(data.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array);
        Assert.Equal(12, data.GetProperty("warrantyMonths").GetInt32());

        var variant = data.GetProperty("variants")[0];
        Assert.Equal(150000, variant.GetProperty("price").GetInt32());
        Assert.Equal(200000, variant.GetProperty("compareAtPrice").GetInt32());
        Assert.Equal(10, variant.GetProperty("stock").GetInt32());
        Assert.True(variant.GetProperty("enabled").GetBoolean());
        Assert.Equal(300, variant.GetProperty("weightGrams").GetInt32());
        Assert.Equal(20, variant.GetProperty("lengthCm").GetInt32());
        Assert.Equal(15, variant.GetProperty("widthCm").GetInt32());
        Assert.Equal(10, variant.GetProperty("heightCm").GetInt32());
    }

    [Fact]
    // Tìm kiếm theo mã SKU không phân biệt chữ hoa chữ thường.
    public async Task GetProducts_SearchSku_IsCaseInsensitive()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Tìm Kiếm {suffix}",
            [new SkuSeed($"UPPER-CASE-{suffix}".ToUpperInvariant(), new Dictionary<string, string>(), 100000, 5, true)]);
        _createdProductIds.Add(product.Id);

        // Tìm kiếm bằng chuỗi chữ thường tương ứng.
        var lowerQuery = $"upper-case-{suffix}".ToLowerInvariant();
        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?search={lowerQuery}", _salesCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetString() == product.Id);
    }

    [Fact]
    // Phân trang trả về danh sách phần tử riêng biệt ở các trang và tổng số dòng chính xác.
    public async Task GetProducts_Paging_ReturnsLimitedDistinctPagesAndExactFilteredTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var prod1 = await SeedProductInDbAsync(category.Id, brand.Id, $"Trang Một {suffix}",
            [new SkuSeed($"PG1-{suffix}", new Dictionary<string, string>(), 100000, 5, true)]);
        var prod2 = await SeedProductInDbAsync(category.Id, brand.Id, $"Trang Hai {suffix}",
            [new SkuSeed($"PG2-{suffix}", new Dictionary<string, string>(), 100000, 5, true)]);
        _createdProductIds.AddRange([prod1.Id, prod2.Id]);

        // Yêu cầu trang một với giới hạn một phần tử.
        var firstRequest = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?search={suffix}&page=1&limit=1", _adminCookie);
        var firstResponse = await _client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstJson = await ActorHttp.ReadJsonAsync(firstResponse);
        var data1 = firstJson.RootElement.GetProperty("data");
        Assert.Equal(2, data1.GetProperty("total").GetInt32());
        var firstPageProductId = data1.GetProperty("items")[0].GetProperty("id").GetString()!;

        // Yêu cầu trang hai với giới hạn một phần tử.
        var secondRequest = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?search={suffix}&page=2&limit=1", _adminCookie);
        var secondResponse = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondJson = await ActorHttp.ReadJsonAsync(secondResponse);
        var data2 = secondJson.RootElement.GetProperty("data");
        var secondPageProductId = data2.GetProperty("items")[0].GetProperty("id").GetString()!;

        // Hai phần tử trên hai trang riêng biệt không được trùng nhau.
        Assert.NotEqual(firstPageProductId, secondPageProductId);
    }

    [Fact]
    // Lọc theo trạng thái nháp chỉ trả về sản phẩm nháp và bảo toàn tổng số lượng.
    public async Task GetProducts_FilterDraftStatus_ReturnsOnlyDraftProductsAndExactTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Tạo sản phẩm với các trạng thái khác nhau để kiểm tra độ chính xác của bộ lọc.
        var prodDraft = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Nháp {suffix}",
            [new SkuSeed($"DFT-{suffix}", new Dictionary<string, string>(), 100000, 10, true)], status: ProductStatus.Draft);
        var prodActive = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Bán {suffix}",
            [new SkuSeed($"ACT-{suffix}", new Dictionary<string, string>(), 100000, 10, true)], status: ProductStatus.Active);
        _createdProductIds.AddRange([prodDraft.Id, prodActive.Id]);

        // Gửi yêu cầu lọc danh sách theo trạng thái nháp kết hợp từ khóa phân biệt.
        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?status=draft&search={suffix}", _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());

        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(prodDraft.Id, items[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("khong_hop_le")]
    [InlineData("draft,hidden")]
    [InlineData("draft&status=active")]
    [InlineData("1")]
    // Từ chối danh sách và số vì API chỉ cho lọc đúng một trạng thái sản phẩm.
    public async Task GetProducts_InvalidStatus_Returns400ValidationError(string statusQuery)
    {
        // Gửi cả giá trị lạ, nhiều trạng thái và số để tránh lọc sai sản phẩm.
        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?status={statusQuery}", _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
    }

    [Fact]
    // Lọc theo danh mục gốc phải bao gồm sản phẩm của toàn bộ danh mục con trực thuộc.
    public async Task GetProducts_FilterRootCategory_IncludesDescendants()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rootCategory = await CreateCategoryInDbAsync($"Gốc {suffix}", null, []);
        var childCategory = await CreateCategoryInDbAsync($"Con {suffix}", rootCategory.Id, []);
        var otherCategory = await CreateCategoryInDbAsync($"Khác {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Tạo sản phẩm thuộc danh mục con và sản phẩm thuộc danh mục khác để đối chiếu.
        var prodChild = await SeedProductInDbAsync(childCategory.Id, brand.Id, $"Sản Phẩm Con {suffix}",
            [new SkuSeed($"CHD-{suffix}", new Dictionary<string, string>(), 100000, 10, true)]);
        var prodOther = await SeedProductInDbAsync(otherCategory.Id, brand.Id, $"Sản Phẩm Khác {suffix}",
            [new SkuSeed($"OTH-{suffix}", new Dictionary<string, string>(), 100000, 10, true)]);
        _createdProductIds.AddRange([prodChild.Id, prodOther.Id]);

        // Gửi yêu cầu lọc danh sách theo mã định danh danh mục gốc.
        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?categoryId={rootCategory.Id}&search={suffix}", _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());

        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(prodChild.Id, items[0].GetProperty("id").GetString());
    }

    [Fact]
    // Lọc sắp hết hàng chỉ nhận biến thể bật có tồn kho từ năm trở xuống.
    public async Task GetProducts_FilterLowStock_AppliesThresholdAndExcludesDisabledZero()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Tạo biến thể bị tắt có tồn kho bằng không nhằm kiểm tra loại trừ.
        var prodDisabledZero = await SeedProductInDbAsync(category.Id, brand.Id, $"Tắt Tồn Không {suffix}",
            [new SkuSeed($"DIS0-{suffix}", new Dictionary<string, string>(), 100000, 0, false)]);
        // Tạo biến thể bật có tồn kho bằng năm để kiểm tra ngưỡng cận trên.
        var prodStockFive = await SeedProductInDbAsync(category.Id, brand.Id, $"Bật Tồn Năm {suffix}",
            [new SkuSeed($"STK5-{suffix}", new Dictionary<string, string>(), 100000, 5, true)]);
        // Tạo biến thể bật có tồn kho bằng sáu để kiểm tra điều kiện vượt ngưỡng.
        var prodStockSix = await SeedProductInDbAsync(category.Id, brand.Id, $"Bật Tồn Sáu {suffix}",
            [new SkuSeed($"STK6-{suffix}", new Dictionary<string, string>(), 100000, 6, true)]);
        // Tạo biến thể bật có tồn kho bằng bốn để kiểm tra trường hợp nằm dưới ngưỡng.
        var prodStockFour = await SeedProductInDbAsync(category.Id, brand.Id, $"Bật Tồn Bốn {suffix}",
            [new SkuSeed($"STK4-{suffix}", new Dictionary<string, string>(), 100000, 4, true)]);
        // Một biến thể bật ít hàng đủ để cả sản phẩm xuất hiện dù biến thể khác còn nhiều.
        var prodMixedStock = await SeedProductInDbAsync(category.Id, brand.Id, $"Tồn Kho Lẫn {suffix}",
        [
            new SkuSeed($"MIX-LOW-{suffix}", new Dictionary<string, string> { ["Màu"] = "Đỏ" }, 100000, 3, true),
            new SkuSeed($"MIX-HIGH-{suffix}", new Dictionary<string, string> { ["Màu"] = "Xanh" }, 100000, 20, true)
        ]);
        // Biến thể tắt ít hàng không được làm sản phẩm có biến thể bật còn nhiều xuất hiện.
        var prodMixedDisabled = await SeedProductInDbAsync(category.Id, brand.Id, $"Tắt Lẫn {suffix}",
        [
            new SkuSeed($"OFF-LOW-{suffix}", new Dictionary<string, string> { ["Màu"] = "Đỏ" }, 100000, 0, false),
            new SkuSeed($"ON-HIGH-{suffix}", new Dictionary<string, string> { ["Màu"] = "Xanh" }, 100000, 20, true)
        ]);
        _createdProductIds.AddRange([prodDisabledZero.Id, prodStockFive.Id, prodStockSix.Id, prodStockFour.Id, prodMixedStock.Id, prodMixedDisabled.Id]);

        // Gửi yêu cầu lọc danh sách sản phẩm sắp hết hàng theo tham số lowStock.
        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products?lowStock=true&search={suffix}", _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt32());

        var itemIds = data.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).ToHashSet();
        Assert.Contains(prodStockFive.Id, itemIds);
        Assert.Contains(prodStockFour.Id, itemIds);
        Assert.Contains(prodMixedStock.Id, itemIds);
        Assert.DoesNotContain(prodMixedDisabled.Id, itemIds);
        Assert.DoesNotContain(prodDisabledZero.Id, itemIds);
        Assert.DoesNotContain(prodStockSix.Id, itemIds);
    }

    [Fact]
    // Sai kiểu boolean phải báo lỗi thay vì âm thầm bỏ bộ lọc tồn kho.
    public async Task GetProducts_InvalidLowStock_Returns400ValidationError()
    {
        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/admin/products?lowStock=abc", _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Kết hợp tìm kiếm, danh mục, trạng thái và tồn kho tác động đồng thời lên tổng số và danh sách.
    public async Task GetProducts_CombinedFilters_FiltersTotalAndItemsPreservingPagination()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var targetCategory = await CreateCategoryInDbAsync($"Mục Đích {suffix}", null, []);
        var otherCategory = await CreateCategoryInDbAsync($"Mục Khác {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        // Tạo sản phẩm thỏa mãn toàn bộ bốn điều kiện lọc kết hợp.
        var matchedProduct = await SeedProductInDbAsync(targetCategory.Id, brand.Id, $"Trùng Khớp {suffix}",
            [new SkuSeed($"MATCH-{suffix}", new Dictionary<string, string>(), 100000, 3, true)], status: ProductStatus.Draft);
        // Tạo sản phẩm sai trạng thái để kiểm tra tính độc lập của bộ lọc.
        var wrongStatusProduct = await SeedProductInDbAsync(targetCategory.Id, brand.Id, $"Sai Trạng Thái {suffix}",
            [new SkuSeed($"WST-{suffix}", new Dictionary<string, string>(), 100000, 3, true)], status: ProductStatus.Active);
        // Tạo sản phẩm sai danh mục để kiểm tra tính độc lập của bộ lọc.
        var wrongCategoryProduct = await SeedProductInDbAsync(otherCategory.Id, brand.Id, $"Sai Danh Mục {suffix}",
            [new SkuSeed($"WCT-{suffix}", new Dictionary<string, string>(), 100000, 3, true)], status: ProductStatus.Draft);
        // Tạo sản phẩm sai mức tồn kho để kiểm tra tính độc lập của bộ lọc.
        var excessStockProduct = await SeedProductInDbAsync(targetCategory.Id, brand.Id, $"Dư Tồn Kho {suffix}",
            [new SkuSeed($"EXC-{suffix}", new Dictionary<string, string>(), 100000, 10, true)], status: ProductStatus.Draft);
        _createdProductIds.AddRange([matchedProduct.Id, wrongStatusProduct.Id, wrongCategoryProduct.Id, excessStockProduct.Id]);

        // Gửi yêu cầu kết hợp bốn bộ lọc cùng tham số phân trang.
        var uri = $"/api/admin/products?search={suffix}&categoryId={targetCategory.Id}&status=draft&lowStock=true&page=1&limit=10";
        var request = ActorHttp.CreateRequest(HttpMethod.Get, uri, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());

        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(matchedProduct.Id, items[0].GetProperty("id").GetString());
    }

    [Fact]
    // Cập nhật sản phẩm thành công phát đúng một sự kiện loại cập nhật.
    public async Task UpdateProduct_Success_EmitsExactlyOneUpdatedEvent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Bình Nước {suffix}",
            [new SkuSeed($"BOT-{suffix}", new Dictionary<string, string>(), 100000, 5, true)]);
        _createdProductIds.Add(product.Id);

        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}",
            new { shortDesc = "Mô tả ngắn cập nhật" }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // Đếm toàn bộ sự kiện của sản phẩm và khẳng định chỉ có một sự kiện Updated.
        var allEvents = _factory.DomainEvents.OfType<ProductChangedEvent>()
            .Where(domainEvent => domainEvent.ProductId == product.Id)
            .ToList();
        Assert.Single(allEvents);
        Assert.Equal(ProductChange.Updated, allEvents[0].Change);
    }

    [Fact]
    // Xem chi tiết sản phẩm trả về đầy đủ các biến thể bao gồm cả các biến thể đã bị tắt.
    public async Task GetProductById_ReturnsDetailWithAllVariantsIncludingDisabled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Chi Tiết {suffix}",
        [
            new SkuSeed($"ENABLED-SKU-{suffix}", new Dictionary<string, string> { ["Màu"] = "Đỏ" }, 100000, 10, true),
            new SkuSeed($"DISABLED-SKU-{suffix}", new Dictionary<string, string> { ["Màu"] = "Vàng" }, 100000, 0, false)
        ],
        options: new object[] { new { name = "Màu", values = new[] { "Đỏ", "Vàng" } } });
        _createdProductIds.Add(product.Id);

        var request = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products/{product.Id}", _salesCookie);
        var response = await _client.SendAsync(request);

        // Đọc thông tin chi tiết sản phẩm thành công với mã 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var variants = data.GetProperty("variants").EnumerateArray().ToList();

        // Danh sách biến thể bao gồm cả biến thể đang hoạt động và biến thể đã tắt.
        Assert.Equal(2, variants.Count);
        Assert.Contains(variants, item => item.GetProperty("enabled").GetBoolean());
        Assert.Contains(variants, item => !item.GetProperty("enabled").GetBoolean());

        // Tra cứu mã sản phẩm không tồn tại phải trả mã lỗi 404 PRODUCT_NOT_FOUND.
        var nonExistentId = Guid.NewGuid().ToString("N");
        var notFoundReq = ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products/{nonExistentId}", _salesCookie);
        var notFoundRes = await _client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);
        using var notFoundJson = await ActorHttp.ReadJsonAsync(notFoundRes);
        Assert.Equal("PRODUCT_NOT_FOUND", notFoundJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Vai trò bán hàng xem được danh sách và chi tiết nhưng bị cấm khi gọi tạo mới hoặc cập nhật.
    public async Task Sales_CanGetProducts_CannotCreateOrPatchProduct()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Danh Mục {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Thương Hiệu {suffix}");

        var product = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản Phẩm Cấm Sửa {suffix}",
        [
            new SkuSeed($"FORBIDDEN-{suffix}", new Dictionary<string, string>(), 100000, 10, true)
        ]);
        _createdProductIds.Add(product.Id);

        // Gọi POST tạo mới sản phẩm với quyền bán hàng bị chặn bằng mã lỗi 403.
        var postReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products",
            CreateDefaultProductPayload(category.Id, brand.Id, $"Sản Phẩm Lậu {suffix}"), _salesCookie);
        var postRes = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);

        // Gọi PATCH cập nhật sản phẩm với quyền bán hàng bị chặn bằng mã lỗi 403.
        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{product.Id}",
            new { name = "Tên Mới" }, _salesCookie);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.Forbidden, patchRes.StatusCode);
    }

    [Fact]
    // Cập nhật sản phẩm với định danh không tồn tại phải trả mã lỗi 404 PRODUCT_NOT_FOUND.
    public async Task UpdateProduct_NonExistentId_Returns404()
    {
        var nonExistentId = Guid.NewGuid().ToString("N");
        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{nonExistentId}",
            new { name = "Tên Không Tồn Tại" }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);

        // Báo lỗi 404 khi không tìm thấy thực thể cần cập nhật.
        Assert.Equal(HttpStatusCode.NotFound, patchRes.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(patchRes);
        Assert.Equal("PRODUCT_NOT_FOUND", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("specs", "SPECS_INVALID", 400)]
    [InlineData("description", "DESCRIPTION_INVALID", 400)]
    [InlineData("price", "VALIDATION_ERROR", 400)]
    [InlineData("combinations", "INVALID_PRODUCT_VARIANTS", 400)]
    [InlineData("code", "SKU_CODE_ALREADY_EXISTS", 409)]
    public async Task Patch_InvalidDomainInput_PreservesEntireProductAuditHistoryAndEvents(
        string invalidField, string expectedCode, int expectedStatus)
    {
        var suffix = Guid.NewGuid().ToString("N");
        // Thuộc tính bắt buộc giúp yêu cầu thông số sai có lỗi nghiệp vụ rõ ràng.
        var category = await CreateCategoryInDbAsync($"Kiểm tra PATCH {suffix}", null,
            [new AttributeDefinition
            {
                Key = "ram_gb", Label = "RAM", Group = "Bộ nhớ",
                Type = "number", Required = true, Filterable = true,
            }]);
        var brand = await CreateBrandInDbAsync($"Kiểm tra PATCH {suffix}");
        // Sản phẩm hợp lệ có tồn kho và giá cũ để phát hiện cập nhật bị ghi một phần.
        var existingProduct = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản phẩm gốc {suffix}",
            [new SkuSeed($"ORIGINAL-{suffix}", new Dictionary<string, string>(), 100000, 3, true)],
            specs: new { ram_gb = 16 });
        _createdProductIds.Add(existingProduct.Id);
        var variant = new Dictionary<string, object?>
        {
            ["optionValues"] = new Dictionary<string, string>(),
            ["price"] = 150000,
            ["enabled"] = true,
            ["weightGrams"] = 500,
            ["lengthCm"] = 20,
            ["widthCm"] = 15,
            ["heightCm"] = 10,
        };
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Tên không được lưu {suffix}",
            ["variants"] = new[] { variant },
        };
        // Mỗi ca chỉ vi phạm một quy tắc, tránh lỗi ở trường khác che mất đường ghi cần kiểm tra.
        switch (invalidField)
        {
            case "specs":
                // Chuỗi không hợp lệ cho thuộc tính số, dù nội dung trông giống một số nguyên.
                body["specs"] = new { ram_gb = "16" };
                break;
            case "description":
                // Nút `script` vi phạm whitelist ngay cả khi tài liệu vẫn là JSON hợp lệ.
                body["description"] = new { type = "doc", content = new[] { new { type = "script" } } };
                break;
            case "price":
                // Phần lẻ vi phạm quy tắc giá nguyên, dù giá vẫn dương và nằm trong giới hạn lưu trữ.
                variant["price"] = 150000.5m;
                break;
            case "combinations":
                // Sản phẩm không có tùy chọn chỉ được nhận tổ hợp rỗng.
                variant["optionValues"] = new Dictionary<string, string> { ["Màu"] = "Đỏ" };
                break;
            case "code":
                // Mã thuộc sản phẩm khác phải nhận 409, không được đổi tên hoặc giá của sản phẩm hiện tại.
                var otherProduct = await SeedProductInDbAsync(category.Id, brand.Id, $"Sản phẩm khác {suffix}",
                    [new SkuSeed($"CONFLICT-{suffix}", new Dictionary<string, string>(), 200000, 0, true)],
                    specs: new { ram_gb = 16 });
                _createdProductIds.Add(otherProduct.Id);
                variant["code"] = otherProduct.Skus.Single().Code;
                break;
            default:
                // Ca không được khai báo phải dừng ngay để không vô tình kiểm tra request hợp lệ.
                throw new ArgumentOutOfRangeException(nameof(invalidField));
        }

        var path = $"/api/admin/products/{existingProduct.Id}";
        // Lấy trạng thái sửa sản phẩm trước PATCH để so sánh mọi trường mà client có thể quan sát.
        using var beforeResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, _adminCookie));
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        using var beforeJson = await ActorHttp.ReadJsonAsync(beforeResponse);
        var beforeData = beforeJson.RootElement.GetProperty("data").GetRawText();
        var eventsBefore = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        // Body gửi cả tên và giá mới để lỗi nghiệp vụ phải bảo vệ toàn bộ thay đổi.
        using var patchResponse = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Patch, path, body, _adminCookie));
        Assert.Equal(expectedStatus, (int)patchResponse.StatusCode);
        using var errorJson = await ActorHttp.ReadJsonAsync(patchResponse);
        Assert.Equal(expectedCode, errorJson.RootElement.GetProperty("error").GetProperty("code").GetString());
        // Đọc lại dữ liệu chứng minh cả giá, tồn kho, thông số và thời điểm cập nhật đều giữ nguyên.
        using var afterResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, _adminCookie));
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        using var afterJson = await ActorHttp.ReadJsonAsync(afterResponse);
        Assert.Equal(beforeData, afterJson.RootElement.GetProperty("data").GetRawText());
        Assert.Equal(eventsBefore, _factory.DomainEvents.OfType<ProductChangedEvent>().Count);
        // Tên và giá gửi kèm không được tạo lịch sử hoặc audit khi PATCH bị từ chối.
        await using var catalogDatabase = _database.CreateCatalogContext();
        Assert.False(await catalogDatabase.ProductSlugHistories.AnyAsync(history => history.ProductId == existingProduct.Id));
        await using var identityDatabase = _database.CreateIdentityContext();
        var skuId = existingProduct.Skus.Single().Id;
        Assert.False(await identityDatabase.AuditLogs.AnyAsync(log => log.TargetId == skuId));
    }

    [Theory]
    [InlineData("weightGrams", 0)]
    [InlineData("lengthCm", 0)]
    [InlineData("widthCm", 0)]
    [InlineData("heightCm", 0)]
    [InlineData("weightGrams", -1)]
    [InlineData("lengthCm", -1)]
    [InlineData("widthCm", -1)]
    [InlineData("heightCm", -1)]
    public async Task EnabledSku_InvalidIndividualPackageMeasurement_RejectsCreateAndPatchWithoutChanges(
        string measurementName, int? measurementValue)
    {
        var suffix = Guid.NewGuid().ToString("N");
        // Dữ liệu riêng giúp mỗi số đo được kiểm tra độc lập, không dùng lỗi của trường khác.
        var category = await CreateCategoryInDbAsync($"Số đo {suffix}", null, []);
        var brand = await CreateBrandInDbAsync($"Số đo {suffix}");
        var variant = new Dictionary<string, object?>
        {
            ["optionValues"] = new Dictionary<string, string>(),
            ["price"] = 150000,
            ["enabled"] = true,
            ["weightGrams"] = 500,
            ["lengthCm"] = 20,
            ["widthCm"] = 15,
            ["heightCm"] = 10,
        };
        // Chỉ làm sai một số đo để các quy tắc khác không che mất lỗi cần phát hiện.
        variant[measurementName] = measurementValue;
        var invalidName = $"Thiếu số đo {suffix}";
        var createBody = new
        {
            categoryId = category.Id,
            brandId = brand.Id,
            name = invalidName,
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            variants = new[] { variant },
        };
        var eventsBeforeCreate = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        // POST phải từ chối trước khi ghi sản phẩm có SKU bật thiếu số đo hợp lệ.
        using var createResponse = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", createBody, _adminCookie));
        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.Equal(eventsBeforeCreate, _factory.DomainEvents.OfType<ProductChangedEvent>().Count);
        // DB không được chứa sản phẩm từ yêu cầu POST bị từ chối.
        await using var catalogDatabase = _database.CreateCatalogContext();
        Assert.False(await catalogDatabase.Products.AnyAsync(product => product.Name == invalidName));

        // SKU hợp lệ có tồn kho để PATCH lỗi phải giữ cả dữ liệu thương mại và tồn kho.
        var existingProduct = await SeedProductInDbAsync(category.Id, brand.Id, $"Đủ số đo {suffix}",
            [new SkuSeed($"MEASUREMENT-{suffix}", new Dictionary<string, string>(), 100000, 3, true)]);
        _createdProductIds.Add(existingProduct.Id);
        var path = $"/api/admin/products/{existingProduct.Id}";
        // Ghi nhận toàn bộ dữ liệu sửa sản phẩm trước yêu cầu PATCH lỗi.
        using var beforeResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, _adminCookie));
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        using var beforeJson = await ActorHttp.ReadJsonAsync(beforeResponse);
        var beforeData = beforeJson.RootElement.GetProperty("data").GetRawText();
        var eventsBeforePatch = _factory.DomainEvents.OfType<ProductChangedEvent>().Count;
        // Gửi cả tên và giá mới để phát hiện việc ghi một phần khi số đo sai.
        using var patchResponse = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Patch, path,
            new { name = $"Tên không được lưu {suffix}", variants = new[] { variant } }, _adminCookie));
        Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
        // Dữ liệu đọc lại phải giữ nguyên mọi trường, kể cả thời điểm cập nhật và kích thước.
        using var afterResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, _adminCookie));
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        using var afterJson = await ActorHttp.ReadJsonAsync(afterResponse);
        Assert.Equal(beforeData, afterJson.RootElement.GetProperty("data").GetRawText());
        Assert.Equal(eventsBeforePatch, _factory.DomainEvents.OfType<ProductChangedEvent>().Count);
        // PATCH bị từ chối không được tạo lịch sử slug hoặc nhật ký giá.
        Assert.False(await catalogDatabase.ProductSlugHistories.AnyAsync(history => history.ProductId == existingProduct.Id));
        await using var identityDatabase = _database.CreateIdentityContext();
        var skuId = existingProduct.Skus.Single().Id;
        Assert.False(await identityDatabase.AuditLogs.AnyAsync(log => log.TargetId == skuId));
    }

    private static object CreateDefaultProductPayload(string categoryId, string brandId, string name)
    {
        return new
        {
            categoryId,
            brandId,
            name,
            shortDesc = "Mô tả ngắn tiêu chuẩn",
            description = BuildValidDescriptionDocument(),
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string>(),
                    price = 100000,
                    enabled = true,
                    weightGrams = (int?)200,
                    lengthCm = (int?)15,
                    widthCm = (int?)10,
                    heightCm = (int?)5
                }
            }
        };
    }

    private static object BuildValidDescriptionDocument()
    {
        return new
        {
            type = "doc",
            content = new[]
            {
                new
                {
                    type = "paragraph",
                    content = new[]
                    {
                        new { type = "text", text = "Nội dung mô tả sản phẩm hợp lệ." }
                    }
                }
            }
        };
    }

    private async Task<Category> CreateCategoryInDbAsync(string name, string? parentId, IReadOnlyList<AttributeDefinition> attributes)
    {
        await using var catalogDatabase = _database.CreateCatalogContext();
        // Chuẩn hóa cấu trúc thuộc tính danh mục sang định dạng JSON.
        var attributesJson = JsonDocument.Parse(JsonSerializer.Serialize(attributes, AttributeDefinition.JsonOptions));
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = $"cat-{Guid.NewGuid():N}",
            ParentId = parentId,
            Position = 0,
            Attributes = attributesJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        // Thêm danh mục kiểm thử vào ngữ cảnh cơ sở dữ liệu.
        catalogDatabase.Categories.Add(category);
        // Lưu danh mục mới vào cơ sở dữ liệu để phục vụ kiểm thử.
        await catalogDatabase.SaveChangesAsync();
        // Ghi nhận mã danh mục để dọn dẹp sau khi kết thúc kiểm thử.
        _createdCategoryIds.Add(category.Id);
        return category;
    }

    private async Task<Brand> CreateBrandInDbAsync(string name)
    {
        await using var catalogDatabase = _database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = $"brand-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        // Thêm thương hiệu kiểm thử vào ngữ cảnh cơ sở dữ liệu.
        catalogDatabase.Brands.Add(brand);
        // Lưu thương hiệu mới vào cơ sở dữ liệu để phục vụ kiểm thử.
        await catalogDatabase.SaveChangesAsync();
        // Ghi nhận mã thương hiệu để dọn dẹp sau khi kiểm thử xong.
        _createdBrandIds.Add(brand.Id);
        return brand;
    }

    private async Task<Product> SeedProductInDbAsync(
        string categoryId,
        string brandId,
        string name,
        IReadOnlyList<SkuSeed> skus,
        object? options = null,
        object? specs = null,
        ProductStatus status = ProductStatus.Draft)
    {
        await using var catalogDatabase = _database.CreateCatalogContext();
        var productId = Guid.NewGuid().ToString("N");
        // Slug từ tên tạo đúng tình huống trùng đường dẫn và đổi tên không đổi slug trong các bài test.
        var slug = SlugGenerator.ToSlug(name);

        // Chuẩn hóa tài liệu mô tả và thông số kỹ thuật sang dạng JSON.
        var descJson = JsonDocument.Parse(JsonSerializer.Serialize(BuildValidDescriptionDocument()));
        var specsJson = JsonDocument.Parse(JsonSerializer.Serialize(specs ?? new { }));
        var optionsJson = JsonDocument.Parse(JsonSerializer.Serialize(options ?? Array.Empty<object>()));

        var product = new Product
        {
            Id = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Name = name,
            Slug = slug,
            ShortDesc = "Mô tả ngắn hạt giống",
            Description = descJson,
            Specs = specsJson,
            Options = optionsJson,
            WarrantyMonths = 12,
            Status = status,
            MinPrice = skus.Count > 0 ? skus.Where(sku => sku.Enabled).Min(sku => (int?)sku.Price) : null,
            InStock = skus.Any(sku => sku.Enabled && sku.Stock > 0),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var skuEntities = new List<(Sku Entity, int TargetStock)>();
        foreach (var skuSeed in skus)
        {
            var skuOptionJson = JsonDocument.Parse(JsonSerializer.Serialize(skuSeed.OptionValues));
            var sku = new Sku
            {
                Id = Guid.NewGuid().ToString("N"),
                ProductId = productId,
                Code = skuSeed.Code,
                OptionValues = skuOptionJson,
                Price = skuSeed.Price,
                CompareAtPrice = skuSeed.CompareAtPrice,
                // Tạo SKU ban đầu với mức tồn kho bằng không theo quy tắc kiểm thử.
                Stock = 0,
                Enabled = skuSeed.Enabled,
                WeightGrams = skuSeed.WeightGrams ?? 200,
                LengthCm = skuSeed.LengthCm ?? 15,
                WidthCm = skuSeed.WidthCm ?? 10,
                HeightCm = skuSeed.HeightCm ?? 5,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            product.Skus.Add(sku);
            skuEntities.Add((sku, skuSeed.Stock));
        }

        // Thêm sản phẩm cùng các biến thể khởi tạo vào cơ sở dữ liệu.
        catalogDatabase.Products.Add(product);
        // Lưu sản phẩm vào cơ sở dữ liệu với mức tồn kho mặc định bằng không.
        await catalogDatabase.SaveChangesAsync();

        // Cập nhật tồn kho kiểm thử qua lệnh SQL để tuân thủ quy tắc nghiệp vụ.
        foreach (var (sku, targetStock) in skuEntities)
        {
            if (targetStock > 0)
            {
                await catalogDatabase.Database.ExecuteSqlInterpolatedAsync($"UPDATE skus SET stock = {targetStock} WHERE id = {sku.Id}");
                sku.Stock = targetStock;

                var movement = new InventoryMovement
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SkuId = sku.Id,
                    Delta = targetStock,
                    Type = InventoryMovementType.Manual,
                    Reason = "Khởi tạo tồn kho kiểm thử",
                    CreatedAt = DateTime.UtcNow
                };
                // Tạo bản ghi biến động tồn kho thủ công tương ứng để giữ bất biến dữ liệu.
                catalogDatabase.InventoryMovements.Add(movement);
                await catalogDatabase.SaveChangesAsync();
                // Ghi nhận mã biến động tồn kho để dọn dẹp sau kiểm thử.
                _createdMovementIds.Add(movement.Id);
            }
        }

        return product;
    }

    private sealed record SkuSeed(
        string Code,
        IReadOnlyDictionary<string, string> OptionValues,
        int Price,
        int Stock,
        bool Enabled,
        int? CompareAtPrice = null,
        int? WeightGrams = null,
        int? LengthCm = null,
        int? WidthCm = null,
        int? HeightCm = null);

    private sealed class FailingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken)
        {
            // Cố ý ném ngoại lệ để kiểm tra rollback giao dịch cơ sở dữ liệu.
            throw new InvalidOperationException("Lỗi cố ý từ nhật ký kiểm toán");
        }
    }

    private sealed class DelegateProductChangedHandler(Func<ProductChangedEvent, Task> handle) : IDomainEventHandler<ProductChangedEvent>
    {
        public Task HandleAsync(ProductChangedEvent @event, CancellationToken cancellationToken = default)
        {
            return handle(@event);
        }
    }
}
