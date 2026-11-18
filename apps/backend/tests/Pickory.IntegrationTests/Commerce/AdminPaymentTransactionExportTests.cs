using System.Data.Common;
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
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// EXP-04: xuất giao dịch SePay ra Excel. Route dùng chung bộ lọc với danh sách, ghi nhật ký và chặn file quá lớn.
[Collection(CommerceCollection.Name)]
public sealed class AdminPaymentTransactionExportTests : IClassFixture<CommerceWebApplicationFactory>, IAsyncLifetime
{
    private const string ExportsFixture = "exports-printing/exports.json";
    private const string ExportPath = "/api/admin/exports/payment-transactions";
    private const string TransactionsPath = "/api/admin/payments/transactions";
    private const string ExportAuditAction = "commerce.payment-transactions.export";
    private const string SheetName = "Giao dịch SePay";

    // Mốc cố định ứng với 10:15 ngày 09/11/2026 giờ Việt Nam, khớp ví dụ `payment transactions export success`.
    private static readonly DateTime BaseTime = new(2026, 11, 9, 3, 15, 0, DateTimeKind.Utc);

    private readonly CommerceWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _kit;
    private readonly HttpClient _client;

    public AdminPaymentTransactionExportTests(CommerceWebApplicationFactory factory, CommerceDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new AdminOrderTestKit(database);
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Kit xóa cả giao dịch lẫn đơn, vì danh sách đếm cả bảng và dữ liệu của test khác làm lệch tổng.
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
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(_database, role.Code, "exp04");
        return (staff, $"pk_session={token}");
    }

    private async Task<(User Staff, string Cookie)> ExporterAsync() =>
        await StaffWithPermissionsAsync(Permissions.PaymentsRead, Permissions.ReportsExport);

    private async Task<string> AdminCookieAsync()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, "exp04-admin");
        return $"pk_session={token}";
    }

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

    private static List<string> ProviderIdsOf(IXLWorksheet sheet) =>
        Enumerable.Range(1, DataRowCount(sheet)).Select(row => TextOf(sheet, row, "Mã giao dịch SePay")!).ToList();

    // Tên file thật trong header, để so với `TargetId` của nhật ký.
    private static string FileNameOf(HttpResponseMessage response)
    {
        var header = string.Join(", ", response.Content.Headers.GetValues("Content-Disposition"));
        return Regex.Match(header, "filename=\"(?<name>[^\"]+)\"").Groups["name"].Value;
    }

    private async Task<int> ListTotalAsync(string query, string cookie)
    {
        var path = query.Length == 0 ? TransactionsPath : $"{TransactionsPath}?{query}";
        using var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, cookie));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").GetProperty("total").GetInt32();
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

    private async Task AddTransactionAsync(
        string id,
        string matchStatus,
        DateTime receivedAt,
        string? orderId = null,
        int amount = 220_000,
        string? content = null)
    {
        await using var commerce = _database.CreateCommerceContext();
        commerce.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = id,
            Provider = "sepay",
            ProviderTxId = "FT-" + id,
            Amount = amount,
            Content = content ?? "Noi dung " + id,
            // Payload thô chứa một dấu hiệu riêng để test kiểm file không rò rỉ cột `raw`.
            Raw = JsonSerializer.SerializeToDocument(new { secretMarker = "RAW-SECRET-" + id }),
            OrderId = orderId,
            MatchStatus = matchStatus,
            ReceivedAt = receivedAt,
        });
        await commerce.SaveChangesAsync();
    }

    // Bảy giao dịch có trạng thái khớp và cờ đơn khác nhau để mỗi bộ lọc chọn một tập con riêng.
    private async Task SeedFilterDatasetAsync()
    {
        var underpaid = await _kit.AddOrderAsync("EXP4-U", BaseTime, OrderStatus.PendingPayment, paymentMethod: PaymentMethod.Sepay, flags: ["underpaid"]);
        var overpaid = await _kit.AddOrderAsync(
            "EXP4-O", BaseTime, OrderStatus.PendingConfirmation, PaymentStatus.Paid, PaymentMethod.Sepay, ["late_payment", "overpaid"]);
        var plain = await _kit.AddOrderAsync("EXP4-P", BaseTime, OrderStatus.PendingConfirmation, PaymentStatus.Paid, PaymentMethod.Sepay);
        var strayUnderpaid = await _kit.AddOrderAsync("EXP4-S", BaseTime, paymentMethod: PaymentMethod.Sepay, flags: ["underpaid"]);

        await AddTransactionAsync("t1", "unmatched", BaseTime.AddMinutes(1));
        await AddTransactionAsync("t2", "unmatched", BaseTime.AddMinutes(2));
        await AddTransactionAsync("t3", "matched", BaseTime.AddMinutes(3), underpaid.Id, amount: 100_000);
        await AddTransactionAsync("t4", "matched", BaseTime.AddMinutes(4), overpaid.Id, amount: 300_000);
        await AddTransactionAsync("t5", "matched", BaseTime.AddMinutes(5), plain.Id);
        await AddTransactionAsync("t6", "ignored", BaseTime.AddMinutes(6));
        // Giao dịch chưa khớp nhưng đơn gắn kèm có cờ `underpaid`, để bộ lọc kết hợp khác từng bộ lọc riêng lẻ.
        await AddTransactionAsync("t7", "unmatched", BaseTime.AddMinutes(7), strayUnderpaid.Id);
    }

    private WebApplicationFactory<Program> FactoryWithMaxRows(int maxRows) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting("Exports:MaxRows", maxRows.ToString(CultureInfo.InvariantCulture)));

    [Fact]
    // Contract: file thành công khớp ví dụ `payment transactions export success` về header, tên sheet, tiêu đề và kiểu ô dòng 2.
    public async Task Export_Success_MatchesContractWorkbook()
    {
        // Giao dịch chưa khớp có số tiền lớn như `sampleRow`, nên dòng 2 có đủ kiểu số, ngày giờ và chữ.
        await AddTransactionAsync("tx-ok", "unmatched", BaseTime.AddMinutes(5), amount: 25_370_000, content: "CK tien hang thang 11");
        // Giao dịch khác trạng thái không được vào file của bộ lọc `matchStatus=unmatched`.
        await AddTransactionAsync("tx-skip", "ignored", BaseTime.AddMinutes(6));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("matchStatus=unmatched", cookie);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        WorkbookFixture.AssertMatches(ExportsFixture, "payment transactions export success", bytes, response);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(1, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Thiếu `reports:export` trả 403, không ghi nhật ký.
    // Dùng ví dụ lỗi của route đơn hàng, vì fixture chưa có ví dụ 403 riêng cho route này.
    public async Task Export_MissingReportsExport_Returns403()
    {
        var (_, cookie) = await StaffWithPermissionsAsync(Permissions.PaymentsRead);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Có `reports:export` nhưng thiếu `payments:read` cũng trả 403, không ghi nhật ký.
    // Dùng ví dụ lỗi của route đơn hàng, vì fixture chưa có ví dụ 403 riêng cho route này.
    public async Task Export_MissingPaymentsRead_Returns403()
    {
        var (_, cookie) = await StaffWithPermissionsAsync(Permissions.ReportsExport);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Chưa đăng nhập thì trả 401, và không có dòng nhật ký nào.
    public async Task Export_WithoutSession_Returns401()
    {
        using var response = await ExportAsync(string.Empty, cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
    // Nội dung chuyển khoản có thể chứa tên người chuyển, nên file không được lưu bản sao ở trình duyệt hay proxy.
    public async Task Export_Success_SetsNoStore()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Lỗi bộ lọc cũng mang `no-store`, vì header đặt trước bước kiểm tham số.
    public async Task Export_InvalidFilter_SetsNoStore()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("matchStatus=khong_co", cookie);

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
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie, client);

        Assert.Equal("giao-dich-sepay-20261110-1530.xlsx", FileNameOf(response));
    }

    [Theory]
    [InlineData("", 7)]
    [InlineData("matchStatus=unmatched", 3)]
    [InlineData("matchStatus=matched", 3)]
    [InlineData("matchStatus=ignored", 1)]
    [InlineData("orderFlag=underpaid", 2)]
    [InlineData("orderFlag=overpaid", 1)]
    [InlineData("matchStatus=matched&orderFlag=underpaid", 1)]
    // Số dòng của sheet bằng `total` của danh sách cùng bộ lọc, nên hai route cùng thấy một tập giao dịch.
    public async Task Export_RowCount_EqualsListTotal(string query, int expectedCount)
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();
        var listCookie = await AdminCookieAsync();

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var listTotal = await ListTotalAsync(query, listCookie);
        Assert.Equal(expectedCount, listTotal);
        Assert.Equal(listTotal, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Giao dịch chưa gắn đơn bị loại khi lọc `orderFlag`, như danh sách. Chỉ hai giao dịch gắn đơn `underpaid` còn lại.
    public async Task Export_OrderFlagFilter_SkipsUnattachedTransactions()
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("orderFlag=underpaid", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(["FT-t7", "FT-t3"], ProviderIdsOf(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Giao dịch chưa khớp đơn nào để trống cả bốn cột cuối. Ô trống khác số 0 và khác chuỗi rỗng.
    public async Task Export_UnmatchedTransaction_LeavesFourOrderCellsBlank()
    {
        await AddTransactionAsync("tx-free", "unmatched", BaseTime);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        foreach (var header in new[] { "Mã đơn", "Tổng tiền đơn", "Trạng thái thanh toán của đơn", "Ghi chú đơn" })
        {
            var cell = CellOf(sheet, 1, header);
            Assert.True(cell.IsEmpty(), $"Ô \"{header}\" phải trống.");
            Assert.Equal(XLDataType.Blank, cell.DataType);
        }

        Assert.Equal("Chưa khớp", TextOf(sheet, 1, "Trạng thái khớp"));
    }

    [Fact]
    // Giao dịch gắn đơn có mã đơn, tổng tiền, nhãn trạng thái thanh toán và ghi chú đúng với đơn.
    public async Task Export_MatchedTransaction_HasOrderCodeTotalPaymentStatusAndNote()
    {
        var order = await _kit.AddOrderAsync(
            "EXP4-M", BaseTime, OrderStatus.PendingConfirmation, PaymentStatus.Paid, PaymentMethod.Sepay, ["late_payment", "overpaid"]);
        await AddTransactionAsync("tx-m", "matched", BaseTime, order.Id, amount: 250_000);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal("FT-tx-m", TextOf(sheet, 1, "Mã giao dịch SePay"));
        Assert.Equal(250_000, CellOf(sheet, 1, "Số tiền").GetDouble());
        Assert.Equal("Noi dung tx-m", TextOf(sheet, 1, "Nội dung chuyển khoản"));
        Assert.Equal("Đã khớp", TextOf(sheet, 1, "Trạng thái khớp"));
        Assert.Equal("EXP4-M", TextOf(sheet, 1, "Mã đơn"));
        // Kit tạo đơn một dòng hàng: 200.000 tạm tính, cộng 30.000 phí giao, trừ 10.000 giảm phí.
        Assert.Equal(220_000, CellOf(sheet, 1, "Tổng tiền đơn").GetDouble());
        Assert.Equal("Đã thanh toán", TextOf(sheet, 1, "Trạng thái thanh toán của đơn"));
        // Đơn nhiều cờ nối bằng cùng dấu nối với file xuất đơn hàng.
        Assert.Equal("Thanh toán trễ, Thanh toán dư", TextOf(sheet, 1, "Ghi chú đơn"));
    }

    [Fact]
    // Đơn không có cờ thì ô ghi chú trống, nhưng ba ô còn lại của đơn vẫn có giá trị.
    public async Task Export_MatchedTransactionWithoutFlags_LeavesOnlyNoteBlank()
    {
        var order = await _kit.AddOrderAsync("EXP4-N", BaseTime, paymentMethod: PaymentMethod.Sepay);
        await AddTransactionAsync("tx-n", "matched", BaseTime, order.Id);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal("EXP4-N", TextOf(sheet, 1, "Mã đơn"));
        Assert.Equal("Chưa thanh toán", TextOf(sheet, 1, "Trạng thái thanh toán của đơn"));
        Assert.Null(TextOf(sheet, 1, "Ghi chú đơn"));
    }

    [Fact]
    // Đơn có tổng tiền bằng 0 vẫn ghi ô số 0. Ô trống chỉ dành cho giao dịch chưa gắn đơn.
    public async Task Export_MatchedOrderWithZeroTotal_WritesZeroNumberCell()
    {
        var order = await _kit.AddOrderAsync("EXP4-ZERO", BaseTime, paymentMethod: PaymentMethod.Sepay);
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Kit luôn tạo đơn có tiền. Ghi đè tổng tiền để có đơn 0 đồng.
            await commerce.Orders
                .Where(candidate => candidate.Id == order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Total, 0));
        }

        await AddTransactionAsync("tx-zero", "matched", BaseTime, order.Id);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var cell = CellOf(workbook.Worksheet(SheetName), 1, "Tổng tiền đơn");
        Assert.False(cell.IsEmpty());
        Assert.Equal(XLDataType.Number, cell.DataType);
        Assert.Equal(0, cell.GetDouble());
    }

    [Fact]
    // Thời điểm nhận tiền đổi sang giờ Việt Nam và là ô ngày giờ. Số tiền là ô số.
    public async Task Export_ReceivedAt_IsVietnamLocalDateTimeCell()
    {
        await AddTransactionAsync("tx-time", "unmatched", BaseTime);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        var receivedAt = CellOf(sheet, 1, "Thời điểm nhận tiền");
        Assert.Equal(XLDataType.DateTime, receivedAt.DataType);
        Assert.Equal(new DateTime(2026, 11, 9, 10, 15, 0), receivedAt.GetDateTime());
        Assert.Equal(XLDataType.Number, CellOf(sheet, 1, "Số tiền").DataType);
    }

    [Fact]
    // Nội dung chuyển khoản bắt đầu bằng `=` là ô chữ, không phải công thức Excel.
    public async Task Export_ContentStartingWithEquals_IsTextNotFormula()
    {
        await AddTransactionAsync("tx-formula", "unmatched", BaseTime, content: "=1+1");
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var cell = CellOf(workbook.Worksheet(SheetName), 1, "Nội dung chuyển khoản");
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.False(cell.HasFormula);
        Assert.Equal("=1+1", cell.GetString());
    }

    [Fact]
    // File không chứa cột `raw`. Payload thô của nhà cung cấp không bao giờ rời DB.
    public async Task Export_NeverContainsRawPayload()
    {
        await AddTransactionAsync("tx-raw", "unmatched", BaseTime);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        var lastColumn = sheet.LastColumnUsed()!.ColumnNumber();
        var allText = Enumerable.Range(1, lastColumn).Select(column => sheet.Cell(2, column).GetString());
        Assert.DoesNotContain(allText, text => text.Contains("RAW-SECRET", StringComparison.Ordinal));
        Assert.Equal(9, lastColumn);
    }

    [Fact]
    // Giao dịch mới nhận tiền lên đầu, giống danh sách. Hai giao dịch cùng `ReceivedAt` xếp theo `Id`.
    public async Task Export_IsNewestFirstThenById()
    {
        await AddTransactionAsync("old", "unmatched", BaseTime.AddHours(-5));
        await AddTransactionAsync("tie-b", "unmatched", BaseTime);
        await AddTransactionAsync("tie-a", "unmatched", BaseTime);
        await AddTransactionAsync("new", "unmatched", BaseTime.AddHours(5));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(["FT-new", "FT-tie-a", "FT-tie-b", "FT-old"], ProviderIdsOf(workbook.Worksheet(SheetName)));
    }

    [Theory]
    [InlineData("page=2&limit=1")]
    [InlineData("page=abc&limit=0")]
    [InlineData("limit=abc")]
    [InlineData("limit=1000")]
    // Tên tham số phân trang không phân biệt hoa thường, nên `PAGE` và `Limit` cũng bị bỏ qua.
    [InlineData("PAGE=abc&Limit=abc")]
    // `page` và `limit` bị bỏ qua, kể cả giá trị mà danh sách coi là sai. File luôn chứa mọi giao dịch khớp bộ lọc.
    public async Task Export_IgnoresPageAndLimit(string query)
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(7, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Không có giao dịch nào vẫn trả file hợp lệ chỉ có dòng tiêu đề, và vẫn ghi nhật ký với `rowCount` bằng 0.
    public async Task Export_NoTransactions_ReturnsHeaderOnlyFileAndAuditLog()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(0, DataRowCount(sheet));
        Assert.Equal("Mã giao dịch SePay", sheet.Cell(1, 1).GetString());
        var log = Assert.Single(await ExportAuditLogsAsync());
        Assert.Equal(0, log.After!.RootElement.GetProperty("rowCount").GetInt32());
    }

    [Theory]
    [InlineData("matchStatus=khong_co", "matchStatus")]
    [InlineData("orderFlag=khong_co", "orderFlag")]
    // Cờ hợp lệ của đơn nhưng ngoài tập của màn đối soát cũng bị từ chối, giống danh sách.
    [InlineData("orderFlag=late_payment", "orderFlag")]
    [InlineData("matchStatus=matched&matchStatus=ignored", "matchStatus")]
    // Giá trị lạ và tham số lặp trả 400 `VALIDATION_ERROR` cho đúng tham số, và không có dòng nhật ký nào.
    public async Task Export_InvalidFilter_Returns400ForTheParameterAndWritesNoAuditLog(string query, string parameter)
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(query, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var keys = error.GetProperty("details").EnumerateArray().Select(detail => detail.GetProperty("key").GetString());
        Assert.Contains(parameter, keys);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Fixture chưa có ví dụ 422 riêng cho route này. 3 giao dịch và giới hạn 2 trả 422, không ghi nhật ký.
    public async Task Export_OverLimit_Returns422WithoutAuditLog()
    {
        await AddTransactionAsync("l1", "unmatched", BaseTime.AddMinutes(1));
        await AddTransactionAsync("l2", "unmatched", BaseTime.AddMinutes(2));
        await AddTransactionAsync("l3", "unmatched", BaseTime.AddMinutes(3));
        var (_, cookie) = await ExporterAsync();
        using var limitedFactory = FactoryWithMaxRows(2);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, limitedClient);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Đúng bằng giới hạn thì vẫn trả file. Chỉ khi vượt giới hạn một giao dịch mới bị từ chối.
    public async Task Export_ExactlyAtLimit_ReturnsFile()
    {
        await AddTransactionAsync("e1", "unmatched", BaseTime.AddMinutes(1));
        await AddTransactionAsync("e2", "unmatched", BaseTime.AddMinutes(2));
        await AddTransactionAsync("e3", "unmatched", BaseTime.AddMinutes(3));
        var (_, cookie) = await ExporterAsync();
        using var limitedFactory = FactoryWithMaxRows(3);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, limitedClient);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(3, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Xuất thành công ghi đúng 1 dòng nhật ký: người xuất, tên file, bộ lọc có giá trị và số giao dịch.
    public async Task Export_Success_WritesExactlyOneAuditLog()
    {
        await SeedFilterDatasetAsync();
        var (staff, cookie) = await ExporterAsync();

        using var response = await ExportAsync("matchStatus=matched&page=2&limit=1", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var log = Assert.Single(await ExportAuditLogsAsync());
        Assert.Equal(staff.Id, log.ActorId);
        Assert.Equal("export", log.TargetType);
        Assert.Equal(FileNameOf(response), log.TargetId);
        // Cột `before` không mang dữ liệu. `AuditRecorder` luôn lưu JSON `null`, không lưu SQL `NULL`.
        Assert.NotNull(log.Before);
        Assert.Equal(JsonValueKind.Null, log.Before.RootElement.ValueKind);
        var after = log.After!.RootElement;
        Assert.Equal(3, after.GetProperty("rowCount").GetInt32());
        Assert.Equal(DataRowCount(workbook.Worksheet(SheetName)), after.GetProperty("rowCount").GetInt32());
        // `filters` chỉ có tham số lọc có giá trị. `page` và `limit` không có mặt.
        var filters = after.GetProperty("filters");
        Assert.Equal(["matchStatus"], filters.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("matched", filters.GetProperty("matchStatus").GetString());
    }

    [Fact]
    // Cả hai tham số lọc đều đi vào `filters` với đúng tên tham số của query.
    public async Task Export_BothFilters_AreRecordedByQueryParameterName()
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("matchStatus=matched&orderFlag=underpaid", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(1, DataRowCount(workbook.Worksheet(SheetName)));
        var log = Assert.Single(await ExportAuditLogsAsync());
        var filters = log.After!.RootElement.GetProperty("filters");
        var actual = filters.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString());
        Assert.Equal(
            new Dictionary<string, string?> { ["matchStatus"] = "matched", ["orderFlag"] = "underpaid" },
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
    // Ghi nhật ký lỗi thì không trả file. Nhân viên không nhận file xuất mà không có dấu vết.
    public async Task Export_WhenAuditWriteFails_ReturnsNoFile()
    {
        await AddTransactionAsync("tx-fail", "unmatched", BaseTime);
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
    // Dựng file lỗi thì không ghi nhật ký và không trả file.
    // Ô chữ trên 32.767 ký tự ép lỗi, và webhook SePay không tạo được đầu vào này.
    public async Task Export_WhenWorkbookCannotBeBuilt_WritesNoAuditLogAndReturnsNoFile()
    {
        await AddTransactionAsync("tx-huge", "unmatched", BaseTime, content: new string('A', 40_000));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Content.Headers.Contains("Content-Disposition"));
        Assert.Empty(await ExportAuditLogsAsync());
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
