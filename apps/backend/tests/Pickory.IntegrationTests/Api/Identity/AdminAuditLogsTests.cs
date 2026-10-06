using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Audit;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// `GET /api/admin/audit-logs` cho người có quyền xem nhật ký thao tác. Route chỉ đọc, không có route sửa hoặc xóa.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminAuditLogsTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AdminAuditLogsTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    // Lọc theo người thực hiện, hành động và đối tượng. Dòng mới nhất lên đầu và có email, tên của người thực hiện.
    public async Task AuditLogs_FilterNewestFirstWithActorDetails()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var targetType = $"type-{Guid.NewGuid():N}";
        await AddAuditAsync(admin.Id, "role.create", targetType, DateTime.UtcNow.AddMinutes(-2));
        await AddAuditAsync(admin.Id, "role.update", targetType, DateTime.UtcNow.AddMinutes(-1));
        await AddAuditAsync(admin.Id, "role.update", targetType, DateTime.UtcNow);

        var response = await SendAsync($"/api/admin/audit-logs?targetType={targetType}&action=role.update&actorId={admin.Id}&limit=1", adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        // `total` đếm cả dòng ngoài trang hiện tại. Giao diện tính được số trang.
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("limit").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("role.update", item.GetProperty("action").GetString());
        Assert.Equal(admin.Email, item.GetProperty("actorEmail").GetString());
        Assert.Equal(admin.Name, item.GetProperty("actorName").GetString());
        // Trạng thái trước và sau giữ nguyên dạng JSON đã lưu.
        Assert.Equal(JsonValueKind.Null, item.GetProperty("before").ValueKind);
        Assert.Equal("value", item.GetProperty("after").GetProperty("field").GetString());
        Assert.True(item.GetProperty("createdAt").GetString()!.EndsWith('Z'));
    }

    [Fact]
    // Khoảng thời gian gồm cả hai đầu mút. Dòng ngoài khoảng bị loại.
    public async Task AuditLogs_FilterByInclusiveDateRange()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var targetType = $"type-{Guid.NewGuid():N}";
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await AddAuditAsync(admin.Id, "staff.lock", targetType, start.AddSeconds(-1));
        await AddAuditAsync(admin.Id, "staff.lock", targetType, start);
        await AddAuditAsync(admin.Id, "staff.lock", targetType, start.AddDays(1));

        var response = await SendAsync($"/api/admin/audit-logs?targetType={targetType}&startDate=2026-09-01T00:00:00.000Z&endDate=2026-09-02T00:00:00.000Z", adminSession);

        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(2, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    // Người thực hiện không còn trong DB thì email và tên là null. Giao diện hiển thị phương án dự phòng.
    public async Task AuditLogs_UnknownActorHasNullDetails()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var targetType = $"type-{Guid.NewGuid():N}";
        await AddAuditAsync("actor-da-bi-xoa", "role.delete", targetType, DateTime.UtcNow);

        var response = await SendAsync($"/api/admin/audit-logs?targetType={targetType}", adminSession);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var item = Assert.Single(document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("actorEmail").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("actorName").ValueKind);
    }

    [Fact]
    // Thời gian lọc phải là chuỗi ISO có đuôi `Z`, giống `z.string().datetime()` của bản Node.
    public async Task AuditLogs_DateWithoutUtcSuffixIsRejected()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync("/api/admin/audit-logs?startDate=2026-09-01", adminSession);

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (startDate: Thời gian phải theo định dạng ISO 8601 có đuôi Z.).");
    }

    [Fact]
    // Nhân viên bán hàng không có `audit-logs:read`, nên không xem được nhật ký thao tác.
    public async Task AuditLogs_RequireAuditLogsRead()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");

        var response = await SendAsync("/api/admin/audit-logs", salesSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có đủ quyền thực hiện thao tác này.");
    }

    // Ghi nhật ký bằng `AuditRecorder` thật với thời điểm tạo do test đặt.
    private async Task AddAuditAsync(string actorId, string action, string targetType, DateTime createdAt)
    {
        await using var identity = _database.CreateIdentityContext();
        var auditLog = AuditRecorder.Add(identity, actorId, action, targetType, Guid.NewGuid().ToString(), before: null, after: new { field = "value" });
        auditLog.CreatedAt = createdAt;
        await identity.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> SendAsync(string path, string sessionToken)
    {
        return _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, $"pk_session={sessionToken}"));
    }
}
