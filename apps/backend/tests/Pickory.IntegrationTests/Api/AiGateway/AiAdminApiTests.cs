using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Entities;
using Pickory.Modules.AiGateway.Settings;
using Xunit;

namespace Pickory.IntegrationTests.Api.AiGateway;

// Port từ `ai-admin.int.test.ts` của bản Node. Route quản trị AI dùng ma trận quyền của identity.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AiAdminApiTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AiAdminApiTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    private async Task ResetAiTablesAsync()
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        await aiGateway.UsageLogs.ExecuteDeleteAsync();
        await aiGateway.Settings.ExecuteDeleteAsync();
        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == "ai_setting").ExecuteDeleteAsync();
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? session)
    {
        return _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, session is null ? null : $"pk_session={session}"));
    }

    private Task<HttpResponseMessage> PutSettingsAsync(object body, string session)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, "/api/admin/ai/settings", body, $"pk_session={session}"));
    }

    [Fact]
    // Nhân viên sales xem được chi phí và cấu hình, nhưng không sửa được cấu hình.
    public async Task Sales_CanReadButCannotUpdate()
    {
        await ResetAiTablesAsync();
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");

        var usage = await GetAsync("/api/admin/ai/usage", salesSession);
        var settings = await GetAsync("/api/admin/ai/settings", salesSession);
        var update = await PutSettingsAsync(new { monthlyBudgetUsd = 50, chatDailyLimitUser = 100, chatDailyLimitAnon = 30 }, salesSession);

        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    // Khách chưa đăng nhập không xem được báo cáo chi phí AI.
    public async Task Guest_CannotReadUsage()
    {
        var response = await GetAsync("/api/admin/ai/usage", session: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Admin sửa ngân sách. Lời gọi gateway kế tiếp dùng ngay ngân sách mới, và có đúng 1 dòng audit.
    public async Task Admin_UpdateTakesEffectImmediatelyAndIsAudited()
    {
        await ResetAiTablesAsync();
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        await using (var aiGateway = _database.CreateAiGatewayContext())
        {
            aiGateway.Settings.Add(new AiSetting { Id = AiSettingsStore.SingletonId, MonthlyBudgetUsd = 1, ChatDailyLimitUser = 50, ChatDailyLimitAnon = 15, UpdatedAt = DateTime.UtcNow });
            aiGateway.UsageLogs.Add(new AiUsageLog { Feature = "shopping-assistant.chat", Provider = "fake", Model = "fake", LatencyMs = 50, Status = "ok", CostUsd = 10, CreatedAt = DateTime.UtcNow });
            await aiGateway.SaveChangesAsync();
        }

        var gateway = _factory.Services.GetRequiredService<IAiGateway>();
        var chatContext = new CallContext(AiFeature.ShoppingAssistantChat, $"user:{admin.Id}");
        var chatRequest = new ChatRequest { Messages = [new UserChatMessage("Xin chào")] };
        await Assert.ThrowsAsync<AiBudgetExceededException>(() => gateway.ChatAsync(chatRequest, chatContext));

        var response = await PutSettingsAsync(new { monthlyBudgetUsd = 100, chatDailyLimitUser = 80, chatDailyLimitAnon = 20 }, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(100, data.GetProperty("monthlyBudgetUsd").GetDouble());
            Assert.Equal(80, data.GetProperty("chatDailyLimitUser").GetInt32());
            Assert.Equal(20, data.GetProperty("chatDailyLimitAnon").GetInt32());
        }

        var result = await gateway.ChatAsync(chatRequest with { Messages = [new UserChatMessage("Xin chào lần hai")] }, chatContext);
        Assert.False(string.IsNullOrEmpty(result.Text));
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking().SingleAsync(log => log.TargetType == "ai_setting");
        Assert.Equal((admin.Id, "ai_settings.update", AiSettingsStore.SingletonId), (audit.ActorId, audit.Action, audit.TargetId));
        Assert.Equal(100, audit.After!.RootElement.GetProperty("monthlyBudgetUsd").GetDouble());
    }

    [Fact]
    // Body sai kiểu, sai giá trị, thiếu trường hoặc có trường lạ đều trả 400. Cấu hình không đổi.
    public async Task Admin_InvalidBodiesReturn400()
    {
        await ResetAiTablesAsync();
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        object[] invalidBodies =
        [
            new { monthlyBudgetUsd = -10, chatDailyLimitUser = 50, chatDailyLimitAnon = 15 },
            new { monthlyBudgetUsd = 30, chatDailyLimitUser = 0, chatDailyLimitAnon = 15 },
            new { monthlyBudgetUsd = 30, chatDailyLimitUser = 25.5, chatDailyLimitAnon = 15 },
            new { monthlyBudgetUsd = 30, chatDailyLimitUser = 50, chatDailyLimitAnon = 15, extraUnauthorizedField = "malicious" },
            new { monthlyBudgetUsd = 30 },
            new { monthlyBudgetUsd = "30", chatDailyLimitUser = 50, chatDailyLimitAnon = 15 },
        ];

        foreach (var body in invalidBodies)
        {
            var response = await PutSettingsAsync(body, adminSession);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = await ActorHttp.ReadJsonAsync(response);
            Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        await using var aiGateway = _database.CreateAiGatewayContext();
        Assert.False(await aiGateway.Settings.AnyAsync());
        await using var identity = _database.CreateIdentityContext();
        Assert.False(await identity.AuditLogs.AnyAsync(log => log.TargetType == "ai_setting"));
    }

    [Fact]
    // Ngày sai định dạng hoặc ngược thứ tự trả 400. Khoảng 92 ngày hợp lệ, dài hơn 1 ms thì bị từ chối.
    public async Task Usage_ValidatesDateRange()
    {
        await ResetAiTablesAsync();
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var invalidIso = await GetAsync("/api/admin/ai/usage?from=khong-phai-ngay&to=2026-10-05T00:00:00.000Z", adminSession);
        var inverted = await GetAsync("/api/admin/ai/usage?from=2026-10-10T00:00:00.000Z&to=2026-10-05T00:00:00.000Z", adminSession);
        var quarter = await GetAsync("/api/admin/ai/usage?from=2026-07-01T00:00:00.000Z&to=2026-10-01T00:00:00.000Z", adminSession);
        var tooWide = await GetAsync("/api/admin/ai/usage?from=2026-07-01T00:00:00.000Z&to=2026-10-01T00:00:00.001Z", adminSession);

        Assert.Equal(HttpStatusCode.BadRequest, invalidIso.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quarter.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(tooWide);
        Assert.Equal("INVALID_DATE_RANGE", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Báo cáo trả đủ các trường mà trang `/admin/ai` đọc.
    public async Task Usage_ReturnsSummaryShape()
    {
        await ResetAiTablesAsync();
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        await using (var aiGateway = _database.CreateAiGatewayContext())
        {
            aiGateway.UsageLogs.Add(new AiUsageLog { Feature = "smart-search.index", Provider = "fake", Model = "fake", LatencyMs = 40, Status = "ok", CostUsd = 0.5, CreatedAt = DateTime.UtcNow.AddMinutes(-5) });
            await aiGateway.SaveChangesAsync();
        }

        var response = await GetAsync("/api/admin/ai/usage", adminSession);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(0.5, data.GetProperty("costUsd").GetDouble());
        Assert.Equal(1, data.GetProperty("calls").GetInt32());
        Assert.Equal(0, data.GetProperty("errorRate").GetDouble());
        Assert.Equal(40, data.GetProperty("p95LatencyMs").GetInt32());
        Assert.Equal("smart-search.index", data.GetProperty("byFeature")[0].GetProperty("feature").GetString());
        Assert.Equal(1, data.GetProperty("byDay").GetArrayLength());
        Assert.False(data.GetProperty("budgetWarning").GetBoolean());
    }
}
