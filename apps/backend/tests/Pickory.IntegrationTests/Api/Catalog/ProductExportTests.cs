using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Identity.Contracts.Authorization;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// Xuất sản phẩm ra Excel, mỗi dòng một biến thể (SKU). Route dùng chung bộ lọc với danh sách và không ghi nhật ký thao tác.
[Collection(CatalogCollection.Name)]
public sealed class ProductExportTests : IClassFixture<CatalogWebApplicationFactory>, IAsyncLifetime
{
    private const string ExportsFixture = "exports-printing/exports.json";
    private const string ExportPath = "/api/admin/exports/products";
    private const string ProductsPath = "/api/admin/products";
    private const string SheetName = "Sản phẩm";

    // Mốc cố định ứng với 09:00 ngày 01/10/2026 giờ Việt Nam, khớp `sampleRow` của ví dụ `products export success`.
    private static readonly DateTime BaseTime = new(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);

    private readonly CatalogWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _createdProductIds = [];
    private readonly List<string> _createdCategoryIds = [];
    private readonly List<string> _createdBrandIds = [];

    public ProductExportTests(CatalogWebApplicationFactory factory, CatalogDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await using var catalog = _database.CreateCatalogContext();

        // Tìm mọi sản phẩm của test, kể cả sản phẩm thuộc danh mục hoặc thương hiệu của test. Khóa ngoại chặn xóa danh mục còn sản phẩm.
        var productIds = await catalog.Products
            .IgnoreQueryFilters()
            .Where(product => _createdProductIds.Contains(product.Id)
                || _createdCategoryIds.Contains(product.CategoryId)
                || _createdBrandIds.Contains(product.BrandId))
            .Select(product => product.Id)
            .ToListAsync();

        // Xóa biến động tồn kho trước, vì khóa ngoại của biến động chặn xóa biến thể.
        var skuIds = await catalog.Skus.Where(sku => productIds.Contains(sku.ProductId)).Select(sku => sku.Id).ToListAsync();
        await catalog.InventoryMovements.Where(movement => skuIds.Contains(movement.SkuId)).ExecuteDeleteAsync();
        await catalog.Products.IgnoreQueryFilters().Where(product => productIds.Contains(product.Id)).ExecuteDeleteAsync();

        // Danh mục con có khóa ngoại tới danh mục cha, nên xóa danh mục con trước.
        await catalog.Categories.IgnoreQueryFilters()
            .Where(category => _createdCategoryIds.Contains(category.Id) && category.ParentId != null)
            .ExecuteDeleteAsync();
        await catalog.Categories.IgnoreQueryFilters().Where(category => _createdCategoryIds.Contains(category.Id)).ExecuteDeleteAsync();
        await catalog.Brands.IgnoreQueryFilters().Where(brand => _createdBrandIds.Contains(brand.Id)).ExecuteDeleteAsync();
    }

    // Phiên của nhân viên giữ đúng các quyền cho trước. Role riêng cho từng lần gọi nên test không phụ thuộc role mặc định.
    private async Task<string> StaffCookieAsync(params string[] permissions)
    {
        var role = await IdentityTestData.CreateRoleAsync(_database, permissions);
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(_database, role.Code, "exp05");
        return $"pk_session={token}";
    }

    private Task<string> ExporterCookieAsync() => StaffCookieAsync(Permissions.ProductsRead, Permissions.ReportsExport);

    private Task<string> ListReaderCookieAsync() => StaffCookieAsync(Permissions.ProductsRead);

    private Task<HttpResponseMessage> ExportAsync(string query, string? cookie, HttpClient? client = null)
    {
        var path = query.Length == 0 ? ExportPath : $"{ExportPath}?{query}";
        return (client ?? _client).SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, cookie));
    }

    // Đọc file từ response thành công. Test khẳng định status 200 trước để lỗi hiện đúng chỗ.
    private static async Task<XLWorkbook> ReadWorkbookAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new XLWorkbook(new MemoryStream(bytes));
    }

    // Số dòng dữ liệu của sheet, không tính dòng tiêu đề. Sheet chỉ có tiêu đề trả 0.
    private static int DataRowCount(IXLWorksheet sheet) => Math.Max((sheet.LastRowUsed()?.RowNumber() ?? 1) - 1, 0);

    // Tìm cột theo tiêu đề dòng 1. Test không phụ thuộc số thứ tự cột, vì thứ tự cột có test hợp đồng riêng.
    private static int ColumnOf(IXLWorksheet sheet, string header)
    {
        for (var column = 1; column <= sheet.LastColumnUsed()!.ColumnNumber(); column++)
        {
            if (string.Equals(sheet.Cell(1, column).GetString(), header, StringComparison.Ordinal))
            {
                return column;
            }
        }

        throw new InvalidOperationException($"Sheet \"{sheet.Name}\" không có cột \"{header}\".");
    }

    // Ô của dòng dữ liệu thứ `dataRow` (bắt đầu từ 1) ở cột có tiêu đề cho trước.
    private static IXLCell CellOf(IXLWorksheet sheet, int dataRow, string header) =>
        sheet.Cell(dataRow + 1, ColumnOf(sheet, header));

    private static string? TextOf(IXLWorksheet sheet, int dataRow, string header)
    {
        var cell = CellOf(sheet, dataRow, header);
        return cell.IsEmpty() ? null : cell.GetString();
    }

    private static List<string> SkuCodesOf(IXLWorksheet sheet) =>
        Enumerable.Range(1, DataRowCount(sheet)).Select(row => TextOf(sheet, row, "Mã SKU")!).ToList();

    // Tên sản phẩm không lặp, theo thứ tự xuất hiện. Một sản phẩm nhiều biến thể chiếm nhiều dòng nhưng chỉ có một tên.
    private static List<string> ProductNamesOf(IXLWorksheet sheet) =>
        Enumerable.Range(1, DataRowCount(sheet)).Select(row => TextOf(sheet, row, "Tên sản phẩm")!).Distinct().ToList();

    // Tên file thật trong header, để so với tên mong đợi.
    private static string FileNameOf(HttpResponseMessage response)
    {
        var header = string.Join(", ", response.Content.Headers.GetValues("Content-Disposition"));
        return Regex.Match(header, "filename=\"(?<name>[^\"]+)\"").Groups["name"].Value;
    }

    // Tên sản phẩm của route danh sách, theo thứ tự trả về. `limit=100` đủ lớn để một trang chứa hết dữ liệu của test.
    private async Task<List<string>> ListedProductNamesAsync(string query)
    {
        var cookie = await ListReaderCookieAsync();
        using var response = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, $"{ProductsPath}?{query}&limit=100", cookie));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)
            .ToList();
    }

    private async Task<int> AuditLogCountAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync();
    }

    private WebApplicationFactory<Program> FactoryWithMaxRows(int maxRows) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting("Exports:MaxRows", maxRows.ToString(CultureInfo.InvariantCulture)));

    private sealed record SkuSeed(string Code, int Price, int Stock, bool Enabled = true, string OptionValuesJson = "{}");

    private async Task<Category> CreateCategoryAsync(string name, string? parentId = null)
    {
        await using var catalog = _database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = $"exp05-cat-{Guid.NewGuid():N}",
            ParentId = parentId,
            Attributes = JsonDocument.Parse("[]"),
            CreatedAt = BaseTime,
            UpdatedAt = BaseTime,
        };
        catalog.Categories.Add(category);
        await catalog.SaveChangesAsync();
        _createdCategoryIds.Add(category.Id);
        return category;
    }

    private async Task<Brand> CreateBrandAsync(string name)
    {
        await using var catalog = _database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = $"exp05-brand-{Guid.NewGuid():N}",
            CreatedAt = BaseTime,
            UpdatedAt = BaseTime,
        };
        catalog.Brands.Add(brand);
        await catalog.SaveChangesAsync();
        _createdBrandIds.Add(brand.Id);
        return brand;
    }

    // Tạo sản phẩm cùng biến thể. Biến thể có tồn kho đi kèm một biến động thủ công, để bất biến "stock bằng tổng delta" vẫn đúng.
    private async Task<Product> SeedProductAsync(
        string name,
        IReadOnlyList<SkuSeed> skus,
        string categoryId,
        string brandId,
        ProductStatus status = ProductStatus.Active,
        DateTime? createdAt = null,
        string? id = null)
    {
        await using var catalog = _database.CreateCatalogContext();
        var created = createdAt ?? BaseTime;
        var product = new Product
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            CategoryId = categoryId,
            BrandId = brandId,
            Name = name,
            Slug = $"exp05-{Guid.NewGuid():N}",
            ShortDesc = "Mô tả ngắn",
            Description = JsonDocument.Parse("{}"),
            Specs = JsonDocument.Parse("{}"),
            Options = JsonDocument.Parse("[]"),
            Status = status,
            CreatedAt = created,
            UpdatedAt = created,
        };

        foreach (var seed in skus)
        {
            var sku = new Sku
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = seed.Code,
                OptionValues = JsonDocument.Parse(seed.OptionValuesJson),
                Price = seed.Price,
                Stock = seed.Stock,
                Enabled = seed.Enabled,
                CreatedAt = created,
                UpdatedAt = created,
            };
            if (seed.Stock > 0)
            {
                sku.Movements.Add(new InventoryMovement
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Delta = seed.Stock,
                    Type = InventoryMovementType.Manual,
                    Reason = "Khởi tạo tồn kho cho test xuất file",
                    CreatedAt = created,
                });
            }

            product.Skus.Add(sku);
        }

        catalog.Products.Add(product);
        await catalog.SaveChangesAsync();
        _createdProductIds.Add(product.Id);
        return product;
    }

    // Mã SKU là duy nhất toàn DB, nên mã của test luôn mang hậu tố riêng của lần chạy.
    private string Code(string name) => $"EXP5-{name}-{_suffix}";

    [Fact]
    // Contract: file thành công khớp ví dụ `products export success` về header, tên sheet, tiêu đề và kiểu ô dòng 2.
    public async Task Export_Success_MatchesContractWorkbook()
    {
        var category = await CreateCategoryAsync($"Điện thoại {_suffix}");
        var brand = await CreateBrandAsync("Apple");
        // Biến thể sắp hết hàng có số bán được là 3 như `sampleRow`, nên dòng 2 có đủ kiểu chữ, số và ngày giờ.
        await SeedProductAsync(
            $"iPhone 16 256GB {_suffix}",
            [new SkuSeed(Code("IP16"), 24_990_000, 3, OptionValuesJson: """{"color":"Đen","storage":"256 GB"}""")],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"lowStock=true&categoryId={category.Id}", cookie);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        WorkbookFixture.AssertMatches(ExportsFixture, "products export success", bytes, response);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(1, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Thiếu `reports:export` trả 403. Fixture chưa có ví dụ lỗi riêng cho route này, nên dùng ví dụ của route đơn hàng.
    public async Task Export_MissingReportsExport_Returns403()
    {
        var cookie = await StaffCookieAsync(Permissions.ProductsRead);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
    }

    [Fact]
    // Có `reports:export` nhưng thiếu `products:read` cũng trả 403. Fixture chưa có ví dụ lỗi riêng cho route này.
    public async Task Export_MissingProductsRead_Returns403()
    {
        var cookie = await StaffCookieAsync(Permissions.ReportsExport);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
    }

    [Fact]
    // Chưa đăng nhập thì trả 401.
    public async Task Export_WithoutSession_Returns401()
    {
        using var response = await ExportAsync(string.Empty, cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Đủ hai quyền thì nhận file. Đây là chiều đối chứng của hai test 403.
    public async Task Export_WithBothPermissions_Returns200()
    {
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"search={Guid.NewGuid():N}", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("status=khong_co")]
    // Phản hồi mang `no-store` cả khi thành công lẫn khi tham số sai, vì header đặt trước bước kiểm tham số.
    public async Task Export_Response_SetsNoStore(string query)
    {
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync(query, cookie);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Tên file dùng đồng hồ do hệ thống cấp, theo giờ Việt Nam. 08:30 giờ quốc tế ngày 10/11/2026 là 15:30 cùng ngày.
    public async Task Export_FileName_UsesClockFromDependencyInjection()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero));
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"search={Guid.NewGuid():N}", cookie, client);

        Assert.Equal("san-pham-20261110-1530.xlsx", FileNameOf(response));
    }

    [Fact]
    // Sản phẩm có 3 biến thể tạo 3 dòng. Cột "Phân loại" ghép giá trị bằng dấu phẩy, theo thứ tự khóa đã lưu trong DB.
    public async Task Export_ProductWithThreeSkus_WritesThreeRowsWithJoinedOptions()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        // Cột `jsonb` của PostgreSQL xếp khóa ngắn trước, nên `color` đứng trước `storage` dù JSON gửi vào ghi ngược lại.
        await SeedProductAsync(
            $"Ốp lưng {_suffix}",
            [
                new SkuSeed(Code("A"), 100_000, 10, OptionValuesJson: """{"storage":"128 GB","color":"Đen"}"""),
                new SkuSeed(Code("B"), 120_000, 10, OptionValuesJson: """{"storage":"256 GB","color":"Trắng"}"""),
                new SkuSeed(Code("C"), 140_000, 10, OptionValuesJson: """{"storage":"512 GB","color":"Xanh"}"""),
            ],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(3, DataRowCount(sheet));
        Assert.Equal("Đen, 128 GB", TextOf(sheet, 1, "Phân loại"));
        Assert.Equal("Trắng, 256 GB", TextOf(sheet, 2, "Phân loại"));
        Assert.Equal("Xanh, 512 GB", TextOf(sheet, 3, "Phân loại"));
    }

    [Fact]
    // Sản phẩm không có phân loại có ô "Phân loại" trống. Ô trống khác chuỗi rỗng.
    public async Task Export_SkuWithoutOptions_LeavesOptionCellBlank()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Cáp sạc {_suffix}", [new SkuSeed(Code("PLAIN"), 50_000, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var cell = CellOf(workbook.Worksheet(SheetName), 1, "Phân loại");
        Assert.True(cell.IsEmpty());
        Assert.Equal(XLDataType.Blank, cell.DataType);
    }

    [Fact]
    // Biến thể tắt là tổ hợp phân loại đã bỏ, nên không có trong file. Biến thể bật của cùng sản phẩm vẫn có.
    public async Task Export_DisabledSku_IsNotInFile()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync(
            $"Tai nghe {_suffix}",
            [
                new SkuSeed(Code("ON"), 100_000, 10),
                new SkuSeed(Code("OFF"), 100_000, 10, Enabled: false),
            ],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal([Code("ON")], SkuCodesOf(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Sản phẩm không có biến thể nào khớp không có dòng trong file, dù danh sách vẫn hiện nó. Đây là điểm lệch có chủ ý vì mỗi dòng là một biến thể.
    public async Task Export_ProductWithoutEnabledSku_HasListRowButNoFileRow()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Có biến thể bật {_suffix}", [new SkuSeed(Code("LIVE"), 1, 10)], category.Id, brand.Id, createdAt: BaseTime.AddHours(2));
        await SeedProductAsync($"Chỉ biến thể tắt {_suffix}", [new SkuSeed(Code("DEAD"), 1, 10, Enabled: false)], category.Id, brand.Id, createdAt: BaseTime.AddHours(1));
        await SeedProductAsync($"Chưa có biến thể {_suffix}", [], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();
        var query = $"categoryId={category.Id}";

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(
            [$"Có biến thể bật {_suffix}", $"Chỉ biến thể tắt {_suffix}", $"Chưa có biến thể {_suffix}"],
            await ListedProductNamesAsync(query));
        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([Code("LIVE")], SkuCodesOf(sheet));
        Assert.Equal([$"Có biến thể bật {_suffix}"], ProductNamesOf(sheet));
    }

    [Fact]
    // Danh sách quản trị cố ý hiện sản phẩm xóa mềm, nên file cũng có chúng. Nhãn trạng thái vẫn là nhãn của `Status`.
    public async Task Export_SoftDeletedProduct_IsInFileLikeInList()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        var product = await SeedProductAsync($"Đã xóa mềm {_suffix}", [new SkuSeed(Code("GONE"), 1, 10)], category.Id, brand.Id);
        await using (var catalog = _database.CreateCatalogContext())
        {
            await catalog.Products.Where(value => value.Id == product.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.DeletedAt, BaseTime));
        }

        var cookie = await ExporterCookieAsync();
        var query = $"categoryId={category.Id}";

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([$"Đã xóa mềm {_suffix}"], await ListedProductNamesAsync(query));
        Assert.Equal([$"Đã xóa mềm {_suffix}"], ProductNamesOf(sheet));
        Assert.Equal("Đang bán", TextOf(sheet, 1, "Trạng thái"));
    }

    [Fact]
    // Giá bán và số bán được là ô số đúng giá trị, không bị đổi chỗ. Ngày tạo đổi sang giờ Việt Nam và là ô ngày giờ.
    public async Task Export_Values_AreWrittenToTheirOwnColumns()
    {
        var category = await CreateCategoryAsync($"Điện thoại {_suffix}");
        var brand = await CreateBrandAsync($"Apple {_suffix}");
        await SeedProductAsync(
            $"iPhone {_suffix}",
            [new SkuSeed(Code("IP"), 24_990_000, 3, OptionValuesJson: """{"color":"Đen","storage":"256 GB"}""")],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal($"iPhone {_suffix}", TextOf(sheet, 1, "Tên sản phẩm"));
        Assert.Equal(Code("IP"), TextOf(sheet, 1, "Mã SKU"));
        Assert.Equal("Đen, 256 GB", TextOf(sheet, 1, "Phân loại"));
        Assert.Equal($"Apple {_suffix}", TextOf(sheet, 1, "Thương hiệu"));
        Assert.Equal($"Điện thoại {_suffix}", TextOf(sheet, 1, "Danh mục"));
        Assert.Equal(24_990_000, CellOf(sheet, 1, "Giá bán").GetDouble());
        Assert.Equal(3, CellOf(sheet, 1, "Số bán được").GetDouble());
        Assert.Equal("Đang bán", TextOf(sheet, 1, "Trạng thái"));
        var createdAt = CellOf(sheet, 1, "Ngày tạo");
        Assert.Equal(XLDataType.DateTime, createdAt.DataType);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), createdAt.GetDateTime());
    }

    [Fact]
    // `lowStock=true`: file chỉ có biến thể sắp hết, và tập sản phẩm bằng tập của danh sách. Biến thể còn nhiều của cùng sản phẩm bị bỏ.
    public async Task Export_LowStock_HasOnlyLowStockSkusAndSameProductsAsList()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync(
            $"Hỗn hợp {_suffix}",
            [new SkuSeed(Code("LOW"), 100_000, 2), new SkuSeed(Code("MANY"), 100_000, 50)],
            category.Id,
            brand.Id);
        await SeedProductAsync($"Còn nhiều {_suffix}", [new SkuSeed(Code("FULL"), 100_000, 50)], category.Id, brand.Id);
        // Biến thể tắt có tồn kho 0 không tính là sắp hết, nên sản phẩm này không vào danh sách hay file.
        await SeedProductAsync($"Chỉ tắt {_suffix}", [new SkuSeed(Code("DIS"), 100_000, 0, Enabled: false)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"lowStock=true&categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([Code("LOW")], SkuCodesOf(sheet));
        var listed = await ListedProductNamesAsync($"lowStock=true&categoryId={category.Id}");
        Assert.Equal([$"Hỗn hợp {_suffix}"], listed);
        Assert.Equal(listed, ProductNamesOf(sheet));
    }

    [Fact]
    // Không có `lowStock` thì file giữ cả biến thể sắp hết lẫn biến thể còn nhiều của cùng sản phẩm.
    public async Task Export_WithoutLowStock_KeepsEveryEnabledSku()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync(
            $"Hỗn hợp {_suffix}",
            [new SkuSeed(Code("LOW"), 100_000, 2), new SkuSeed(Code("MANY"), 100_000, 50)],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal([Code("LOW"), Code("MANY")], SkuCodesOf(workbook.Worksheet(SheetName)));
    }

    [Theory]
    [InlineData("Đặc biệt", "Đặc biệt")]
    [InlineData("Đặc biệt", "dac biet")]
    [InlineData("Đặc biệt", "ĐẶC BIỆT")]
    // Tìm theo tên có dấu và không dấu cho cùng tập sản phẩm với danh sách. Sản phẩm có tên khác không khớp.
    public async Task Export_SearchByName_ReturnsSameProductsAsList(string productWord, string searchWord)
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"{productWord} khớp {_suffix}", [new SkuSeed(Code("HIT"), 100_000, 10)], category.Id, brand.Id);
        await SeedProductAsync($"Máy khác {_suffix}", [new SkuSeed(Code("MISS"), 100_000, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();
        // Từ khóa "khop" không dấu phải khớp tên "khớp" có dấu. Cột tên và từ khóa cùng đi qua `unaccent`.
        var query = $"search={Uri.EscapeDataString($"{searchWord} khop {_suffix}")}";
        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([$"{productWord} khớp {_suffix}"], ProductNamesOf(sheet));
        Assert.Equal(await ListedProductNamesAsync(query), ProductNamesOf(sheet));
    }

    [Fact]
    // Tìm theo mã SKU khớp sản phẩm dù tên sản phẩm không chứa từ khóa, giống danh sách.
    public async Task Export_SearchBySkuCode_ReturnsSameProductsAsList()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Sản phẩm A {_suffix}", [new SkuSeed(Code("FINDME"), 100_000, 10)], category.Id, brand.Id);
        await SeedProductAsync($"Sản phẩm B {_suffix}", [new SkuSeed(Code("OTHER"), 100_000, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();
        var query = $"search={Uri.EscapeDataString(Code("FINDME"))}";

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([$"Sản phẩm A {_suffix}"], ProductNamesOf(sheet));
        Assert.Equal(await ListedProductNamesAsync(query), ProductNamesOf(sheet));
    }

    [Fact]
    // Lọc danh mục cha lấy cả sản phẩm của danh mục con. Sản phẩm ở danh mục khác bị loại, giống danh sách.
    public async Task Export_ParentCategory_IncludesChildCategoryProductsAsList()
    {
        var parent = await CreateCategoryAsync($"Cha {_suffix}");
        var child = await CreateCategoryAsync($"Con {_suffix}", parent.Id);
        var other = await CreateCategoryAsync($"Khác {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Của con {_suffix}", [new SkuSeed(Code("CHILD"), 100_000, 10)], child.Id, brand.Id, createdAt: BaseTime.AddHours(1));
        await SeedProductAsync($"Của cha {_suffix}", [new SkuSeed(Code("PARENT"), 100_000, 10)], parent.Id, brand.Id);
        await SeedProductAsync($"Của nhóm khác {_suffix}", [new SkuSeed(Code("OTHER"), 100_000, 10)], other.Id, brand.Id);
        var cookie = await ExporterCookieAsync();
        var query = $"categoryId={parent.Id}";

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([$"Của con {_suffix}", $"Của cha {_suffix}"], ProductNamesOf(sheet));
        Assert.Equal(await ListedProductNamesAsync(query), ProductNamesOf(sheet));
    }

    [Fact]
    // Lọc `status` chỉ giữ sản phẩm đúng trạng thái, cùng tập với danh sách.
    public async Task Export_StatusFilter_ReturnsSameProductsAsList()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Đang bán {_suffix}", [new SkuSeed(Code("ACT"), 100_000, 10)], category.Id, brand.Id, ProductStatus.Active);
        await SeedProductAsync($"Bản nháp {_suffix}", [new SkuSeed(Code("DRF"), 100_000, 10)], category.Id, brand.Id, ProductStatus.Draft);
        var cookie = await ExporterCookieAsync();
        var query = $"categoryId={category.Id}&status=draft";

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal([$"Bản nháp {_suffix}"], ProductNamesOf(sheet));
        Assert.Equal(await ListedProductNamesAsync(query), ProductNamesOf(sheet));
    }

    [Fact]
    // Bốn trạng thái có bốn nhãn tiếng Việt đúng. Mỗi sản phẩm một trạng thái, theo thứ tự mới tạo lên đầu.
    public async Task Export_FourStatuses_WriteFourVietnameseLabels()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"P1 {_suffix}", [new SkuSeed(Code("S1"), 1, 10)], category.Id, brand.Id, ProductStatus.Draft, BaseTime.AddHours(4));
        await SeedProductAsync($"P2 {_suffix}", [new SkuSeed(Code("S2"), 1, 10)], category.Id, brand.Id, ProductStatus.Active, BaseTime.AddHours(3));
        await SeedProductAsync($"P3 {_suffix}", [new SkuSeed(Code("S3"), 1, 10)], category.Id, brand.Id, ProductStatus.Hidden, BaseTime.AddHours(2));
        await SeedProductAsync($"P4 {_suffix}", [new SkuSeed(Code("S4"), 1, 10)], category.Id, brand.Id, ProductStatus.Discontinued, BaseTime.AddHours(1));
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        var labels = Enumerable.Range(1, 4).Select(row => TextOf(sheet, row, "Trạng thái")).ToList();
        Assert.Equal(["Bản nháp", "Đang bán", "Đã ẩn", "Ngừng bán"], labels);
    }

    [Fact]
    // Thương hiệu hoặc danh mục đã xóa mềm cho ô trống, và sản phẩm vẫn có trong file như trong danh sách.
    // Form quản lý không cho xóa thương hiệu hay danh mục còn sản phẩm, nên test ghi thẳng vào DB.
    public async Task Export_SoftDeletedBrandAndCategory_LeaveCellsBlank()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Mồ côi {_suffix}", [new SkuSeed(Code("ORPHAN"), 100_000, 10)], category.Id, brand.Id);
        await using (var catalog = _database.CreateCatalogContext())
        {
            await catalog.Brands.Where(value => value.Id == brand.Id).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.DeletedAt, BaseTime));
            await catalog.Categories.Where(value => value.Id == category.Id).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.DeletedAt, BaseTime));
        }

        var cookie = await ExporterCookieAsync();
        // Danh mục đã xóa mềm không còn khớp `categoryId` trong bộ lọc, nên tìm theo tên sản phẩm.
        var query = $"search={Uri.EscapeDataString($"Mồ côi {_suffix}")}";
        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(1, DataRowCount(sheet));
        Assert.Equal(await ListedProductNamesAsync(query), ProductNamesOf(sheet));
        Assert.True(CellOf(sheet, 1, "Thương hiệu").IsEmpty());
        Assert.True(CellOf(sheet, 1, "Danh mục").IsEmpty());
        Assert.Equal(Code("ORPHAN"), TextOf(sheet, 1, "Mã SKU"));
    }

    [Fact]
    // Tên sản phẩm bắt đầu bằng `=` là ô chữ, không phải công thức Excel.
    public async Task Export_ProductNameStartingWithEquals_IsTextNotFormula()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync("=1+1", [new SkuSeed(Code("FORMULA"), 100_000, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var cell = CellOf(workbook.Worksheet(SheetName), 1, "Tên sản phẩm");
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.False(cell.HasFormula);
        Assert.Equal("=1+1", cell.GetString());
    }

    [Fact]
    // Sản phẩm mới tạo lên đầu, hai sản phẩm cùng `CreatedAt` đi theo `Id`, biến thể trong sản phẩm đi theo `Code`.
    // Thứ tự tên sản phẩm bằng thứ tự của route danh sách.
    public async Task Export_IsNewestFirstThenByIdAndSkusByCode()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Cũ {_suffix}", [new SkuSeed(Code("OLD"), 1, 10)], category.Id, brand.Id, createdAt: BaseTime.AddHours(-5));
        // Mã SKU của sản phẩm có `Id` lớn hơn xếp trước theo `Code`. Nhờ vậy thứ tự theo `Id` khác thứ tự theo `Code`.
        await SeedProductAsync(
            $"Hòa hai {_suffix}", [new SkuSeed(Code("TIE-A"), 1, 10)], category.Id, brand.Id, id: $"{_suffix}-b");
        await SeedProductAsync(
            $"Hòa một {_suffix}",
            [new SkuSeed(Code("TIE-B3"), 1, 10), new SkuSeed(Code("TIE-B1"), 1, 10), new SkuSeed(Code("TIE-B2"), 1, 10)],
            category.Id,
            brand.Id,
            id: $"{_suffix}-a");
        await SeedProductAsync($"Mới {_suffix}", [new SkuSeed(Code("NEW"), 1, 10)], category.Id, brand.Id, createdAt: BaseTime.AddHours(5));
        var cookie = await ExporterCookieAsync();
        var query = $"categoryId={category.Id}";

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(
            [Code("NEW"), Code("TIE-B1"), Code("TIE-B2"), Code("TIE-B3"), Code("TIE-A"), Code("OLD")],
            SkuCodesOf(sheet));
        Assert.Equal(await ListedProductNamesAsync(query), ProductNamesOf(sheet));
    }

    [Theory]
    [InlineData("page=2&limit=1")]
    [InlineData("page=abc&limit=0")]
    [InlineData("limit=abc")]
    [InlineData("limit=1000")]
    // `page` và `limit` bị bỏ qua, kể cả giá trị mà danh sách coi là sai. File luôn chứa mọi dòng khớp bộ lọc.
    public async Task Export_IgnoresPageAndLimit(string pagingQuery)
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Một {_suffix}", [new SkuSeed(Code("ONE"), 1, 10), new SkuSeed(Code("TWO"), 1, 10)], category.Id, brand.Id);
        await SeedProductAsync($"Hai {_suffix}", [new SkuSeed(Code("THREE"), 1, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}&{pagingQuery}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(3, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Theory]
    [InlineData("status=xyz", "status")]
    [InlineData("lowStock=maybe", "lowStock")]
    [InlineData("search=a&search=b", "search")]
    // Giá trị lạ và tham số lặp trả 400 `VALIDATION_ERROR` với `details[].key` là đúng tên tham số.
    public async Task Export_InvalidParameter_Returns400ForTheParameter(string query, string parameter)
    {
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync(query, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var keys = error.GetProperty("details").EnumerateArray().Select(detail => detail.GetProperty("key").GetString());
        Assert.Contains(parameter, keys);
    }

    [Fact]
    // Không có sản phẩm nào khớp vẫn trả file hợp lệ chỉ có dòng tiêu đề.
    public async Task Export_NoMatchingProduct_ReturnsHeaderOnlyFile()
    {
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"search={Guid.NewGuid():N}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(0, DataRowCount(sheet));
        Assert.Equal("Tên sản phẩm", sheet.Cell(1, 1).GetString());
    }

    [Fact]
    // Giới hạn đếm theo dòng biến thể, không theo sản phẩm: 3 biến thể vượt giới hạn 2.
    // Fixture chưa có ví dụ lỗi riêng cho route này, nên dùng ví dụ `orders export too large`.
    public async Task Export_OverLimit_Returns422()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync(
            $"Nhiều biến thể {_suffix}",
            [new SkuSeed(Code("L1"), 1, 10), new SkuSeed(Code("L2"), 1, 10), new SkuSeed(Code("L3"), 1, 10)],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();
        using var limitedFactory = FactoryWithMaxRows(2);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync($"categoryId={category.Id}", cookie, limitedClient);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response);
    }

    [Fact]
    // Đúng bằng giới hạn thì vẫn trả file. Chỉ khi vượt giới hạn một dòng biến thể mới bị từ chối.
    public async Task Export_ExactlyAtLimit_ReturnsFile()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync(
            $"Nhiều biến thể {_suffix}",
            [new SkuSeed(Code("E1"), 1, 10), new SkuSeed(Code("E2"), 1, 10), new SkuSeed(Code("E3"), 1, 10)],
            category.Id,
            brand.Id);
        var cookie = await ExporterCookieAsync();
        using var limitedFactory = FactoryWithMaxRows(3);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync($"categoryId={category.Id}", cookie, limitedClient);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(3, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Xuất thành công không tạo dòng nhật ký nào, vì file sản phẩm không có dữ liệu cá nhân.
    public async Task Export_Success_WritesNoAuditLog()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync($"Có nhật ký {_suffix}", [new SkuSeed(Code("AUDIT"), 1, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();
        var countBefore = await AuditLogCountAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(1, DataRowCount(workbook.Worksheet(SheetName)));
        Assert.Equal(countBefore, await AuditLogCountAsync());
    }

    [Fact]
    // Dựng file lỗi thì trả lỗi và không trả file, vì ô chữ trên 32.767 ký tự không ghi được.
    // Form quản lý không nhận tên dài như vậy, nên đầu vào này không có thật qua giao diện.
    public async Task Export_WhenWorkbookCannotBeBuilt_ReturnsNoFile()
    {
        var category = await CreateCategoryAsync($"Cha {_suffix}");
        var brand = await CreateBrandAsync($"Hãng {_suffix}");
        await SeedProductAsync(new string('A', 40_000), [new SkuSeed(Code("HUGE"), 1, 10)], category.Id, brand.Id);
        var cookie = await ExporterCookieAsync();

        using var response = await ExportAsync($"categoryId={category.Id}", cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Content.Headers.Contains("Content-Disposition"));
    }
}
