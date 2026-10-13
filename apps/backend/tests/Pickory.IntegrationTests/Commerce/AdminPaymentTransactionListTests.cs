using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-13: danh sách giao dịch SePay cho nhân viên. Bộ lọc trạng thái khớp, cờ đơn, phân trang và thứ tự ổn định.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminPaymentTransactionListTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string TransactionsPath = "/api/admin/payments/transactions";

    // Mốc cố định để test không phụ thuộc đồng hồ máy.
    private static readonly DateTime BaseTime = new(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc);

    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _kit;
    private readonly HttpClient _client;

    public AdminPaymentTransactionListTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _kit = new AdminOrderTestKit(database);
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Kit xóa cả giao dịch lẫn đơn, vì danh sách đếm cả bảng và dữ liệu của test khác làm lệch tổng.
        await _kit.ResetAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public Task DisposeAsync() => _kit.ResetAsync();

    private async Task<HttpResponseMessage> GetAsync(string query = "", string roleCode = "sales")
    {
        var session = (await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com13list")).SessionToken;
        var path = query.Length == 0 ? TransactionsPath : $"{TransactionsPath}?{query}";
        return await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, $"pk_session={session}"));
    }

    private async Task AddTransactionAsync(
        string id, string matchStatus, DateTime receivedAt, string? orderId = null, int amount = 220_000)
    {
        await using var commerce = _database.CreateCommerceContext();
        commerce.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = id,
            Provider = "sepay",
            ProviderTxId = "list-" + id,
            Amount = amount,
            Content = "Noi dung " + id,
            // Payload thô chứa một dấu hiệu riêng để test kiểm response không rò rỉ cột `raw`.
            Raw = JsonSerializer.SerializeToDocument(new { secretMarker = "RAW-SECRET-" + id }),
            OrderId = orderId,
            MatchStatus = matchStatus,
            ReceivedAt = receivedAt,
        });
        await commerce.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static string[] IdsOf(JsonElement data) =>
        data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(field, document.RootElement.GetProperty("error").GetRawText());
    }

    [Fact]
    // Mỗi giá trị `matchStatus` chỉ trả giao dịch ở trạng thái đó.
    public async Task List_FiltersByMatchStatus()
    {
        var order = await _kit.AddOrderAsync("PM1", BaseTime, OrderStatus.PendingConfirmation, PaymentStatus.Paid, PaymentMethod.Sepay);
        await AddTransactionAsync("tx-unmatched", "unmatched", BaseTime.AddMinutes(1));
        await AddTransactionAsync("tx-matched", "matched", BaseTime.AddMinutes(2), order.Id);
        await AddTransactionAsync("tx-ignored", "ignored", BaseTime.AddMinutes(3));

        Assert.Equal(["tx-unmatched"], IdsOf(await ReadDataAsync(await GetAsync("matchStatus=unmatched"))));
        Assert.Equal(["tx-matched"], IdsOf(await ReadDataAsync(await GetAsync("matchStatus=matched"))));
        Assert.Equal(["tx-ignored"], IdsOf(await ReadDataAsync(await GetAsync("matchStatus=ignored"))));
        Assert.Equal(3, (await ReadDataAsync(await GetAsync())).GetProperty("total").GetInt32());
    }

    [Fact]
    // `orderFlag` lọc theo cờ của đơn gắn kèm. Giao dịch chưa gắn đơn không bao giờ khớp.
    public async Task List_FiltersByOrderFlag_AndSkipsUnattachedTransactions()
    {
        var underpaid = await _kit.AddOrderAsync("PU1", BaseTime, flags: ["underpaid"], paymentMethod: PaymentMethod.Sepay, status: OrderStatus.PendingPayment);
        var overpaid = await _kit.AddOrderAsync("PO1", BaseTime, flags: ["overpaid"], paymentMethod: PaymentMethod.Sepay, status: OrderStatus.PendingConfirmation, paymentStatus: PaymentStatus.Paid);
        var plain = await _kit.AddOrderAsync("PN1", BaseTime, paymentMethod: PaymentMethod.Sepay, status: OrderStatus.PendingConfirmation, paymentStatus: PaymentStatus.Paid);
        await AddTransactionAsync("tx-under", "matched", BaseTime.AddMinutes(1), underpaid.Id, amount: 100_000);
        await AddTransactionAsync("tx-over", "matched", BaseTime.AddMinutes(2), overpaid.Id, amount: 300_000);
        await AddTransactionAsync("tx-plain", "matched", BaseTime.AddMinutes(3), plain.Id);
        await AddTransactionAsync("tx-free", "unmatched", BaseTime.AddMinutes(4));

        Assert.Equal(["tx-under"], IdsOf(await ReadDataAsync(await GetAsync("orderFlag=underpaid"))));
        Assert.Equal(["tx-over"], IdsOf(await ReadDataAsync(await GetAsync("orderFlag=overpaid"))));
    }

    [Theory]
    [InlineData("matchStatus=khong_co", "matchStatus")]
    [InlineData("orderFlag=khong_co", "orderFlag")]
    // Cờ hợp lệ của đơn nhưng ngoài tập `underpaid` và `overpaid` của màn đối soát cũng bị từ chối.
    [InlineData("orderFlag=late_payment", "orderFlag")]
    [InlineData("matchStatus=matched&matchStatus=ignored", "matchStatus")]
    [InlineData("orderFlag=underpaid&orderFlag=overpaid", "orderFlag")]
    [InlineData("limit=101", "limit")]
    [InlineData("page=0", "page")]
    // Giá trị lạ và tham số lặp bị từ chối cho đúng trường, không bị bỏ qua âm thầm.
    public async Task List_InvalidQuery_Returns400ForTheField(string query, string field)
    {
        await AssertValidationErrorAsync(await GetAsync(query), field);
    }

    [Fact]
    // `total` đếm cùng điều kiện lọc, không đếm cả bảng, và hai trang liền nhau không trùng hay sót.
    public async Task List_TotalCountsFilteredRows_AndPaginates()
    {
        for (var index = 0; index < 5; index++)
        {
            await AddTransactionAsync($"tx-u{index}", "unmatched", BaseTime.AddMinutes(index));
        }

        await AddTransactionAsync("tx-ign", "ignored", BaseTime.AddMinutes(9));

        var first = await ReadDataAsync(await GetAsync("matchStatus=unmatched&page=1&limit=2"));
        var second = await ReadDataAsync(await GetAsync("matchStatus=unmatched&page=2&limit=2"));
        var third = await ReadDataAsync(await GetAsync("matchStatus=unmatched&page=3&limit=2"));

        // Giao dịch mới nhất lên đầu.
        Assert.Equal(["tx-u4", "tx-u3"], IdsOf(first));
        Assert.Equal(["tx-u2", "tx-u1"], IdsOf(second));
        Assert.Equal(["tx-u0"], IdsOf(third));
        Assert.Equal(5, first.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("limit").GetInt32());
    }

    [Fact]
    // Hai giao dịch cùng `ReceivedAt` xếp theo `Id`, nên phân trang không lặp hay sót khi gọi lại.
    public async Task List_SameReceivedAt_OrdersByIdStably()
    {
        await AddTransactionAsync("tx-c", "unmatched", BaseTime);
        await AddTransactionAsync("tx-a", "unmatched", BaseTime);
        await AddTransactionAsync("tx-b", "unmatched", BaseTime);

        var first = await ReadDataAsync(await GetAsync("page=1&limit=2"));
        var second = await ReadDataAsync(await GetAsync("page=2&limit=2"));
        var again = await ReadDataAsync(await GetAsync("page=1&limit=2"));

        Assert.Equal(["tx-a", "tx-b"], IdsOf(first));
        Assert.Equal(["tx-c"], IdsOf(second));
        Assert.Equal(IdsOf(first), IdsOf(again));
    }

    [Fact]
    // Mỗi dòng có đủ trường và `order` của giao dịch đã gắn. Cột `raw` không có trong response.
    public async Task List_RowShape_HasOrderSummaryAndNeverRaw()
    {
        var order = await _kit.AddOrderAsync(
            "PS1", BaseTime, OrderStatus.PendingPayment, paymentMethod: PaymentMethod.Sepay, flags: ["underpaid"]);
        await AddTransactionAsync("tx-shape", "matched", BaseTime, order.Id, amount: 100_000);
        await AddTransactionAsync("tx-free", "unmatched", BaseTime.AddMinutes(-1));

        var response = await GetAsync();
        var text = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");

        Assert.DoesNotContain("RAW-SECRET", text);
        Assert.DoesNotContain("\"raw\"", text);
        var attached = data.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "tx-shape");
        Assert.Equal("list-tx-shape", attached.GetProperty("providerTxId").GetString());
        Assert.Equal(100_000, attached.GetProperty("amount").GetInt32());
        Assert.Equal("Noi dung tx-shape", attached.GetProperty("content").GetString());
        Assert.Equal("matched", attached.GetProperty("matchStatus").GetString());
        var orderJson = attached.GetProperty("order");
        Assert.Equal("PS1", orderJson.GetProperty("code").GetString());
        Assert.Equal("pending_payment", orderJson.GetProperty("status").GetString());
        Assert.Equal("unpaid", orderJson.GetProperty("paymentStatus").GetString());
        Assert.Equal(220_000, orderJson.GetProperty("total").GetInt32());
        Assert.Equal(["underpaid"], orderJson.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()!).ToArray());
        var free = data.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "tx-free");
        Assert.Equal(JsonValueKind.Null, free.GetProperty("order").ValueKind);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // `support` không có `payments:read` nên nhận 403.
    public async Task List_WithoutPermission_Returns403()
    {
        var response = await GetAsync(roleCode: "support");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
