using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Commerce;
using Pickory.Modules.Commerce.Contracts.Reports;
using Pickory.Modules.Commerce.Demo;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// ADM-19: kiểm lệnh tạo đơn mẫu trên DB thật của bộ test.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class DemoOrdersCommandTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset TestClockInstant = new(2040, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(TestClockInstant);
    private readonly WebApplicationFactory<Program> _host;
    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _orderKit;
    private readonly List<string> _brandIds = [];
    private readonly List<string> _categoryIds = [];
    private readonly List<string> _productIds = [];

    public DemoOrdersCommandTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Lệnh đọc đồng hồ từ DI. Đồng hồ giả cố định tháng hiện tại cho kết quả lặp lại được.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
        }));
        _orderKit = new AdminOrderTestKit(database);
    }

    public async Task InitializeAsync()
    {
        await _orderKit.ResetAsync();
        await SeedCatalogAsync();
    }

    public async Task DisposeAsync()
    {
        await _orderKit.ResetAsync();
        await CleanCatalogAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Run_InProduction_RefusesAndCreatesNothing()
    {
        var result = await RunCommandAsync(isProduction: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Production", result.Error);
        Assert.Equal(0, await CountDemoOrdersAsync());
    }

    [Fact]
    public async Task Run_InProductionWithAllowFlag_CreatesOrders()
    {
        var result = await RunCommandAsync(isProduction: true, "--allow-production");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Đã tạo 48 đơn mẫu", result.Output);
        Assert.Equal(48, await CountDemoOrdersAsync());
    }

    [Fact]
    public async Task Run_CreatesOrdersForSixMonthsWithAllReportStatuses()
    {
        var result = await RunCommandAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Đã tạo 48 đơn mẫu", result.Output);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(48, await CountDemoOrdersAsync());
        Assert.True(await CountOrderBrandsAsync() >= 2);

        var daily = await GetDailySalesAsync();

        // Mỗi tháng trong 6 tháng phải có doanh thu, đơn tạo và số lượng bán khác 0.
        for (var month = new DateOnly(2040, 2, 1); month <= new DateOnly(2040, 7, 1); month = month.AddMonths(1))
        {
            var rows = daily.Where(day => day.Day.Year == month.Year && day.Day.Month == month.Month).ToList();
            Assert.NotEmpty(rows);
            Assert.True(rows.Sum(day => day.RecognizedRevenue) > 0);
            Assert.True(rows.Sum(day => day.CreatedOrderCount) > 0);
            Assert.True(rows.Sum(day => day.SoldQuantity) > 0);
        }

        // Đủ các trạng thái mà báo cáo đọc: COD, SePay, khách hủy, shop hủy, quá hạn.
        Assert.True(daily.Sum(day => day.PaymentMethods.Cod) > 0);
        Assert.True(daily.Sum(day => day.PaymentMethods.Sepay) > 0);
        Assert.True(daily.Sum(day => day.Cancellations.ByCustomer) > 0);
        Assert.True(daily.Sum(day => day.Cancellations.ByShop) > 0);
        Assert.True(daily.Sum(day => day.Cancellations.PaymentExpired) > 0);
        Assert.True(daily.Sum(day => day.OrderStatuses.Delivered) > 0);
        Assert.True(daily.Sum(day => day.OrderStatuses.Confirmed) > 0);
        Assert.True(daily.Sum(day => day.OrderStatuses.Cancelled) > 0);
    }

    [Fact]
    public async Task Run_Twice_DoesNotCreateDuplicateOrders()
    {
        await RunCommandAsync();
        var second = await RunCommandAsync();

        Assert.Equal(0, second.ExitCode);
        Assert.Contains("Đã tạo 0 đơn mẫu", second.Output);
        Assert.Contains("Bỏ qua 48 đơn mẫu", second.Output);
        Assert.Equal(48, await CountDemoOrdersAsync());
    }

    [Fact]
    public async Task Reset_DeletesOnlyDemoOrders()
    {
        await RunCommandAsync();
        await _orderKit.AddOrderAsync("REAL-ORDER-1", new DateTime(2040, 6, 1, 3, 0, 0, DateTimeKind.Utc));

        var result = await RunCommandAsync("--reset");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Đã xóa 48 đơn mẫu", result.Output);
        Assert.Equal(0, await CountDemoOrdersAsync());

        await using var commerce = _database.CreateCommerceContext();
        Assert.True(await commerce.Orders.AnyAsync(order => order.Code == "REAL-ORDER-1"));
    }

    private async Task<CommandResult> RunCommandAsync(params string[] options) =>
        await RunCommandAsync(isProduction: false, options);

    private async Task<CommandResult> RunCommandAsync(bool isProduction, params string[] options)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var args = new[] { DemoOrdersCommand.Command }.Concat(options).ToList();
        var exitCode = await CommerceModule.RunDemoOrdersCommandAsync(
            _host.Services, args, isProduction, output, error, CancellationToken.None);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private async Task<int> CountDemoOrdersAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.CountAsync(order => order.Flags.Contains(DemoOrderSeeder.DemoFlag));
    }

    private async Task<int> CountOrderBrandsAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        var orderIds = await commerce.Orders.AsNoTracking()
            .Where(order => order.Flags.Contains(DemoOrderSeeder.DemoFlag))
            .Select(order => order.Id)
            .ToListAsync();
        var productIds = await commerce.OrderItems.AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId))
            .Select(item => item.ProductId)
            .Distinct()
            .ToListAsync();

        await using var catalog = _database.CreateCatalogContext();
        return await catalog.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => product.BrandId)
            .Distinct()
            .CountAsync();
    }

    private async Task<IReadOnlyList<DailySales>> GetDailySalesAsync()
    {
        using var scope = _host.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<ICommerceReports>();
        return await reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 2, 1), new DateOnly(2040, 7, 15)), CancellationToken.None);
    }

    private async Task SeedCatalogAsync()
    {
        await using var catalog = _database.CreateCatalogContext();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Danh mục demo",
            Slug = "danh-muc-demo-" + suffix,
            Position = 0,
            Attributes = JsonDocument.Parse("[]"),
            CreatedAt = now,
            UpdatedAt = now,
        };
        catalog.Categories.Add(category);
        _categoryIds.Add(category.Id);

        var prices = new[] { 1_000_000, 2_000_000, 3_000_000 };
        var brandNames = new[] { "Hãng demo A", "Hãng demo B", "Hãng demo C" };

        for (var index = 0; index < brandNames.Length; index++)
        {
            var brand = new Brand
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = brandNames[index],
                Slug = "hang-demo-" + index + "-" + suffix,
                CreatedAt = now,
                UpdatedAt = now,
            };
            catalog.Brands.Add(brand);
            _brandIds.Add(brand.Id);

            var product = new Product
            {
                Id = Guid.NewGuid().ToString("N"),
                CategoryId = category.Id,
                BrandId = brand.Id,
                Name = "Sản phẩm demo " + (index + 1),
                Slug = "san-pham-demo-" + index + "-" + suffix,
                ShortDesc = "Sản phẩm mẫu cho lệnh ADM-19.",
                Description = JsonDocument.Parse("""{"type":"doc","content":[]}"""),
                Specs = JsonDocument.Parse("[]"),
                Options = JsonDocument.Parse("[]"),
                Status = ProductStatus.Active,
                MinPrice = prices[index],
                InStock = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            product.Skus.Add(new Sku
            {
                Id = Guid.NewGuid().ToString("N"),
                ProductId = product.Id,
                Code = "DEMO-SKU-" + index + "-" + suffix,
                Price = prices[index],
                Stock = 10,
                Enabled = true,
                OptionValues = JsonDocument.Parse("{}"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            catalog.Products.Add(product);
            _productIds.Add(product.Id);
        }

        await catalog.SaveChangesAsync();
    }

    private async Task CleanCatalogAsync()
    {
        await using var catalog = _database.CreateCatalogContext();
        if (_productIds.Count > 0)
        {
            await catalog.Skus.Where(sku => _productIds.Contains(sku.ProductId)).ExecuteDeleteAsync();
            await catalog.Products.IgnoreQueryFilters().Where(product => _productIds.Contains(product.Id)).ExecuteDeleteAsync();
        }

        if (_brandIds.Count > 0)
        {
            await catalog.Brands.IgnoreQueryFilters().Where(brand => _brandIds.Contains(brand.Id)).ExecuteDeleteAsync();
        }

        if (_categoryIds.Count > 0)
        {
            await catalog.Categories.IgnoreQueryFilters().Where(category => _categoryIds.Contains(category.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
