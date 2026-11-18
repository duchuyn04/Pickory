using System.Data.Common;
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
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// EXP-03: xuất đơn hàng ra Excel. Route dùng chung bộ lọc với danh sách, ghi nhật ký và chặn file quá lớn.
[Collection(CommerceCollection.Name)]
public sealed class AdminOrderExportTests : IClassFixture<CommerceWebApplicationFactory>, IAsyncLifetime
{
    private const string ExportsFixture = "exports-printing/exports.json";
    private const string ExportPath = "/api/admin/exports/orders";
    private const string OrdersPath = "/api/admin/orders";
    private const string ExportAuditAction = "commerce.orders.export";
    private const string OrdersSheetName = "Đơn hàng";
    private const string LinesSheetName = "Chi tiết sản phẩm";

    // Mốc cố định ứng với 10:15 ngày 09/11/2026 giờ Việt Nam, khớp ví dụ `orders export success`.
    private static readonly DateTime BaseTime = new(2026, 11, 9, 3, 15, 0, DateTimeKind.Utc);

    private readonly CommerceWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _kit;
    private readonly HttpClient _client;

    public AdminOrderExportTests(CommerceWebApplicationFactory factory, CommerceDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new AdminOrderTestKit(database);
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _kit.ResetAsync();
        await DeleteExportAuditLogsAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _kit.ResetAsync();
        await DeleteExportAuditLogsAsync();
    }

    // Phiên của nhân viên giữ đúng các quyền cho trước. Role riêng cho từng lần gọi nên test không phụ thuộc role mặc định.
    private async Task<(User Staff, string Cookie)> StaffWithPermissionsAsync(params string[] permissions)
    {
        var role = await IdentityTestData.CreateRoleAsync(_database, permissions);
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(_database, role.Code, "exp03");
        return (staff, $"pk_session={token}");
    }

    private async Task<(User Staff, string Cookie)> ExporterAsync() =>
        await StaffWithPermissionsAsync(Permissions.OrdersRead, Permissions.ReportsExport);

    private async Task<string> AdminCookieAsync()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, "exp03-admin");
        return $"pk_session={token}";
    }

    private Task<HttpResponseMessage> ExportAsync(string query, string cookie, HttpClient? client = null)
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

    // Tìm cột theo tiêu đề dòng 1. Test không phụ thuộc số thứ tự cột, vì thứ tự cột có test riêng.
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

    private static List<string> CodesOf(IXLWorksheet sheet, string header = "Mã đơn") =>
        Enumerable.Range(1, DataRowCount(sheet)).Select(row => TextOf(sheet, row, header)!).ToList();

    // Tên file thật trong header, để so với `TargetId` của nhật ký.
    private static string FileNameOf(HttpResponseMessage response)
    {
        var header = string.Join(", ", response.Content.Headers.GetValues("Content-Disposition"));
        return Regex.Match(header, "filename=\"(?<name>[^\"]+)\"").Groups["name"].Value;
    }

    private async Task<JsonElement> ListTotalAsync(string query, string cookie)
    {
        var path = query.Length == 0 ? OrdersPath : $"{OrdersPath}?{query}";
        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, cookie));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").GetProperty("total").Clone();
    }

    private async Task<List<AuditLog>> ExportAuditLogsAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.AsNoTracking().Where(log => log.Action == ExportAuditAction).ToListAsync();
    }

    private async Task DeleteExportAuditLogsAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.Action == ExportAuditAction).ExecuteDeleteAsync();
    }

    // Năm đơn có trạng thái, thanh toán, cờ và ngày khác nhau để mỗi bộ lọc chọn một tập con riêng.
    private async Task SeedFilterDatasetAsync()
    {
        await _kit.AddOrderAsync("EXP3-F1", BaseTime.AddDays(-1), OrderStatus.Shipping, PaymentStatus.Unpaid, PaymentMethod.Cod, lineCount: 2);
        await _kit.AddOrderAsync("EXP3-F2", BaseTime, OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Sepay, ["underpaid"]);
        await _kit.AddOrderAsync(
            "EXP3-F3", BaseTime.AddHours(1), OrderStatus.Delivered, PaymentStatus.Paid, PaymentMethod.Sepay,
            ["late_payment", "overpaid"], lineCount: 3, recipientName: "Trần Thị Minh Thư");
        await _kit.AddOrderAsync("EXP3-F4", new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc), OrderStatus.Cancelled);
        await _kit.AddOrderAsync(
            "EXP3-F5", BaseTime.AddHours(2), OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Cod,
            ["invoice_issued"], lineCount: 2, withInvoice: true);
    }

    private WebApplicationFactory<Program> FactoryWithMaxRows(int maxRows) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting("Exports:MaxRows", maxRows.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    // Contract: file thành công khớp ví dụ `orders export success` về header, tên sheet, tiêu đề và kiểu ô dòng 2.
    public async Task Export_Success_MatchesContractWorkbook()
    {
        // Đơn khớp bộ lọc của ví dụ, có hóa đơn VAT và hai dòng hàng để dòng 2 của cả hai sheet có dữ liệu.
        await _kit.AddOrderAsync(
            "EXP3-OK", BaseTime, OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Sepay, lineCount: 2, withInvoice: true);
        // Đơn khác trạng thái không được vào file.
        await _kit.AddOrderAsync("EXP3-SKIP", BaseTime, OrderStatus.Shipping);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=confirmed&from=2026-11-01&to=2026-11-09", cookie);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        WorkbookFixture.AssertMatches(ExportsFixture, "orders export success", bytes, response);
    }

    [Fact]
    // Contract: bộ lọc sai trả 400 `VALIDATION_ERROR` giống ví dụ `orders export invalid filter`.
    public async Task Export_InvalidFilter_Returns400MatchingContract()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("from=2026-11-09&to=2026-11-01", cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export invalid filter", response);
    }

    [Fact]
    // Contract: thiếu `reports:export` trả 403 giống ví dụ `orders export missing reports export permission`.
    public async Task Export_MissingReportsExport_Returns403MatchingContract()
    {
        var (_, cookie) = await StaffWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
    }

    [Fact]
    // Có `reports:export` nhưng thiếu `orders:read` cũng trả 403, và không có dòng nhật ký nào.
    public async Task Export_MissingOrdersRead_Returns403()
    {
        await _kit.AddOrderAsync("EXP3-A", BaseTime);
        var (_, cookie) = await StaffWithPermissionsAsync(Permissions.ReportsExport);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Đủ hai quyền thì nhận file. Đây là chiều đối chứng của hai test 403.
    public async Task Export_WithBothPermissions_Returns200()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // File chứa dữ liệu cá nhân của khách nên không được lưu bản sao ở trình duyệt hay proxy.
    public async Task Export_Success_SetsNoStore()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Tên file dùng đồng hồ của DI, giờ Việt Nam: 08:30 UTC ngày 10/11/2026 là 15:30 cùng ngày.
    public async Task Export_FileName_UsesClockFromDependencyInjection()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero));
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie, client);

        Assert.Equal("don-hang-20261110-1530.xlsx", FileNameOf(response));
    }

    [Theory]
    [InlineData("status=shipping", 1)]
    [InlineData("paymentStatus=paid", 3)]
    [InlineData("paymentMethod=sepay", 2)]
    [InlineData("flag=underpaid", 1)]
    [InlineData("from=2026-11-01&to=2026-11-30", 4)]
    [InlineData("search=Minh", 1)]
    [InlineData("status=confirmed&paymentMethod=cod", 1)]
    // Số dòng sheet "Đơn hàng" bằng `total` của danh sách cùng bộ lọc, nên hai route cùng thấy một tập đơn.
    public async Task Export_OrdersSheetRowCount_EqualsListTotal(string query, int expectedCount)
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();
        var listCookie = await AdminCookieAsync();

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var listTotal = (await ListTotalAsync(query, listCookie)).GetInt32();
        Assert.Equal(expectedCount, listTotal);
        Assert.Equal(listTotal, DataRowCount(workbook.Worksheet(OrdersSheetName)));
    }

    [Fact]
    // Sheet chi tiết có một dòng cho mỗi dòng hàng của các đơn trong sheet "Đơn hàng", và mã đơn thuộc sheet đó.
    public async Task Export_LinesSheet_HasOneRowPerOrderLineOfExportedOrders()
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        // `paymentStatus=paid` chọn F2 (1 dòng), F3 (3 dòng), F5 (2 dòng). Đơn F1 (2 dòng) và F4 không được vào sheet 2.
        using var response = await ExportAsync("paymentStatus=paid", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var orderCodes = CodesOf(workbook.Worksheet(OrdersSheetName));
        var lineCodes = CodesOf(workbook.Worksheet(LinesSheetName));
        Assert.Equal(["EXP3-F5", "EXP3-F3", "EXP3-F2"], orderCodes);
        Assert.Equal(6, lineCodes.Count);
        Assert.All(lineCodes, code => Assert.Contains(code, orderCodes));
        // Dòng hàng đi theo thứ tự đơn của sheet 1: mọi dòng của một đơn nằm liền nhau.
        Assert.Equal(["EXP3-F5", "EXP3-F5", "EXP3-F3", "EXP3-F3", "EXP3-F3", "EXP3-F2"], lineCodes);
    }

    [Fact]
    // Tổng "Số sản phẩm" của sheet 1 bằng tổng "Số lượng" của sheet 2, vì cả hai tính từ cùng dòng hàng.
    public async Task Export_ItemCountColumn_EqualsSumOfLineQuantities()
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var orders = workbook.Worksheet(OrdersSheetName);
        var lines = workbook.Worksheet(LinesSheetName);
        var itemCount = Enumerable.Range(1, DataRowCount(orders)).Sum(row => CellOf(orders, row, "Số sản phẩm").GetDouble());
        var quantity = Enumerable.Range(1, DataRowCount(lines)).Sum(row => CellOf(lines, row, "Số lượng").GetDouble());
        // Kit tạo mỗi dòng hàng với số lượng 2. Năm đơn có 2 + 1 + 3 + 1 + 2 = 9 dòng hàng, nên tổng số lượng là 18.
        Assert.Equal(18, itemCount);
        Assert.Equal(itemCount, quantity);
    }

    [Fact]
    // Nhãn tiếng Việt, địa chỉ ghép, 4 cột VAT, cột "Có" và ghi chú từ các cờ còn lại.
    public async Task Export_InvoiceOrder_HasVietnameseLabelsVatColumnsAndNote()
    {
        await _kit.AddOrderAsync(
            "EXP3-VAT", BaseTime, OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Sepay,
            ["late_payment", "invoice_issued", "overpaid"], withInvoice: true);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(OrdersSheetName);
        Assert.Equal("EXP3-VAT", TextOf(sheet, 1, "Mã đơn"));
        Assert.Equal("Đã xác nhận", TextOf(sheet, 1, "Trạng thái đơn"));
        Assert.Equal("Đã thanh toán", TextOf(sheet, 1, "Trạng thái thanh toán"));
        Assert.Equal("Chuyển khoản SePay", TextOf(sheet, 1, "Phương thức thanh toán"));
        Assert.Equal("Nguyễn Văn An", TextOf(sheet, 1, "Tên khách"));
        Assert.Equal("0901234567", TextOf(sheet, 1, "Số điện thoại"));
        Assert.Equal("an@example.com", TextOf(sheet, 1, "Email"));
        Assert.Equal("12 Phố Test, Phường Ba Đình, Hà Nội", TextOf(sheet, 1, "Địa chỉ giao"));
        Assert.Equal("Công ty Thử", TextOf(sheet, 1, "Tên công ty"));
        Assert.Equal("0312345678", TextOf(sheet, 1, "Mã số thuế"));
        Assert.Equal("1 Đường Thử", TextOf(sheet, 1, "Địa chỉ xuất hóa đơn"));
        Assert.Equal("ketoan@example.com", TextOf(sheet, 1, "Email nhận hóa đơn"));
        Assert.Equal("Có", TextOf(sheet, 1, "Đã xuất hóa đơn VAT"));
        Assert.Equal("Thanh toán trễ, Thanh toán dư", TextOf(sheet, 1, "Ghi chú đơn"));
    }

    [Fact]
    // Mã số thuế là ô chữ. Số 0 ở đầu mã không bị Excel cắt.
    public async Task Export_TaxCode_IsTextCell()
    {
        await _kit.AddOrderAsync("EXP3-TAX", BaseTime, withInvoice: true);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(XLDataType.Text, CellOf(workbook.Worksheet(OrdersSheetName), 1, "Mã số thuế").DataType);
    }

    [Fact]
    // Đơn không có thông tin hóa đơn để trống 4 cột VAT, ghi "Không" và không có ghi chú.
    public async Task Export_OrderWithoutInvoice_HasBlankVatColumnsAndNoNote()
    {
        await _kit.AddOrderAsync("EXP3-NOVAT", BaseTime, paymentMethod: PaymentMethod.Cod);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(OrdersSheetName);
        Assert.Equal("COD", TextOf(sheet, 1, "Phương thức thanh toán"));
        Assert.Null(TextOf(sheet, 1, "Tên công ty"));
        Assert.Null(TextOf(sheet, 1, "Mã số thuế"));
        Assert.Null(TextOf(sheet, 1, "Địa chỉ xuất hóa đơn"));
        Assert.Null(TextOf(sheet, 1, "Email nhận hóa đơn"));
        Assert.Equal("Không", TextOf(sheet, 1, "Đã xuất hóa đơn VAT"));
        Assert.Null(TextOf(sheet, 1, "Ghi chú đơn"));
    }

    [Fact]
    // Đơn có cờ hóa đơn nhưng không có cờ khác thì cột VAT là "Có" và ghi chú để trống.
    public async Task Export_OnlyInvoiceIssuedFlag_SaysYesAndLeavesNoteBlank()
    {
        await _kit.AddOrderAsync("EXP3-INV", BaseTime, flags: ["invoice_issued"], withInvoice: true);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(OrdersSheetName);
        Assert.Equal("Có", TextOf(sheet, 1, "Đã xuất hóa đơn VAT"));
        Assert.Null(TextOf(sheet, 1, "Ghi chú đơn"));
    }

    [Fact]
    // Dòng hàng có mã SKU, tên, phân loại, số lượng, đơn giá và thành tiền đúng với dữ liệu đã lưu.
    public async Task Export_LinesSheet_HasLineDetails()
    {
        await _kit.AddOrderAsync("EXP3-LINE", BaseTime);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(LinesSheetName);
        Assert.Equal("EXP3-LINE", TextOf(sheet, 1, "Mã đơn"));
        Assert.Equal("SKU-EXP3-LINE-0", TextOf(sheet, 1, "Mã SKU"));
        Assert.Equal("Sản phẩm 0", TextOf(sheet, 1, "Tên sản phẩm"));
        Assert.Equal("Đỏ", TextOf(sheet, 1, "Phân loại"));
        Assert.Equal(2, CellOf(sheet, 1, "Số lượng").GetDouble());
        Assert.Equal(100_000, CellOf(sheet, 1, "Đơn giá").GetDouble());
        Assert.Equal(200_000, CellOf(sheet, 1, "Thành tiền").GetDouble());
    }

    [Fact]
    // Thành tiền nhân bằng `long`. Đơn giá 100 triệu nhân 100 là 10 tỷ, vượt `int.MaxValue`.
    public async Task Export_LineTotal_DoesNotOverflowInt()
    {
        var order = await _kit.AddOrderAsync("EXP3-BIG", BaseTime);
        await using (var commerce = _database.CreateCommerceContext())
        {
            await commerce.OrderItems
                .Where(item => item.OrderId == order.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.UnitPrice, 100_000_000)
                    .SetProperty(item => item.Quantity, 100));
        }

        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(10_000_000_000d, CellOf(workbook.Worksheet(LinesSheetName), 1, "Thành tiền").GetDouble());
        Assert.Equal(100, CellOf(workbook.Worksheet(OrdersSheetName), 1, "Số sản phẩm").GetDouble());
    }

    [Fact]
    // Sheet 1 xếp đơn mới nhất lên đầu, giống danh sách. Hai đơn cùng ngày tạo xếp theo `Id` để thứ tự ổn định.
    public async Task Export_OrdersSheet_IsNewestFirstThenById()
    {
        await _kit.AddOrderAsync("EXP3-OLD", BaseTime.AddHours(-5));
        await _kit.AddOrderAsync("EXP3-TIE-B", BaseTime, id: "00000000-0000-0000-0000-00000000000b");
        await _kit.AddOrderAsync("EXP3-TIE-A", BaseTime, id: "00000000-0000-0000-0000-00000000000a");
        await _kit.AddOrderAsync("EXP3-NEW", BaseTime.AddHours(5));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(["EXP3-NEW", "EXP3-TIE-A", "EXP3-TIE-B", "EXP3-OLD"], CodesOf(workbook.Worksheet(OrdersSheetName)));
    }

    [Theory]
    [InlineData("page=2&limit=1")]
    [InlineData("page=abc&limit=0")]
    [InlineData("limit=1000")]
    // `page` và `limit` bị bỏ qua, kể cả giá trị mà danh sách coi là sai. File luôn chứa mọi đơn khớp bộ lọc.
    public async Task Export_IgnoresPageAndLimit(string query)
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(5, DataRowCount(workbook.Worksheet(OrdersSheetName)));
    }

    [Fact]
    // Danh sách rỗng vẫn trả file hợp lệ chỉ có dòng tiêu đề, và vẫn ghi nhật ký với `rowCount` bằng 0.
    public async Task Export_NoMatchingOrders_ReturnsHeaderOnlyFile()
    {
        await _kit.AddOrderAsync("EXP3-EMPTY", BaseTime, OrderStatus.Confirmed);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=returned", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(0, DataRowCount(workbook.Worksheet(OrdersSheetName)));
        Assert.Equal(0, DataRowCount(workbook.Worksheet(LinesSheetName)));
        var log = Assert.Single(await ExportAuditLogsAsync());
        Assert.Equal(0, log.After!.RootElement.GetProperty("rowCount").GetInt32());
    }

    [Fact]
    // Ví dụ `orders export too large`: 3 đơn và giới hạn 2 trả 422 và không ghi nhật ký.
    public async Task Export_OverLimit_Returns422WithoutAuditLog()
    {
        await _kit.AddOrderAsync("EXP3-L1", BaseTime.AddMinutes(1));
        await _kit.AddOrderAsync("EXP3-L2", BaseTime.AddMinutes(2));
        await _kit.AddOrderAsync("EXP3-L3", BaseTime.AddMinutes(3));
        var (_, cookie) = await ExporterAsync();
        using var limitedFactory = FactoryWithMaxRows(2);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, limitedClient);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Đúng bằng giới hạn thì vẫn trả file. Chỉ khi vượt giới hạn một đơn mới bị từ chối.
    public async Task Export_ExactlyAtLimit_ReturnsFile()
    {
        await _kit.AddOrderAsync("EXP3-E1", BaseTime.AddMinutes(1));
        await _kit.AddOrderAsync("EXP3-E2", BaseTime.AddMinutes(2));
        await _kit.AddOrderAsync("EXP3-E3", BaseTime.AddMinutes(3));
        var (_, cookie) = await ExporterAsync();
        using var limitedFactory = FactoryWithMaxRows(3);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, limitedClient);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(3, DataRowCount(workbook.Worksheet(OrdersSheetName)));
    }

    [Fact]
    // Xuất thành công ghi đúng 1 dòng nhật ký: người xuất, tên file, bộ lọc có giá trị và số đơn.
    public async Task Export_Success_WritesExactlyOneAuditLog()
    {
        await SeedFilterDatasetAsync();
        var (staff, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=confirmed&page=2&limit=1", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var log = Assert.Single(await ExportAuditLogsAsync());
        Assert.Equal(staff.Id, log.ActorId);
        Assert.Equal("export", log.TargetType);
        Assert.Equal(FileNameOf(response), log.TargetId);
        // Cột `before` không mang dữ liệu. `AuditRecorder` luôn lưu JSON `null`, không lưu SQL `NULL`.
        Assert.NotNull(log.Before);
        Assert.Equal(JsonValueKind.Null, log.Before.RootElement.ValueKind);
        var after = log.After!.RootElement;
        Assert.Equal(2, after.GetProperty("rowCount").GetInt32());
        Assert.Equal(DataRowCount(workbook.Worksheet(OrdersSheetName)), after.GetProperty("rowCount").GetInt32());
        // `filters` chỉ có tham số lọc có giá trị. `page` và `limit` không có mặt.
        var filters = after.GetProperty("filters");
        Assert.Equal(["status"], filters.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("confirmed", filters.GetProperty("status").GetString());
    }

    [Fact]
    // Cả bảy tham số lọc đều đi vào `filters` với đúng tên tham số của query.
    public async Task Export_AllFilters_AreRecordedByQueryParameterName()
    {
        await _kit.AddOrderAsync(
            "EXP3-ALL", BaseTime, OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Sepay, ["underpaid"], recipientName: "An Binh");
        var (_, cookie) = await ExporterAsync();

        // Từ khóa có khoảng trắng thừa. Nhật ký ghi từ khóa đã chuẩn hóa, không ghi mẫu `ILIKE`.
        using var response = await ExportAsync(
            "status=confirmed&paymentStatus=paid&paymentMethod=sepay&flag=underpaid&from=2026-11-01&to=2026-11-30&search=%20%20An%20%20Binh%20", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(1, DataRowCount(workbook.Worksheet(OrdersSheetName)));
        var log = Assert.Single(await ExportAuditLogsAsync());
        var filters = log.After!.RootElement.GetProperty("filters");
        var actual = filters.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString());
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["status"] = "confirmed",
                ["paymentStatus"] = "paid",
                ["paymentMethod"] = "sepay",
                ["flag"] = "underpaid",
                ["from"] = "2026-11-01",
                ["to"] = "2026-11-30",
                ["search"] = "An Binh",
            },
            actual);
    }

    [Fact]
    // Không có bộ lọc thì `filters` là object rỗng, không phải `null`.
    public async Task Export_WithoutFilters_RecordsEmptyFiltersObject()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        var log = Assert.Single(await ExportAuditLogsAsync());
        var filters = log.After!.RootElement.GetProperty("filters");
        Assert.Equal(JsonValueKind.Object, filters.ValueKind);
        Assert.Empty(filters.EnumerateObject());
    }

    [Fact]
    // Lỗi bộ lọc xảy ra trước khi chạm DB, nên không có dòng nhật ký nào.
    public async Task Export_InvalidFilter_WritesNoAuditLog()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=khong_co", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Ghi nhật ký lỗi thì không trả file. Nhân viên không nhận file xuất mà không có dấu vết.
    public async Task Export_WhenAuditWriteFails_ReturnsNoFile()
    {
        await _kit.AddOrderAsync("EXP3-FAIL", BaseTime);
        var (_, cookie) = await ExporterAsync();
        using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, FailingAuditLogWriter>();
        }));
        using var failingClient = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, failingClient);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Content.Headers.Contains("Content-Disposition"));
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Tạo file lỗi thì không ghi nhật ký và không trả file.
    // Test ép lỗi bằng ô chữ trên 32.767 ký tự, đầu vào không có thật qua checkout, nên đừng xóa nhầm.
    public async Task Export_WhenWorkbookCannotBeBuilt_WritesNoAuditLogAndReturnsNoFile()
    {
        await _kit.AddOrderAsync("EXP3-HUGE", BaseTime, recipientName: new string('A', 40_000));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Content.Headers.Contains("Content-Disposition"));
        Assert.Empty(await ExportAuditLogsAsync());
    }

    private (WebApplicationFactory<Program> Factory, HttpClient Client) FactoryWithLineBatchSize(int orderBatchSize)
    {
        // Lô nhỏ làm vài đơn đi qua nhánh nhiều lô của việc đọc dòng hàng, nhánh mà mọi lần xuất trên 2.000 đơn đều chạy.
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Pickory.Modules.Commerce.Orders.Admin.OrderExportLineBatching>();
            services.AddSingleton(new Pickory.Modules.Commerce.Orders.Admin.OrderExportLineBatching(orderBatchSize));
        }));
        return (factory, factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));
    }

    [Fact]
    // Lô 2 với 5 đơn có 0, 1, 2, 3, 1 dòng hàng. Sheet 2 giữ thứ tự đơn của sheet 1 và mỗi dòng hàng xuất hiện đúng một lần.
    public async Task Export_MultipleLineBatches_KeepsOrderLinesAndTotals()
    {
        var lineCounts = new[] { 0, 1, 2, 3, 1 };
        // Đơn S1 mới nhất nên đứng đầu sheet 1. Mốc thời gian giảm dần theo chỉ số.
        for (var index = 0; index < lineCounts.Length; index++)
        {
            await _kit.AddOrderAsync($"EXP3-S{index + 1}", BaseTime.AddMinutes(10 - index), lineCount: lineCounts[index]);
        }

        var (_, cookie) = await ExporterAsync();
        var (factory, client) = FactoryWithLineBatchSize(2);
        using var batchedFactory = factory;
        using var batchedClient = client;

        using var response = await ExportAsync(string.Empty, cookie, client);
        using var workbook = await ReadWorkbookAsync(response);

        var orders = workbook.Worksheet(OrdersSheetName);
        var lines = workbook.Worksheet(LinesSheetName);
        Assert.Equal(["EXP3-S1", "EXP3-S2", "EXP3-S3", "EXP3-S4", "EXP3-S5"], CodesOf(orders));
        // Kit tạo mỗi dòng hàng với số lượng 2, nên "Số sản phẩm" bằng số dòng hàng nhân 2.
        var itemCounts = Enumerable.Range(1, 5).Select(row => CellOf(orders, row, "Số sản phẩm").GetDouble()).ToArray();
        Assert.Equal([0d, 2d, 4d, 6d, 2d], itemCounts);
        Assert.Equal(
            ["EXP3-S2", "EXP3-S3", "EXP3-S3", "EXP3-S4", "EXP3-S4", "EXP3-S4", "EXP3-S5"],
            CodesOf(lines));
        var skuCodes = Enumerable.Range(1, DataRowCount(lines)).Select(row => TextOf(lines, row, "Mã SKU")!).ToList();
        Assert.Equal(skuCodes.Count, skuCodes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    // Đơn không có dòng hàng vẫn có dòng ở sheet 1 với "Số sản phẩm" bằng 0, và sheet 2 không có dòng nào.
    public async Task Export_OrderWithoutLines_HasZeroItemCountAndNoLineRows()
    {
        await _kit.AddOrderAsync("EXP3-NOLINE", BaseTime, lineCount: 0);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var orders = workbook.Worksheet(OrdersSheetName);
        Assert.Equal(["EXP3-NOLINE"], CodesOf(orders));
        Assert.Equal(0, CellOf(orders, 1, "Số sản phẩm").GetDouble());
        Assert.Equal(0, DataRowCount(workbook.Worksheet(LinesSheetName)));
    }

    [Fact]
    // Đơn cũ thiếu khóa `email` trong `contact` vẫn xuất được, và ô email để trống.
    public async Task Export_OrderWithoutContactEmail_LeavesEmailBlank()
    {
        await _kit.AddOrderAsync("EXP3-NOMAIL", BaseTime, omitRecipientEmail: true);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(OrdersSheetName);
        Assert.Null(TextOf(sheet, 1, "Email"));
        Assert.Equal("Nguyễn Văn An", TextOf(sheet, 1, "Tên khách"));
    }

    [Fact]
    // Dòng hàng trong một đơn đi theo thứ tự `Id`, không theo thứ tự lưu trong bảng.
    public async Task Export_LinesOfOneOrder_AreOrderedById()
    {
        var order = await _kit.AddOrderAsync("EXP3-IDS", BaseTime, lineCount: 3);
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Gán `Id` giảm dần theo thứ tự SKU, rồi cập nhật theo thứ tự SKU 0, 1, 2. Thứ tự lưu là 0, 1, 2 còn thứ tự `Id` là 2, 1, 0.
            for (var index = 0; index < 3; index++)
            {
                var skuCode = $"SKU-EXP3-IDS-{index}";
                var newId = $"item-{(char)('c' - index)}";
                await commerce.OrderItems
                    .Where(item => item.OrderId == order.Id && item.SkuCodeSnapshot == skuCode)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Id, newId));
            }
        }

        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var lines = workbook.Worksheet(LinesSheetName);
        var skuCodes = Enumerable.Range(1, DataRowCount(lines)).Select(row => TextOf(lines, row, "Mã SKU")!).ToArray();
        Assert.Equal(["SKU-EXP3-IDS-2", "SKU-EXP3-IDS-1", "SKU-EXP3-IDS-0"], skuCodes);
    }

    private sealed class FailingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken)
        {
            // Cố ý ném lỗi để kiểm tra route không trả file khi nhật ký không ghi được.
            throw new InvalidOperationException("Lỗi cố ý từ nhật ký thao tác");
        }
    }
}
