using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-06: chữ ký tải ảnh, gắn ảnh vào sản phẩm, kiểm lại ảnh và job xóa ảnh khi bỏ ảnh.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ProductImageTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private string _adminCookie = null!;
    private string _salesCookie = null!;
    private string _categoryId = null!;
    private string _brandId = null!;
    private readonly List<string> _productIds = new();

    public ProductImageTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
        var (_, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, $"img-admin-{_suffix}");
        _adminCookie = $"pk_session={adminToken}";
        var (_, salesToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.SalesCode, $"img-sales-{_suffix}");
        _salesCookie = $"pk_session={salesToken}";

        // Danh mục lá không có thuộc tính bắt buộc, để payload sản phẩm chỉ cần phần ảnh.
        await using var context = _database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Ảnh {_suffix}", Slug = $"anh-{_suffix}",
            Position = 0, Attributes = JsonDocument.Parse("[]"), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Hãng ảnh {_suffix}", Slug = $"hang-anh-{_suffix}",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        context.Categories.Add(category);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
        _brandId = brand.Id;
    }

    public async Task DisposeAsync()
    {
        // Kiểm bất biến trước khi dọn, vì dọn dữ liệu xóa luôn movement.
        await StockInvariant.AssertHoldsAsync(_database);
        await using var context = _database.CreateCatalogContext();
        await context.ImageDeletionJobs.Where(job => job.PublicId.Contains(_suffix)).ExecuteDeleteAsync();
        await context.Products.Where(product => _productIds.Contains(product.Id)).ExecuteDeleteAsync();
        await context.Categories.Where(category => category.Id == _categoryId).ExecuteDeleteAsync();
        await context.Brands.Where(brand => brand.Id == _brandId).ExecuteDeleteAsync();
    }

    // Mô phỏng trình duyệt vừa tải ảnh lên Cloudinary: ảnh có trong kho ảnh giả với kích thước và định dạng cho trước.
    private string UploadedImage(string name, long bytes = 200_000, string format = "jpg")
    {
        var publicId = $"{ImageUploadRules.Folder}/{_suffix}-{name}";
        _factory.ImageStorage.Add(publicId, bytes, format);
        return publicId;
    }

    private object ProductPayload(object[] images) => new
    {
        categoryId = _categoryId,
        brandId = _brandId,
        name = $"Điện thoại ảnh {_suffix}-{Guid.NewGuid():N}",
        shortDesc = "Mô tả ngắn",
        description = new { type = "doc", content = new[] { new { type = "paragraph" } } },
        specs = new { },
        options = Array.Empty<object>(),
        warrantyMonths = 12,
        variants = new[] { new { optionValues = new Dictionary<string, string>(), price = 100000, enabled = true } },
        images,
    };

    private static object Image(string publicId, string alt = "Mặt trước điện thoại", string? skuId = null) =>
        new { publicId, alt, skuId };

    private async Task<HttpResponseMessage> CreateProductAsync(object[] images)
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/admin/products", ProductPayload(images), _adminCookie));
        if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK)
        {
            using var json = await ActorHttp.ReadJsonAsync(response);
            _productIds.Add(json.RootElement.GetProperty("data").GetProperty("id").GetString()!);
        }

        return response;
    }

    private async Task<string> CreateProductIdAsync(object[] images)
    {
        var response = await CreateProductAsync(images);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return _productIds[^1];
    }

    private async Task<HttpResponseMessage> PatchImagesAsync(string productId, object[] images) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Patch, $"/api/admin/products/{productId}", new { images }, _adminCookie));

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Mong đợi {status}, nhận {response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<List<ImageDeletionJob>> JobsForAsync(string publicId)
    {
        await using var context = _database.CreateCatalogContext();
        return await context.ImageDeletionJobs.AsNoTracking().Where(job => job.PublicId == publicId).ToListAsync();
    }

    [Fact]
    // Chữ ký khớp giá trị tự tính bằng secret giả. Phản hồi không chứa secret ở bất kỳ trường nào.
    public async Task Signature_Admin_ReturnsVerifiableSignatureWithoutSecret()
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/admin/uploads/signature", null, _adminCookie));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(FakeImageStorage.Credentials.ApiSecret, body);
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("apiSecret", out _));
        Assert.Equal("jpg,png,webp", data.GetProperty("allowedFormats").GetString());
        Assert.Equal(5 * 1024 * 1024, data.GetProperty("maxFileBytes").GetInt64());
        Assert.Equal(ImageUploadRules.Folder, data.GetProperty("folder").GetString());

        var expected = CloudinarySignature.Sign(new Dictionary<string, string>
        {
            ["allowed_formats"] = "jpg,png,webp",
            ["folder"] = ImageUploadRules.Folder,
            ["timestamp"] = data.GetProperty("timestamp").GetInt64().ToString(),
        }, FakeImageStorage.Credentials.ApiSecret);
        Assert.Equal(expected, data.GetProperty("signature").GetString());
    }

    [Fact]
    // Chữ ký logo phải khóa đúng thư mục thương hiệu để không tải nhầm vào thư mục sản phẩm.
    public async Task BrandLogoSignature_Admin_ReturnsBrandFolderAndVerifiableSignature()
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/admin/uploads/brand-logo/signature", null, _adminCookie));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(FakeImageStorage.Credentials.ApiSecret, body);
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(ImageUploadRules.BrandFolder, data.GetProperty("folder").GetString());
        var expected = CloudinarySignature.Sign(new Dictionary<string, string>
        {
            ["allowed_formats"] = "jpg,png,webp",
            ["folder"] = ImageUploadRules.BrandFolder,
            ["timestamp"] = data.GetProperty("timestamp").GetInt64().ToString(),
        }, FakeImageStorage.Credentials.ApiSecret);
        Assert.Equal(expected, data.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task Signature_SalesWithoutProductsWrite_Returns403()
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/admin/uploads/signature", null, _salesCookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Test dùng kho ảnh giả, không gọi Cloudinary thật.
    public void TestHost_UsesFakeImageStorage()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<FakeImageStorage>(scope.ServiceProvider.GetRequiredService<IImageStorage>());
    }

    [Fact]
    // Ảnh lưu theo thứ tự gửi lên, kèm alt, và trả về trong chi tiết sản phẩm.
    public async Task Create_WithImages_StoresOrderAndAlt()
    {
        var front = UploadedImage("front");
        var back = UploadedImage("back", format: "webp");

        var productId = await CreateProductIdAsync([Image(front, "Mặt trước"), Image(back, "Mặt sau")]);

        var detail = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, $"/api/admin/products/{productId}", _adminCookie));
        using var json = await ActorHttp.ReadJsonAsync(detail);
        var images = json.RootElement.GetProperty("data").GetProperty("images").EnumerateArray().ToList();
        Assert.Equal([front, back], images.Select(image => image.GetProperty("publicId").GetString()));
        Assert.Equal([0, 1], images.Select(image => image.GetProperty("position").GetInt32()));
        Assert.Equal("Mặt sau", images[1].GetProperty("alt").GetString());
        // Phản hồi có URL giao ảnh, để giao diện hiện ảnh thu nhỏ mà không cần quyền ghi hay chữ ký.
        Assert.Equal($"https://res.cloudinary.com/{FakeImageStorage.Credentials.CloudName}/image/upload/{front}",
            images[0].GetProperty("url").GetString());
    }

    [Fact]
    // Tối đa 10 ảnh mỗi sản phẩm. Ảnh thứ 11 làm cả yêu cầu bị từ chối.
    public async Task Create_WithElevenImages_Returns400()
    {
        var images = Enumerable.Range(1, 11).Select(index => Image(UploadedImage($"many-{index}"))).ToArray();

        var response = await CreateProductAsync(images);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Create_WithTenImages_Succeeds()
    {
        var images = Enumerable.Range(1, 10).Select(index => Image(UploadedImage($"ten-{index}"))).ToArray();

        var response = await CreateProductAsync(images);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    // Bỏ một ảnh khỏi sản phẩm tạo đúng một job xóa cho ảnh đó. Ảnh còn giữ không bị tạo job.
    public async Task Patch_RemovingImage_CreatesOneDeletionJob()
    {
        var kept = UploadedImage("kept");
        var removed = UploadedImage("removed");
        var productId = await CreateProductIdAsync([Image(kept), Image(removed)]);

        var response = await PatchImagesAsync(productId, [Image(kept)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jobs = await JobsForAsync(removed);
        var job = Assert.Single(jobs);
        Assert.Equal(ImageDeletionProcessor.PendingStatus, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Empty(await JobsForAsync(kept));
        await using var context = _database.CreateCatalogContext();
        Assert.Equal([kept], await context.ProductImages.Where(image => image.ProductId == productId)
            .Select(image => image.PublicId).ToListAsync());
    }

    [Fact]
    // PATCH không gửi trường `images` thì giữ nguyên ảnh và không tạo job.
    public async Task Patch_WithoutImagesField_KeepsImages()
    {
        var image = UploadedImage("untouched");
        var productId = await CreateProductIdAsync([Image(image)]);

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Patch, $"/api/admin/products/{productId}", new { shortDesc = "Mô tả mới" }, _adminCookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = _database.CreateCatalogContext();
        Assert.Equal(1, await context.ProductImages.CountAsync(item => item.ProductId == productId));
        Assert.Empty(await JobsForAsync(image));
    }

    [Fact]
    // Ảnh trên 5 MB bị từ chối, và ảnh đó được xếp lịch xóa vì đã nằm trên Cloudinary.
    public async Task Create_WithImageOverFiveMegabytes_RejectsAndSchedulesDeletion()
    {
        var oversized = UploadedImage("oversized", bytes: ImageUploadRules.MaxBytes + 1);

        var response = await CreateProductAsync([Image(oversized)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "IMAGE_TOO_LARGE");
        Assert.Single(await JobsForAsync(oversized));
    }

    [Fact]
    public async Task Create_WithImageOfExactlyFiveMegabytes_Succeeds()
    {
        var limit = UploadedImage("limit", bytes: ImageUploadRules.MaxBytes);

        var response = await CreateProductAsync([Image(limit)]);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_WithGifImage_RejectsAndSchedulesDeletion()
    {
        var gif = UploadedImage("animated", format: "gif");

        var response = await CreateProductAsync([Image(gif)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "IMAGE_FORMAT_INVALID");
        Assert.Single(await JobsForAsync(gif));
    }

    [Fact]
    // Ảnh không có trên kho ảnh thì không gắn được và không cần job xóa.
    public async Task Create_WithUnknownImage_Returns400WithoutJob()
    {
        var missing = $"{ImageUploadRules.Folder}/{_suffix}-missing";

        var response = await CreateProductAsync([Image(missing)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "IMAGE_NOT_FOUND");
        Assert.Empty(await JobsForAsync(missing));
    }

    [Theory]
    [InlineData("pickory/brands/logo")]
    [InlineData("pickory/products/../brands/logo")]
    // Ảnh ngoài thư mục sản phẩm bị chặn trước khi hỏi kho ảnh.
    public async Task Create_WithImageOutsideProductFolder_Returns400(string publicId)
    {
        var response = await CreateProductAsync([Image(publicId)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Alt text bắt buộc cho người dùng trình đọc màn hình.
    public async Task Create_WithoutAlt_Returns400(string alt)
    {
        var response = await CreateProductAsync([Image(UploadedImage($"no-alt-{alt.Length}"), alt)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Create_WithDuplicatePublicId_Returns400()
    {
        var image = UploadedImage("twice");

        var response = await CreateProductAsync([Image(image), Image(image)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Ảnh gắn với SKU của sản phẩm khác bị từ chối, để gallery theo màu không lấy nhầm biến thể.
    public async Task Patch_WithSkuOfAnotherProduct_Returns400()
    {
        var otherProductId = await CreateProductIdAsync([]);
        await using var context = _database.CreateCatalogContext();
        var otherSkuId = await context.Skus.Where(sku => sku.ProductId == otherProductId).Select(sku => sku.Id).FirstAsync();
        var productId = await CreateProductIdAsync([]);

        var response = await PatchImagesAsync(productId, [Image(UploadedImage("wrong-sku"), skuId: otherSkuId)]);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "IMAGE_SKU_INVALID");
    }

    [Fact]
    public async Task Patch_WithOwnSku_StoresSkuLink()
    {
        var productId = await CreateProductIdAsync([]);
        await using var context = _database.CreateCatalogContext();
        var skuId = await context.Skus.Where(sku => sku.ProductId == productId).Select(sku => sku.Id).FirstAsync();
        var image = UploadedImage("own-sku");

        var response = await PatchImagesAsync(productId, [Image(image, skuId: skuId)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(skuId, await context.ProductImages.Where(item => item.PublicId == image)
            .Select(item => item.SkuId).SingleAsync());
    }
}
