using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Providers.Fake;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port từ `gateway-core.test.ts` và phần chat của `gateway-regressions.test.ts` ở bản Node.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GatewayCoreTests(IntegrationDatabaseFixture database)
{
    private static readonly ChatRequest Request = new() { Messages = [new UserChatMessage("Tìm áo")] };
    private static readonly CallContext SearchContext = new(AiFeature.SmartSearchParseQuery, "user:regression");
    private static readonly TokenUsage Usage = new(10, 5, 0);

    [Fact]
    // Một lời gọi chat ghi đúng 1 dòng log. Log có hash SHA-256 của actor, không có nội dung prompt.
    public async Task ChatAsync_WritesOneLogWithHashedActor()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        const string actorKey = "user:customer_vip_777";

        var result = await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Xin chào bot bí mật")] },
            new CallContext(AiFeature.ShoppingAssistantChat, actorKey));

        Assert.Equal("Xin chào từ fake AI", result.Text);
        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal("ok", log.Status);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(actorKey)));
        Assert.Equal(expectedHash, log.ActorKeyHash);
    }

    [Fact]
    // Provider chỉ nhận text đã ẩn số điện thoại và email.
    public async Task ChatAsync_RedactsPiiBeforeProvider()
    {
        var harness = await GatewayHarness.CreateAsync(database);

        await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Số điện thoại của tôi là 0912 345 678 và email a@b.vn cần tư vấn.")] },
            new CallContext(AiFeature.ShoppingAssistantChat, "user:client_001"));

        var sentMessage = Assert.Single(Assert.Single(harness.Chat.RecordedRequests).Messages).Content;
        Assert.DoesNotContain("0912 345 678", sentMessage);
        Assert.DoesNotContain("a@b.vn", sentMessage);
        Assert.Contains("[đã ẩn]", sentMessage);
    }

    [Fact]
    // Model chưa có bảng giá bị chặn trước provider. Log ghi lỗi với chi phí 0.
    public async Task ChatAsync_RejectsUnpricedModelBeforeProvider()
    {
        var provider = new FakeChatProvider(model: "unlisted-expensive-model");
        var harness = await GatewayHarness.CreateAsync(database, chatProvider: provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Thử model không có giá")] },
            new CallContext(AiFeature.ShoppingAssistantChat, "user:client_test")));

        Assert.Empty(provider.RecordedRequests);
        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal(("error", "UNKNOWN_MODEL_PRICING", 0d), (log.Status, log.ErrorCode, log.CostUsd));
    }

    [Fact]
    // `maxSteps` không dương, chat thiếu actor hoặc actor sai tiền tố đều bị từ chối trước provider.
    public async Task ChatAsync_ValidatesMaxStepsAndActorKey()
    {
        var harness = await GatewayHarness.CreateAsync(database);

        var maxSteps = await Assert.ThrowsAsync<AppException>(() => harness.Gateway.ChatAsync(
            Request with { MaxSteps = -1 },
            new CallContext(AiFeature.ShoppingAssistantChat, "user:ok_user")));
        var missingActor = await Assert.ThrowsAsync<AppException>(() => harness.Gateway.ChatAsync(
            Request,
            new CallContext(AiFeature.ShoppingAssistantChat)));
        var malformedActor = await Assert.ThrowsAsync<AppException>(() => harness.Gateway.ChatAsync(
            Request,
            new CallContext(AiFeature.SmartSearchParseQuery, "invalid_prefix_key")));

        Assert.Equal(("INVALID_MAX_STEPS", 400), (maxSteps.Code, maxSteps.StatusCode));
        Assert.Equal("INVALID_ACTOR_KEY", missingActor.Code);
        Assert.Equal("INVALID_ACTOR_KEY", malformedActor.Code);
        Assert.Empty(harness.Chat.RecordedRequests);
        Assert.Equal(3, (await harness.ReadLogsAsync()).Count(log => log.Status == "error"));
    }

    [Fact]
    // Provider lỗi thì gateway vẫn ghi 1 dòng log trạng thái lỗi.
    public async Task ChatAsync_ProviderErrorStillWritesLog()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Chat.EnqueueError(new InvalidOperationException("Lỗi mạng"));

        await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.ChatAsync(
            Request,
            new CallContext(AiFeature.ShoppingAssistantChat, "user:client_002")));

        Assert.Equal("error", Assert.Single(await harness.ReadLogsAsync()).Status);
    }

    [Fact]
    // Lỗi HTTP của provider có mã ổn định trong log. Log không chứa nội dung lỗi gốc.
    public async Task ChatAsync_ProviderHttpErrorUsesStableCode()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Chat.EnqueueError(new ProviderException(401));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.ChatAsync(Request, SearchContext));

        Assert.Equal("PROVIDER_HTTP_401", error.ReasonCode);
        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal(("error", "PROVIDER_HTTP_401", 0d), (log.Status, log.ErrorCode, log.CostUsd));
    }

    [Fact]
    // Thiếu provider (production chưa có khóa) thì trả lỗi không khả dụng và ghi 1 log `AI_NOT_CONFIGURED`.
    public async Task ChatAndJson_WithoutProviderLogOutage()
    {
        var harness = await GatewayHarness.CreateAsync(database, withoutProviders: true);

        var chatError = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.ChatAsync(Request, SearchContext));
        var jsonError = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.ChatJsonAsync<JsonObject>(Request, SearchContext));

        Assert.Equal("AI_UNAVAILABLE", chatError.Code);
        Assert.Equal("AI_UNAVAILABLE", jsonError.Code);
        var logs = await harness.ReadLogsAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(("blocked", "AI_NOT_CONFIGURED", 0d), (log.Status, log.ErrorCode, log.CostUsd)));
    }

    [Fact]
    // Mạch đang mở thì không gọi provider. Log ghi trạng thái chặn với mã `CIRCUIT_OPEN`.
    public async Task ChatJsonAsync_OpenCircuitLogsBlockedWithoutCallingProvider()
    {
        var reliability = new ProviderReliability(TimeProvider.System, () => 0);
        for (var failure = 0; failure < 5; failure++)
        {
            reliability.Circuit.Failure("fake", reliability.Circuit.Acquire("fake"));
        }

        var harness = await GatewayHarness.CreateAsync(database, reliability: reliability);

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.ChatJsonAsync<JsonObject>(Request, SearchContext));

        Assert.Equal("AI_UNAVAILABLE", error.Code);
        Assert.Empty(harness.Chat.RecordedRequests);
        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal(("blocked", "CIRCUIT_OPEN", 0d), (log.Status, log.ErrorCode, log.CostUsd));
    }

    [Fact]
    // Chat và JSON dùng chung hạn mức ngày. Admin tăng hạn mức thì lượt mới có hiệu lực ngay.
    public async Task ChatLimit_IsSharedAndReadsSettingsEachCall()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(30, 50, 2));
        harness.Chat.EnqueueResponse(new ChatResult("""{"ok":true}""", Usage));
        var chatContext = new CallContext(AiFeature.ShoppingAssistantChat, "anon:shared-limit");

        var json = await harness.Gateway.ChatJsonAsync<OkResult>(Request, chatContext);
        await harness.Gateway.ChatAsync(Request, chatContext);
        var blocked = await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(Request, chatContext));
        var blockedLog = (await harness.ReadLogsAsync())[^1];
        await harness.SetSettingsAsync(new AiSettings(30, 50, 3));
        var allowed = await harness.Gateway.ChatAsync(Request, chatContext);

        Assert.True(json.Ok);
        Assert.Equal("AI_BUDGET_EXCEEDED", blocked.Code);
        Assert.Equal(("blocked", "RATE_LIMIT_EXCEEDED"), (blockedLog.Status, blockedLog.ErrorCode));
        Assert.Equal("Xin chào từ fake AI", allowed.Text);
        Assert.Equal(3, harness.Chat.RecordedRequests.Count);
    }

    [Fact]
    // Sự cố provider hoàn lại lượt chat. Lượt đã thành công không được hoàn lại.
    public async Task ChatLimit_RefundsProviderFailureOnly()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(30, 50, 2));
        var chatContext = new CallContext(AiFeature.ShoppingAssistantChat, "anon:refund");

        await harness.Gateway.ChatAsync(Request, chatContext);
        harness.Chat.EnqueueError(new ProviderException(401));
        await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.ChatAsync(Request, chatContext));
        var afterRefund = await harness.Gateway.ChatAsync(Request, chatContext);

        Assert.Equal("Xin chào từ fake AI", afterRefund.Text);
        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(Request, chatContext));
        Assert.Equal(3, harness.Chat.RecordedRequests.Count);
    }

    [Fact]
    // Ngân sách bằng 0 chặn lượt chat mà không lấy mất lượt. Admin bật lại thì khách vẫn còn lượt.
    public async Task ChatLimit_BudgetBlockDoesNotConsumeTurn()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(0, 50, 1));
        var chatContext = new CallContext(AiFeature.ShoppingAssistantChat, "anon:budget-refund");

        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(Request, chatContext));
        await harness.SetSettingsAsync(new AiSettings(30, 50, 1));
        var allowed = await harness.Gateway.ChatAsync(Request, chatContext);

        Assert.Equal("Xin chào từ fake AI", allowed.Text);
        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(Request, chatContext));
    }

    [Fact]
    // Lượt lỗi bắt đầu trước nửa đêm giờ Việt Nam không hoàn vào bộ đếm của ngày mới.
    public async Task ChatLimit_PreviousDayFailureDoesNotRefundNewDay()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 16, 59, 59, TimeSpan.Zero));
        var provider = new FakeChatProvider();
        var pending = new TaskCompletionSource<ChatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.EnqueueHandler((_, _) => pending.Task);
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(30, 50, 1), chatProvider: provider, timeProvider: time);
        var chatContext = new CallContext(AiFeature.ShoppingAssistantChat, "anon:midnight-refund");

        var oldCall = harness.Gateway.ChatAsync(Request, chatContext);
        await Eventually.UntilAsync(() => provider.RecordedRequests.Count == 1);
        time.Advance(TimeSpan.FromSeconds(1));
        await harness.Gateway.ChatAsync(Request, chatContext);
        pending.SetException(new ProviderException(401));
        await Assert.ThrowsAsync<AiUnavailableException>(() => oldCall);

        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(Request, chatContext));
        Assert.Equal(2, provider.RecordedRequests.Count);
    }

    [Fact]
    // Lịch sử tool giữ JSON hợp lệ khi ẩn số điện thoại và số dài. Tool vẫn nhận input gốc.
    public async Task ToolHistory_StaysValidJsonAfterRedaction()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Chat.EnqueueResponse(new ChatResult(string.Empty, Usage,
        [
            new ToolCall("lookup1", "lookup", JsonNode.Parse("""{"phone":84912345678,"orderId":123456789012,"price":25990000}""")),
        ]));
        harness.Chat.EnqueueResponse(new ChatResult("Đã tìm thấy đơn", Usage));
        long? receivedOrderId = null;
        var lookup = new ToolDefinition<LookupInput>("lookup", "Tra cứu đơn", (input, _) =>
        {
            receivedOrderId = input.OrderId;
            return Task.FromResult<object?>(new { phone = 84912345678, account = 123456789012 });
        });

        var result = await harness.Gateway.ChatAsync(Request with { MaxSteps = 2, Tools = [lookup] }, SearchContext);

        Assert.Equal("Đã tìm thấy đơn", result.Text);
        Assert.Equal(123456789012, receivedOrderId);
        var history = harness.Chat.RecordedRequests[1].Messages;
        var assistant = Assert.IsType<AssistantChatMessage>(history[1]);
        Assert.Equal("""{"phone":"[đã ẩn]","orderId":"[đã ẩn]","price":25990000}""", ToJson(assistant.ToolCalls![0].Input));
        var tool = Assert.IsType<ToolChatMessage>(history[2]);
        Assert.Equal(("lookup1", "lookup"), (tool.ToolCallId, tool.ToolName));
        Assert.Equal("""{"phone":"[đã ẩn]","account":"[đã ẩn]"}""", tool.Content);

        harness.Chat.EnqueueResponse(new ChatResult("""{"ok":true}""", Usage));
        var json = await harness.Gateway.ChatJsonAsync<OkResult>(
            new ChatRequest { Messages = [new AssistantChatMessage(string.Empty, [new ToolCall("old", "lookup", null)])] },
            SearchContext);
        Assert.True(json.Ok);
        Assert.Equal(["ok", "ok", "ok"], (await harness.ReadLogsAsync()).Select(log => log.Status));
    }

    private static string ToJson(JsonNode? node)
    {
        return node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) ?? "null";
    }

    private sealed record OkResult(bool Ok);

    private sealed record LookupInput(long Phone, long OrderId, long Price);
}
