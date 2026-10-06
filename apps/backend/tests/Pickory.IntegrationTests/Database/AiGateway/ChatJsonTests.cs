using FluentValidation;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Settings;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port từ `chat-json.test.ts` của bản Node. Bản .NET đọc JSON thành kiểu C# thay cho schema Zod.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ChatJsonTests(IntegrationDatabaseFixture database)
{
    private static readonly CallContext JsonContext = new(AiFeature.PersonalizedContentDescription, "user:staff_marketing_1");
    private static readonly ChatRequest Request = new() { Messages = [new UserChatMessage("Tạo sản phẩm mẫu")] };

    [Fact]
    // JSON đúng ở lần đầu. Gateway gọi provider 1 lần và ghi 1 dòng log.
    public async Task ChatJsonAsync_ReturnsValidObjectOnFirstAttempt()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo thun cotton","price":150000}""", new TokenUsage(50, 20, 0)));

        var item = await harness.Gateway.ChatJsonAsync<Item>(Request, JsonContext);

        Assert.Equal(new Item("Áo thun cotton", 150000), item);
        var providerRequest = Assert.Single(harness.Chat.RecordedRequests);
        Assert.True(providerRequest.Json);
        Assert.Single(await harness.ReadLogsAsync());
    }

    [Fact]
    // Lần đầu thiếu trường. Gateway thử lại 1 lần kèm lý do lỗi và nhận kết quả đúng ở lần hai.
    public async Task ChatJsonAsync_RetriesOnceWithValidationReason()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo thun lỗi"}""", new TokenUsage(40, 15, 0)));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo thun chuẩn","price":180000}""", new TokenUsage(60, 25, 0)));

        var item = await harness.Gateway.ChatJsonAsync<Item>(Request, JsonContext);

        Assert.Equal(new Item("Áo thun chuẩn", 180000), item);
        Assert.Equal(2, harness.Chat.RecordedRequests.Count);
        var retryMessages = harness.Chat.RecordedRequests[1].Messages;
        Assert.IsType<AssistantChatMessage>(retryMessages[^2]);
        var correction = Assert.IsType<UserChatMessage>(retryMessages[^1]);
        Assert.Contains("Dữ liệu không khớp schema", correction.Content);
        var logs = await harness.ReadLogsAsync();
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, log => log.Status == "error" && log.ErrorCode == "AI_INVALID_OUTPUT");
        Assert.Contains(logs, log => log.Status == "ok");
    }

    [Fact]
    // Sai kiểu ở cả 2 lần thì ném `AiInvalidOutputException`. Cả 2 dòng log đều là lỗi output.
    public async Task ChatJsonAsync_ThrowsAfterTwoInvalidAttempts()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo","price":"không phải số"}""", new TokenUsage(30, 10, 0)));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Vẫn sai","price":"vẫn không phải số"}""", new TokenUsage(45, 12, 0)));

        await Assert.ThrowsAsync<AiInvalidOutputException>(() => harness.Gateway.ChatJsonAsync<Item>(Request, JsonContext));

        Assert.Equal(2, harness.Chat.RecordedRequests.Count);
        var logs = await harness.ReadLogsAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(("error", "AI_INVALID_OUTPUT"), (log.Status, log.ErrorCode)));
    }

    [Fact]
    // Validator của module gọi cũng là một phần của schema. Giá âm làm gateway thử lại.
    public async Task ChatJsonAsync_ValidatorFailureTriggersRetry()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo","price":-5}""", new TokenUsage(30, 10, 0)));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo","price":5}""", new TokenUsage(30, 10, 0)));

        var item = await harness.Gateway.ChatJsonAsync(Request, JsonContext, new ItemValidator());

        Assert.Equal(5, item.Price);
        var correction = Assert.IsType<UserChatMessage>(harness.Chat.RecordedRequests[1].Messages[^1]);
        Assert.Contains("price", correction.Content);
    }

    [Fact]
    // Prompt chưa nhắc tới JSON thì gateway thêm chỉ dẫn hệ thống. DeepSeek yêu cầu từ `json` trong prompt.
    public async Task ChatJsonAsync_AddsJsonInstructionWhenMissing()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("""{"title":"Áo","price":1}""", new TokenUsage(30, 10, 0)));

        await harness.Gateway.ChatJsonAsync<Item>(Request, JsonContext);

        var first = Assert.IsType<SystemChatMessage>(harness.Chat.RecordedRequests[0].Messages[0]);
        Assert.Contains("JSON", first.Content);
    }

    private sealed record Item(string Title, decimal Price);

    private sealed class ItemValidator : AbstractValidator<Item>
    {
        public ItemValidator()
        {
            RuleFor(item => item.Price).GreaterThan(0);
        }
    }
}
