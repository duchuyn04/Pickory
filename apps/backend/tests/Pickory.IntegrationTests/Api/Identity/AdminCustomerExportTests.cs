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
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Xuất khách ra Excel. Route dùng chung bộ lọc với danh sách, ghi nhật ký và chặn file quá lớn.
[Collection(IdentityCollection.Name)]
public sealed class AdminCustomerExportTests : IClassFixture<IdentityWebApplicationFactory>, IAsyncLifetime
{
    private const string ExportsFixture = "exports-printing/exports.json";
    private const string ExportPath = "/api/admin/exports/customers";
    private const string CustomersPath = "/api/admin/customers";
    private const string ExportAuditAction = "identity.customers.export";
    private const string SheetName = "Khách hàng";

    // Mốc cố định ứng với 14:20 ngày 15/09/2026 giờ Việt Nam, khớp `sampleRow` của ví dụ `customers export success`.
    private static readonly DateTime BaseTime = new(2026, 9, 15, 7, 20, 0, DateTimeKind.Utc);

    private readonly IdentityWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AdminCustomerExportTests(IdentityWebApplicationFactory factory, IdentityDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Xóa mọi khách trước test. Số dòng của file và `total` của danh sách đếm cả bảng, nên khách của test khác làm lệch tổng.
        await DeleteCustomersAsync();
        await DeleteExportAuditLogsAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await DeleteCustomersAsync();
        await DeleteExportAuditLogsAsync();
    }

    // Phiên của nhân viên giữ đúng các quyền cho trước. Role riêng cho từng lần gọi nên test không phụ thuộc role mặc định.
    private async Task<(User Staff, string Cookie)> StaffWithPermissionsAsync(params string[] permissions)
    {
        var role = await IdentityTestData.CreateRoleAsync(_database, permissions);
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(_database, role.Code, "export");
        return (staff, $"pk_session={token}");
    }

    private async Task<(User Staff, string Cookie)> ExporterAsync() =>
        await StaffWithPermissionsAsync(Permissions.CustomersRead, Permissions.ReportsExport);

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

    private static List<string> EmailsOf(IXLWorksheet sheet) =>
        Enumerable.Range(1, DataRowCount(sheet)).Select(row => TextOf(sheet, row, "Email")!).ToList();

    // Tên file thật trong header, để so với `TargetId` của nhật ký.
    private static string FileNameOf(HttpResponseMessage response)
    {
        var header = string.Join(", ", response.Content.Headers.GetValues("Content-Disposition"));
        return Regex.Match(header, "filename=\"(?<name>[^\"]+)\"").Groups["name"].Value;
    }

    private async Task<int> ListTotalAsync(string query, string cookie)
    {
        var path = query.Length == 0 ? CustomersPath : $"{CustomersPath}?{query}";
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

    private async Task DeleteCustomersAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        await identity.Users.Where(user => user.Kind == UserKind.Customer).ExecuteDeleteAsync();
    }

    // Thêm một khách thẳng vào DB với `CreatedAt` và `Id` do test chọn. Mặc định khách hoạt động, đã có tên và số điện thoại.
    private async Task AddCustomerAsync(
        string email,
        string? name = "Khách thử",
        string? phone = "0901234567",
        bool emailVerified = true,
        DateTime? lockedAt = null,
        DateTime? deletedAt = null,
        DateTime? createdAt = null,
        string? id = null)
    {
        await using var identity = _database.CreateIdentityContext();
        identity.Users.Add(new User
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Email = email,
            Name = name,
            Phone = phone,
            Kind = UserKind.Customer,
            EmailVerified = emailVerified,
            LockedAt = lockedAt,
            DeletedAt = deletedAt,
            CreatedAt = createdAt ?? BaseTime,
            UpdatedAt = BaseTime,
        });
        await identity.SaveChangesAsync();
    }

    // Sáu khách có trạng thái khóa, xác minh email và tên khác nhau để mỗi bộ lọc chọn một tập con riêng.
    private async Task SeedFilterDatasetAsync()
    {
        await AddCustomerAsync("c1@export.test", "Nguyễn An", createdAt: BaseTime.AddMinutes(1));
        await AddCustomerAsync("minh-c2@export.test", emailVerified: false, createdAt: BaseTime.AddMinutes(2));
        await AddCustomerAsync("c3@export.test", lockedAt: BaseTime.AddDays(1), createdAt: BaseTime.AddMinutes(3));
        await AddCustomerAsync("c4@export.test", "Phạm Minh Thư", emailVerified: false, lockedAt: BaseTime.AddDays(1), createdAt: BaseTime.AddMinutes(4));
        await AddCustomerAsync("c5@export.test", "Lê Minh", createdAt: BaseTime.AddMinutes(5));
        await AddCustomerAsync("c6@export.test", name: null, phone: null, emailVerified: false, createdAt: BaseTime.AddMinutes(6));
    }

    private WebApplicationFactory<Program> FactoryWithMaxRows(int maxRows) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting("Exports:MaxRows", maxRows.ToString(CultureInfo.InvariantCulture)));

    [Fact]
    // Contract: file thành công khớp ví dụ `customers export success` về header, tên sheet, tiêu đề và kiểu ô dòng 2.
    public async Task Export_Success_MatchesContractWorkbook()
    {
        // Khách hoạt động có tên, số điện thoại và email đã xác minh như `sampleRow`, nên dòng 2 có đủ kiểu chữ và ngày giờ.
        await AddCustomerAsync("minhthu@example.com", "Trần Thị Minh Thư", "0901234567");
        // Khách bị khóa không được vào file của bộ lọc `status=active`.
        await AddCustomerAsync("locked@example.com", lockedAt: BaseTime.AddDays(1), createdAt: BaseTime.AddMinutes(-5));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=active", cookie);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        WorkbookFixture.AssertMatches(ExportsFixture, "customers export success", bytes, response);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(1, DataRowCount(sheet));
        // Giá trị ô khớp `sampleRow`. Ô ngày khóa trống, ngày đăng ký là 14:20 giờ Việt Nam.
        Assert.Equal("Trần Thị Minh Thư", TextOf(sheet, 1, "Họ tên"));
        Assert.Equal("minhthu@example.com", TextOf(sheet, 1, "Email"));
        Assert.Equal("0901234567", TextOf(sheet, 1, "Số điện thoại"));
        Assert.Equal("Có", TextOf(sheet, 1, "Đã xác minh email"));
        Assert.Equal("Hoạt động", TextOf(sheet, 1, "Trạng thái"));
        Assert.Equal(new DateTime(2026, 9, 15, 14, 20, 0), CellOf(sheet, 1, "Ngày đăng ký").GetDateTime());
    }

    [Fact]
    // Contract: `status=deleted` trả 400 `VALIDATION_ERROR` khớp ví dụ `customers export invalid status`, và không ghi nhật ký.
    public async Task Export_InvalidStatus_MatchesContractAndWritesNoAuditLog()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=deleted", cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "customers export invalid status", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Thiếu `reports:export` trả 403, không ghi nhật ký.
    // Dùng ví dụ lỗi của route đơn hàng, vì fixture chưa có ví dụ 403 riêng cho route này.
    public async Task Export_MissingReportsExport_Returns403()
    {
        var (_, cookie) = await StaffWithPermissionsAsync(Permissions.CustomersRead);

        using var response = await ExportAsync(string.Empty, cookie);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export missing reports export permission", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Có `reports:export` nhưng thiếu `customers:read` cũng trả 403, không ghi nhật ký.
    // Dùng ví dụ lỗi của route đơn hàng, vì fixture chưa có ví dụ 403 riêng cho route này.
    public async Task Export_MissingCustomersRead_Returns403()
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
    // File chứa email và số điện thoại của khách, nên không được lưu bản sao ở trình duyệt hay proxy.
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

        using var response = await ExportAsync("status=deleted", cookie);

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

        Assert.Equal("khach-hang-20261110-1530.xlsx", FileNameOf(response));
    }

    [Theory]
    [InlineData("", 6)]
    [InlineData("status=active", 4)]
    [InlineData("status=locked", 2)]
    [InlineData("emailVerified=true", 3)]
    [InlineData("emailVerified=false", 3)]
    [InlineData("search=minh", 3)]
    [InlineData("status=active&emailVerified=true", 2)]
    [InlineData("status=active&emailVerified=true&search=minh", 1)]
    // Số dòng của sheet bằng `total` của danh sách cùng bộ lọc, nên hai route cùng thấy một tập khách.
    public async Task Export_RowCount_EqualsListTotal(string query, int expectedCount)
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var listTotal = await ListTotalAsync(query, cookie);
        Assert.Equal(expectedCount, listTotal);
        Assert.Equal(listTotal, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Khách bị khóa có nhãn "Đã khóa" và ngày khóa đúng giờ Việt Nam. Ngày khóa là ô ngày giờ.
    public async Task Export_LockedCustomer_HasLockedLabelAndLockedAtCell()
    {
        // 03:00 giờ quốc tế ngày 02/10/2026 là 10:00 cùng ngày ở Việt Nam.
        await AddCustomerAsync("locked@export.test", lockedAt: new DateTime(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("status=locked", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(1, DataRowCount(sheet));
        Assert.Equal("Đã khóa", TextOf(sheet, 1, "Trạng thái"));
        var lockedAt = CellOf(sheet, 1, "Ngày khóa");
        Assert.Equal(XLDataType.DateTime, lockedAt.DataType);
        Assert.Equal(new DateTime(2026, 10, 2, 10, 0, 0), lockedAt.GetDateTime());
    }

    [Fact]
    // Khách hoạt động có nhãn "Hoạt động" và ô ngày khóa trống. Ô trống khác chuỗi rỗng.
    public async Task Export_ActiveCustomer_LeavesLockedAtCellBlank()
    {
        await AddCustomerAsync("active@export.test");
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal("Hoạt động", TextOf(sheet, 1, "Trạng thái"));
        var lockedAt = CellOf(sheet, 1, "Ngày khóa");
        Assert.True(lockedAt.IsEmpty());
        Assert.Equal(XLDataType.Blank, lockedAt.DataType);
        Assert.Equal(string.Empty, lockedAt.GetString());
    }

    [Fact]
    // Khách chưa xác minh email có nhãn "Không", khách đã xác minh có nhãn "Có".
    public async Task Export_EmailVerified_IsWrittenAsYesOrNo()
    {
        await AddCustomerAsync("verified@export.test", emailVerified: true, createdAt: BaseTime.AddMinutes(2));
        await AddCustomerAsync("unverified@export.test", emailVerified: false, createdAt: BaseTime.AddMinutes(1));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal("Có", TextOf(sheet, 1, "Đã xác minh email"));
        Assert.Equal("Không", TextOf(sheet, 2, "Đã xác minh email"));
    }

    [Fact]
    // File không có tài khoản nhân viên. Email của nhân viên khớp từ khóa tìm vẫn không vào file.
    public async Task Export_NeverContainsStaffAccounts()
    {
        await AddCustomerAsync("customer@staffcheck.test");
        await IdentityTestData.CreateUserAsync(
            _database, "staff@staffcheck.test", UserKind.Staff, "sales", emailVerified: true);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("search=staffcheck", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(["customer@staffcheck.test"], EmailsOf(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Khách đã tự xóa tài khoản vẫn nằm trong danh sách và trong file với nhãn "Hoạt động".
    // Bộ lọc không loại `DeletedAt`, nên test này ghim hành vi hiện có của cả hai route.
    public async Task Export_DeletedCustomer_IsStillListedAsActive()
    {
        // Tài khoản đã xóa được ẩn danh như `DeleteAccountHandler`: email đổi, tên và số điện thoại bị xóa, `LockedAt` vẫn trống.
        await AddCustomerAsync(
            "deleted-abc@deleted.pickory.local", name: null, phone: null, emailVerified: false, deletedAt: BaseTime.AddDays(1));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(1, await ListTotalAsync(string.Empty, cookie));
        Assert.Equal(1, DataRowCount(sheet));
        Assert.Equal("deleted-abc@deleted.pickory.local", TextOf(sheet, 1, "Email"));
        Assert.Equal("Hoạt động", TextOf(sheet, 1, "Trạng thái"));
    }

    [Fact]
    // Khách chưa nhập họ tên hoặc số điện thoại để ô trống, không ghi chuỗi rỗng.
    public async Task Export_CustomerWithoutNameOrPhone_LeavesCellsBlank()
    {
        await AddCustomerAsync("anon@export.test", name: null, phone: null);
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        foreach (var header in new[] { "Họ tên", "Số điện thoại" })
        {
            var cell = CellOf(sheet, 1, header);
            Assert.True(cell.IsEmpty(), $"Ô \"{header}\" phải trống.");
            Assert.Equal(XLDataType.Blank, cell.DataType);
        }

        Assert.Equal("anon@export.test", TextOf(sheet, 1, "Email"));
    }

    [Fact]
    // Họ tên bắt đầu bằng `=` là ô chữ, không phải công thức Excel.
    public async Task Export_NameStartingWithEquals_IsTextNotFormula()
    {
        await AddCustomerAsync("formula@export.test", name: "=1+1");
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var cell = CellOf(workbook.Worksheet(SheetName), 1, "Họ tên");
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.False(cell.HasFormula);
        Assert.Equal("=1+1", cell.GetString());
    }

    [Fact]
    // File chỉ có đúng 7 cột. Test chỉ đếm số cột, không kiểm nội dung từng cột.
    public async Task Export_HasExactlySevenColumns()
    {
        await AddCustomerAsync("columns@export.test");
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(7, workbook.Worksheet(SheetName).LastColumnUsed()!.ColumnNumber());
    }

    [Fact]
    // Khách mới đăng ký lên đầu, giống danh sách. Hai khách cùng `CreatedAt` xếp theo `Id`.
    public async Task Export_IsNewestFirstThenById()
    {
        await AddCustomerAsync("old@export.test", createdAt: BaseTime.AddHours(-5));
        await AddCustomerAsync("tie-b@export.test", createdAt: BaseTime, id: "export-tie-b");
        await AddCustomerAsync("tie-a@export.test", createdAt: BaseTime, id: "export-tie-a");
        await AddCustomerAsync("new@export.test", createdAt: BaseTime.AddHours(5));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(
            ["new@export.test", "tie-a@export.test", "tie-b@export.test", "old@export.test"],
            EmailsOf(workbook.Worksheet(SheetName)));
    }

    [Theory]
    [InlineData("page=2&limit=1")]
    [InlineData("page=abc&limit=0")]
    [InlineData("limit=abc")]
    [InlineData("limit=1000")]
    // Tên tham số phân trang không phân biệt hoa thường, nên `PAGE` và `Limit` cũng bị bỏ qua.
    [InlineData("PAGE=abc&Limit=abc")]
    // `page` và `limit` bị bỏ qua, kể cả giá trị mà danh sách coi là sai. File luôn chứa mọi khách khớp bộ lọc.
    public async Task Export_IgnoresPageAndLimit(string query)
    {
        await SeedFilterDatasetAsync();
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(query, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(6, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Không có khách nào vẫn trả file hợp lệ chỉ có dòng tiêu đề, và vẫn ghi nhật ký với `rowCount` bằng 0.
    public async Task Export_NoCustomers_ReturnsHeaderOnlyFileAndAuditLog()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var sheet = workbook.Worksheet(SheetName);
        Assert.Equal(0, DataRowCount(sheet));
        Assert.Equal("Họ tên", sheet.Cell(1, 1).GetString());
        var log = Assert.Single(await ExportAuditLogsAsync());
        Assert.Equal(0, log.After!.RootElement.GetProperty("rowCount").GetInt32());
    }

    [Theory]
    [InlineData("status=deleted", "status")]
    [InlineData("emailVerified=maybe", "emailVerified")]
    [InlineData("status=active&status=locked", "status")]
    [InlineData("emailVerified=true&emailVerified=false", "emailVerified")]
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
    // Từ khóa dài quá 100 ký tự trả 400 cho `search`. Lỗi này đến từ `SearchTerm`, dùng chung với danh sách.
    public async Task Export_TooLongSearch_Returns400AndWritesNoAuditLog()
    {
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync("search=" + new string('a', 101), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Fixture chưa có ví dụ 422 riêng cho route này. 3 khách và giới hạn 2 trả 422, không ghi nhật ký.
    public async Task Export_OverLimit_Returns422WithoutAuditLog()
    {
        await AddCustomerAsync("l1@export.test", createdAt: BaseTime.AddMinutes(1));
        await AddCustomerAsync("l2@export.test", createdAt: BaseTime.AddMinutes(2));
        await AddCustomerAsync("l3@export.test", createdAt: BaseTime.AddMinutes(3));
        var (_, cookie) = await ExporterAsync();
        using var limitedFactory = FactoryWithMaxRows(2);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, limitedClient);

        await ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response);
        Assert.Empty(await ExportAuditLogsAsync());
    }

    [Fact]
    // Đúng bằng giới hạn thì vẫn trả file. Chỉ khi vượt giới hạn một khách mới bị từ chối.
    public async Task Export_ExactlyAtLimit_ReturnsFile()
    {
        await AddCustomerAsync("e1@export.test", createdAt: BaseTime.AddMinutes(1));
        await AddCustomerAsync("e2@export.test", createdAt: BaseTime.AddMinutes(2));
        await AddCustomerAsync("e3@export.test", createdAt: BaseTime.AddMinutes(3));
        var (_, cookie) = await ExporterAsync();
        using var limitedFactory = FactoryWithMaxRows(3);
        using var limitedClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await ExportAsync(string.Empty, cookie, limitedClient);
        using var workbook = await ReadWorkbookAsync(response);

        Assert.Equal(3, DataRowCount(workbook.Worksheet(SheetName)));
    }

    [Fact]
    // Xuất thành công ghi đúng 1 dòng nhật ký: người xuất, tên file, bộ lọc có giá trị và số khách.
    public async Task Export_Success_WritesExactlyOneAuditLog()
    {
        await SeedFilterDatasetAsync();
        var (staff, cookie) = await ExporterAsync();

        // Từ khóa có khoảng trắng thừa. Nhật ký ghi chữ đã bỏ khoảng trắng thừa, như `SearchTerm` đọc.
        using var response = await ExportAsync("search=%20%20minh%20%20thu&status=locked&emailVerified=false&page=2&limit=1", cookie);
        using var workbook = await ReadWorkbookAsync(response);

        var log = Assert.Single(await ExportAuditLogsAsync());
        Assert.Equal(staff.Id, log.ActorId);
        Assert.Equal("export", log.TargetType);
        Assert.Equal(FileNameOf(response), log.TargetId);
        // Cột `before` không mang dữ liệu. `AuditRecorder` luôn lưu JSON `null`, không lưu SQL `NULL`.
        Assert.NotNull(log.Before);
        Assert.Equal(JsonValueKind.Null, log.Before.RootElement.ValueKind);
        var after = log.After!.RootElement;
        Assert.Equal(1, after.GetProperty("rowCount").GetInt32());
        Assert.Equal(DataRowCount(workbook.Worksheet(SheetName)), after.GetProperty("rowCount").GetInt32());
        // `filters` chỉ có tham số lọc có giá trị, khóa là tên tham số. `page` và `limit` không có mặt.
        var actual = after.GetProperty("filters").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString());
        Assert.Equal(
            new Dictionary<string, string?> { ["search"] = "minh thu", ["status"] = "locked", ["emailVerified"] = "false" },
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
    // Dựng file lỗi thì không ghi nhật ký và không trả file.
    // Ô chữ trên 32.767 ký tự ép lỗi, và đăng ký tài khoản không tạo được họ tên dài như vậy.
    public async Task Export_WhenWorkbookCannotBeBuilt_WritesNoAuditLogAndReturnsNoFile()
    {
        await AddCustomerAsync("huge@export.test", name: new string('A', 40_000));
        var (_, cookie) = await ExporterAsync();

        using var response = await ExportAsync(string.Empty, cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Content.Headers.Contains("Content-Disposition"));
        Assert.Empty(await ExportAuditLogsAsync());
    }
}
