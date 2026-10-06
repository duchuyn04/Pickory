using System.Text.Json.Nodes;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Settings;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port từ `tool-loop.test.ts` của bản Node. Gateway tự chạy từng bước, không giao vòng lặp cho SDK.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ToolLoopTests(IntegrationDatabaseFixture database)
{
    private static readonly CallContext ChatContext = new(AiFeature.ShoppingAssistantChat, "user:customer_vip_001");

    private static ToolCall Call(string id, string name, string inputJson = "{}")
    {
        return new ToolCall(id, name, JsonNode.Parse(inputJson));
    }

    private static ToolDefinition<ItemInput> ItemTool(string name, Func<string, string> answer)
    {
        return new ToolDefinition<ItemInput>(name, $"Công cụ {name}", (input, _) => Task.FromResult<object?>(answer(input.Item)));
    }

    [Fact]
    // Ba bước ghi đúng 3 dòng log. Vòng lặp dừng ở `maxSteps` dù model còn muốn gọi tool.
    public async Task ChatAsync_StopsAtMaxStepsWithOneLogPerStep()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("Đang tra cứu giá...", new TokenUsage(20, 10, 0), [Call("tc1", "lookup_price", """{"item":"iPhone 15"}""")]));
        harness.Chat.EnqueueResponse(new ChatResult("Đang kiểm tra kho...", new TokenUsage(40, 15, 0), [Call("tc2", "check_stock", """{"item":"iPhone 15"}""")]));
        harness.Chat.EnqueueResponse(new ChatResult("Giá là 20 triệu và còn hàng.", new TokenUsage(60, 20, 0), [Call("tc3", "lookup_discount", """{"item":"iPhone 15"}""")]));
        ToolDefinition[] tools =
        [
            ItemTool("lookup_price", item => $"{item}: 20.000.000 VND"),
            ItemTool("check_stock", item => $"{item}: Còn 5 máy"),
        ];

        var result = await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Tìm iPhone 15")], Tools = tools, MaxSteps = 3 },
            ChatContext);

        Assert.Equal("Giá là 20 triệu và còn hàng.", result.Text);
        Assert.Equal(new TokenUsage(120, 45, 0), result.Usage);
        Assert.Equal(3, harness.Chat.RecordedRequests.Count);
        Assert.Equal(3, (await harness.ReadLogsAsync()).Count);
        var toolMessage = Assert.IsType<ToolChatMessage>(harness.Chat.RecordedRequests[1].Messages[^1]);
        Assert.Equal("iPhone 15: 20.000.000 VND", toolMessage.Content);
    }

    [Fact]
    // Kết quả tool chứa số điện thoại và email. Gateway ẩn chúng trước khi gửi lại cho model.
    public async Task ChatAsync_RedactsToolOutput()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("Đang gọi công cụ...", new TokenUsage(15, 5, 0), [Call("tc_info", "get_contact")]));
        harness.Chat.EnqueueResponse(new ChatResult("Đã hoàn tất tra cứu.", new TokenUsage(30, 10, 0)));
        var contactTool = new ToolDefinition<EmptyInput>("get_contact", "Lấy thông tin liên hệ hỗ trợ", (_, _) =>
            Task.FromResult<object?>("Liên hệ hotline 0912 345 678 hoặc email support@pickory.vn để gặp nhân viên."));

        await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Cần số điện thoại hỗ trợ")], Tools = [contactTool], MaxSteps = 2 },
            ChatContext);

        var toolMessage = harness.Chat.RecordedRequests[1].Messages.OfType<ToolChatMessage>().Single();
        Assert.DoesNotContain("0912 345 678", toolMessage.Content);
        Assert.DoesNotContain("support@pickory.vn", toolMessage.Content);
        Assert.Contains("[đã ẩn]", toolMessage.Content);
    }

    [Fact]
    // Tool ném lỗi thì model nhận thông báo an toàn. Chi tiết exception không đi vào prompt.
    public async Task ChatAsync_ToolExceptionBecomesSafeMessage()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("Đang truy vấn kho...", new TokenUsage(10, 5, 0), [Call("tc_err", "failing_tool")]));
        harness.Chat.EnqueueResponse(new ChatResult("Hệ thống kho đang bận, xin quý khách thử lại sau.", new TokenUsage(25, 12, 0)));
        var failingTool = new ToolDefinition<EmptyInput>("failing_tool", "Công cụ gặp sự cố", (_, _) =>
            throw new InvalidOperationException("Mất kết nối tới máy chủ kho postgres://secret"));

        var result = await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Xem hàng")], Tools = [failingTool], MaxSteps = 2 },
            ChatContext);

        Assert.Equal("Hệ thống kho đang bận, xin quý khách thử lại sau.", result.Text);
        var toolMessage = harness.Chat.RecordedRequests[1].Messages.OfType<ToolChatMessage>().Single();
        Assert.Contains("Thao tác thực thi công cụ \"failing_tool\" không thành công", toolMessage.Content);
        Assert.DoesNotContain("secret", toolMessage.Content);
    }

    [Fact]
    // Input sai kiểu và tên tool lạ đều thành thông báo cho model. Tool không bị chạy với input sai.
    public async Task ChatAsync_InvalidInputAndUnknownToolBecomeMessages()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult(string.Empty, new TokenUsage(10, 5, 0),
        [
            Call("bad", "lookup_price", """{"item":42}"""),
            Call("missing", "no_such_tool"),
        ]));
        harness.Chat.EnqueueResponse(new ChatResult("Xin lỗi.", new TokenUsage(10, 5, 0)));
        var calls = 0;
        var lookup = ItemTool("lookup_price", item =>
        {
            calls++;
            return item;
        });

        await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Giá?")], Tools = [lookup], MaxSteps = 2 },
            ChatContext);

        Assert.Equal(0, calls);
        var toolMessages = harness.Chat.RecordedRequests[1].Messages.OfType<ToolChatMessage>().ToList();
        Assert.StartsWith("Lỗi tham số đầu vào cho công cụ \"lookup_price\": item", toolMessages[0].Content);
        Assert.Equal("Lỗi: Công cụ \"no_such_tool\" không tồn tại.", toolMessages[1].Content);
    }

    [Fact]
    // Chi phí vượt 120% giữa hai bước. Gateway dừng trước bước 2 và ghi log bị chặn.
    public async Task ChatAsync_StopsWhenBudgetIsReachedBetweenSteps()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(100, 50, 15));
        harness.Chat.EnqueueResponse(new ChatResult("Bước 1 hoàn tất", new TokenUsage(10, 5, 0), [Call("tc1", "dummy_tool")]));
        var dummyTool = new ToolDefinition<EmptyInput>("dummy_tool", "Công cụ giả lập", async (_, _) =>
        {
            // Chi phí tháng lên 130 USD giữa hai bước, vượt 120% của ngân sách 100 USD.
            await harness.AddCostAsync(130);
            return "kết quả bước 1";
        });

        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Bắt đầu")], Tools = [dummyTool], MaxSteps = 3 },
            ChatContext));

        Assert.Single(harness.Chat.RecordedRequests);
        var blocked = (await harness.ReadLogsAsync()).Single(log => log.Status == "blocked");
        Assert.Equal("BUDGET_EXCEEDED", blocked.ErrorCode);
    }

    private sealed record ItemInput(string Item);

    private sealed record EmptyInput;
}
