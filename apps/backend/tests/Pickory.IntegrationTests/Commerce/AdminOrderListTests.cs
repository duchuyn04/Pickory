using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-14: danh sách đơn của nhân viên. Phân trang, bộ lọc, ngày theo giờ Việt Nam và thứ tự ổn định.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminOrderListTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/admin/orders";

    // Mốc cố định để test không phụ thuộc đồng hồ máy.
    private static readonly DateTime BaseTime = new(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc);

    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _kit;
    private readonly HttpClient _client;

    public AdminOrderListTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _kit = new AdminOrderTestKit(database);
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _kit.ResetAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public Task DisposeAsync() => _kit.ResetAsync();

    private async Task<string> StaffSessionAsync(string roleCode) =>
        (await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com14")).SessionToken;

    private async Task<HttpResponseMessage> GetAsync(string query = "", string roleCode = "sales")
    {
        var session = await StaffSessionAsync(roleCode);
        var path = query.Length == 0 ? OrdersPath : $"{OrdersPath}?{query}";
        return await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, $"pk_session={session}"));
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static string[] CodesOf(JsonElement data) =>
        data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString()!).ToArray();

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Hai trang liền nhau không trùng và không sót đơn. Tổng, số trang và giới hạn đi kèm trong response.
    public async Task List_Paginates_AndReportsTotal()
    {
        for (var index = 0; index < 5; index++)
        {
            await _kit.AddOrderAsync($"PG{index}", BaseTime.AddMinutes(index));
        }

        var first = await ReadDataAsync(await GetAsync("page=1&limit=2"));
        var second = await ReadDataAsync(await GetAsync("page=2&limit=2"));
        var third = await ReadDataAsync(await GetAsync("page=3&limit=2"));

        // Đơn mới nhất lên đầu.
        Assert.Equal(["PG4", "PG3"], CodesOf(first));
        Assert.Equal(["PG2", "PG1"], CodesOf(second));
        Assert.Equal(["PG0"], CodesOf(third));
        Assert.Equal(5, first.GetProperty("total").GetInt32());
        Assert.Equal(1, first.GetProperty("page").GetInt32());
        Assert.Equal(2, first.GetProperty("limit").GetInt32());
    }

    [Fact]
    // Mặc định trang 1 và 20 dòng, giống `PageQuery` của các route khác.
    public async Task List_DefaultsToFirstPageOfTwenty()
    {
        await _kit.AddOrderAsync("DEF1", BaseTime);

        var data = await ReadDataAsync(await GetAsync());

        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("limit=101")]
    [InlineData("limit=0")]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    // Giới hạn và số trang sai bị từ chối thay vì bị cắt âm thầm.
    public async Task List_InvalidPaging_Returns400(string query)
    {
        await AssertValidationErrorAsync(await GetAsync(query));
    }

    [Fact]
    // Mỗi bộ lọc chỉ trả đơn khớp. Giá trị lọc dùng nhãn snake_case giống nhãn trong response.
    public async Task List_FiltersByStatusPaymentStatusPaymentMethodAndFlag()
    {
        await _kit.AddOrderAsync("F-PEND", BaseTime.AddMinutes(1), OrderStatus.PendingConfirmation);
        await _kit.AddOrderAsync("F-SHIP", BaseTime.AddMinutes(2), OrderStatus.Shipping);
        await _kit.AddOrderAsync("F-SEPAY", BaseTime.AddMinutes(3), paymentMethod: PaymentMethod.Sepay, paymentStatus: PaymentStatus.Paid);
        await _kit.AddOrderAsync("F-UNDER", BaseTime.AddMinutes(4), flags: ["underpaid"]);
        await _kit.AddOrderAsync("F-BOTH", BaseTime.AddMinutes(5), flags: ["late_payment", "overpaid"]);

        Assert.Equal(["F-SHIP"], CodesOf(await ReadDataAsync(await GetAsync("status=shipping"))));
        Assert.Equal(["F-SEPAY"], CodesOf(await ReadDataAsync(await GetAsync("paymentMethod=sepay"))));
        Assert.Equal(["F-SEPAY"], CodesOf(await ReadDataAsync(await GetAsync("paymentStatus=paid"))));
        Assert.Equal(["F-UNDER"], CodesOf(await ReadDataAsync(await GetAsync("flag=underpaid"))));
        // Đơn có nhiều cờ khớp khi lọc theo bất kỳ cờ nào trong số đó.
        Assert.Equal(["F-BOTH"], CodesOf(await ReadDataAsync(await GetAsync("flag=overpaid"))));
    }

    [Fact]
    // COM-17 lọc "COD đã giao chưa nhận tiền" bằng tổ hợp ba tham số. Các bộ lọc kết hợp theo AND.
    public async Task List_CombinesFiltersWithAnd()
    {
        await _kit.AddOrderAsync("C-MATCH", BaseTime.AddMinutes(1), OrderStatus.Delivered, PaymentStatus.Unpaid, PaymentMethod.Cod);
        await _kit.AddOrderAsync("C-PAID", BaseTime.AddMinutes(2), OrderStatus.Delivered, PaymentStatus.Paid, PaymentMethod.Cod);
        await _kit.AddOrderAsync("C-SEPAY", BaseTime.AddMinutes(3), OrderStatus.Delivered, PaymentStatus.Unpaid, PaymentMethod.Sepay);
        await _kit.AddOrderAsync("C-SHIP", BaseTime.AddMinutes(4), OrderStatus.Shipping, PaymentStatus.Unpaid, PaymentMethod.Cod);

        var data = await ReadDataAsync(
            await GetAsync("status=delivered&paymentMethod=cod&paymentStatus=unpaid"));

        Assert.Equal(["C-MATCH"], CodesOf(data));
        Assert.Equal(1, data.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("status=khong_co")]
    [InlineData("paymentStatus=khong_co")]
    [InlineData("paymentMethod=khong_co")]
    [InlineData("flag=khong_co")]
    // Giá trị lọc không có trong tập nhãn bị từ chối, không bị bỏ qua âm thầm.
    public async Task List_UnknownFilterValue_Returns400(string query)
    {
        await AssertValidationErrorAsync(await GetAsync(query));
    }

    [Fact]
    // Ngày lọc tính theo giờ Việt Nam. 23:30 giờ Việt Nam (16:30 UTC) thuộc ngày đó, còn 00:30 giờ Việt Nam (17:30 UTC hôm trước) thuộc ngày sau.
    public async Task List_DateFilter_UsesVietnamDay()
    {
        await _kit.AddOrderAsync("D-2330", new DateTime(2026, 10, 5, 16, 30, 0, DateTimeKind.Utc));
        await _kit.AddOrderAsync("D-0030", new DateTime(2026, 10, 5, 17, 30, 0, DateTimeKind.Utc));

        var fifth = await ReadDataAsync(await GetAsync("from=2026-10-05&to=2026-10-05"));
        var sixth = await ReadDataAsync(await GetAsync("from=2026-10-06&to=2026-10-06"));

        Assert.Equal(["D-2330"], CodesOf(fifth));
        Assert.Equal(["D-0030"], CodesOf(sixth));
    }

    [Fact]
    // Biên ngày: đúng 00:00:00 giờ Việt Nam thuộc ngày mới, còn 23:59:59.999 thuộc ngày cũ.
    public async Task List_DateFilter_BoundariesBelongToCorrectDay()
    {
        // 16:59:59.999 UTC ngày 5 là 23:59:59.999 giờ Việt Nam ngày 5.
        await _kit.AddOrderAsync("B-LAST", new DateTime(2026, 10, 5, 16, 59, 59, 999, DateTimeKind.Utc));
        // 17:00:00 UTC ngày 5 là 00:00:00 giờ Việt Nam ngày 6.
        await _kit.AddOrderAsync("B-FIRST", new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc));

        Assert.Equal(["B-LAST"], CodesOf(await ReadDataAsync(await GetAsync("from=2026-10-05&to=2026-10-05"))));
        Assert.Equal(["B-FIRST"], CodesOf(await ReadDataAsync(await GetAsync("from=2026-10-06&to=2026-10-06"))));
    }

    [Fact]
    // Chỉ có `from` thì không giới hạn phía sau. Chỉ có `to` thì không giới hạn phía trước.
    public async Task List_DateFilter_AcceptsOpenEndedRanges()
    {
        await _kit.AddOrderAsync("O-EARLY", new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc));
        await _kit.AddOrderAsync("O-LATE", new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc));

        Assert.Equal(["O-LATE"], CodesOf(await ReadDataAsync(await GetAsync("from=2026-10-05"))));
        Assert.Equal(["O-EARLY"], CodesOf(await ReadDataAsync(await GetAsync("to=2026-10-05"))));
    }

    [Theory]
    [InlineData("from=2026-10-06&to=2026-10-05")]
    [InlineData("from=05/10/2026")]
    [InlineData("to=2026-02-30")]
    [InlineData("from=2026-10-05T10:00:00Z")]
    // `from` sau `to`, dạng ngày sai hoặc ngày không có thật đều trả 400.
    public async Task List_InvalidDates_Returns400(string query)
    {
        await AssertValidationErrorAsync(await GetAsync(query));
    }

    [Fact]
    // Hai đơn cùng `createdAt` phải có thứ tự cố định theo `id`, để đổi trang không lặp hoặc sót đơn.
    public async Task List_SameCreatedAt_OrdersByIdStably()
    {
        // Chèn theo thứ tự khác thứ tự `id`, để thứ tự vật lý của bảng không trùng thứ tự mong đợi.
        await _kit.AddOrderAsync("S-Z", BaseTime, id: "order-z");
        await _kit.AddOrderAsync("S-A", BaseTime, id: "order-a");
        await _kit.AddOrderAsync("S-M", BaseTime, id: "order-m");

        var all = await ReadDataAsync(await GetAsync("limit=10"));
        var pageOne = await ReadDataAsync(await GetAsync("page=1&limit=1"));
        var pageTwo = await ReadDataAsync(await GetAsync("page=2&limit=1"));
        var pageThree = await ReadDataAsync(await GetAsync("page=3&limit=1"));

        Assert.Equal(["S-A", "S-M", "S-Z"], CodesOf(all));
        Assert.Equal(["S-A"], CodesOf(pageOne));
        Assert.Equal(["S-M"], CodesOf(pageTwo));
        Assert.Equal(["S-Z"], CodesOf(pageThree));
    }

    [Fact]
    // Item danh sách có đủ các trường nhân viên cần, và `itemCount` là số dòng hàng chứ không phải tổng số lượng.
    public async Task List_ItemHasSummaryFields()
    {
        await _kit.AddOrderAsync(
            "I-ONE",
            BaseTime,
            OrderStatus.Confirmed,
            PaymentStatus.Paid,
            PaymentMethod.Sepay,
            flags: ["overpaid"],
            lineCount: 3,
            recipientName: "Trần Thị Bình",
            recipientPhone: "0987654321");

        var data = await ReadDataAsync(await GetAsync());

        var item = data.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("I-ONE", item.GetProperty("code").GetString());
        Assert.Equal("confirmed", item.GetProperty("status").GetString());
        Assert.Equal("paid", item.GetProperty("paymentStatus").GetString());
        Assert.Equal("sepay", item.GetProperty("paymentMethod").GetString());
        Assert.Equal(620_000, item.GetProperty("total").GetInt32());
        Assert.Equal(["overpaid"], item.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()!).ToArray());
        Assert.Equal("Trần Thị Bình", item.GetProperty("recipientName").GetString());
        Assert.Equal("0987654321", item.GetProperty("recipientPhone").GetString());
        Assert.Equal(3, item.GetProperty("lineCount").GetInt32());
        Assert.True(DateTime.TryParse(item.GetProperty("createdAt").GetString(), out _));
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("support")]
    [InlineData("admin")]
    // `sales` và `support` có `orders:read`, admin có mọi quyền. Response không được lưu vào bộ nhớ đệm vì có thông tin liên hệ.
    public async Task List_WithOrdersRead_Returns200WithNoStore(string roleCode)
    {
        var response = await GetAsync(roleCode: roleCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Nhân viên có role không chứa `orders:read` nhận 403.
    public async Task List_WithoutOrdersRead_Returns403()
    {
        var role = await IdentityTestData.CreateRoleAsync(_database, [Permissions.ProductsRead]);
        var user = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("com14-noperm"), UserKind.Staff, role.Code, emailVerified: true);
        var session = await IdentityTestData.CreateSessionAsync(_database, user.Id);

        var response = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, OrdersPath, $"pk_session={session}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Khách vãng lai nhận 401. Khách hàng đã đăng nhập nhận 403.
    public async Task List_AsGuestOrCustomer_IsRejected()
    {
        var guest = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, OrdersPath));
        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);

        var customer = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("com14-cust"), UserKind.Customer, "customer", emailVerified: true);
        var session = await IdentityTestData.CreateSessionAsync(_database, customer.Id);
        var response = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, OrdersPath, $"pk_session={session}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task AssertValidationErrorForFieldAsync(
        HttpResponseMessage response, string field, string? messagePart = null)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var message = error.GetProperty("message").GetString();
        Assert.Contains($"{field}:", message);
        if (messagePart is not null)
        {
            Assert.Contains(messagePart, message);
        }
    }

    private async Task<string[]> SearchCodesAsync(string search, string extraQuery = "")
    {
        var query = $"search={Uri.EscapeDataString(search)}&limit=100{extraQuery}";
        return CodesOf(await ReadDataAsync(await GetAsync(query)));
    }

    [Theory]
    [InlineData("status=shipping&status=pending_confirmation", "status")]
    [InlineData("paymentStatus=paid&paymentStatus=unpaid", "paymentStatus")]
    [InlineData("paymentMethod=cod&paymentMethod=sepay", "paymentMethod")]
    [InlineData("flag=underpaid&flag=overpaid", "flag")]
    [InlineData("from=2026-10-05&from=2026-10-06", "from")]
    [InlineData("to=2026-10-05&to=2026-10-06", "to")]
    // Tham số lặp lại nhận 400 `VALIDATION_ERROR` cho đúng trường, giống danh sách khách và nhân viên.
    public async Task List_RepeatedFilterParameter_Returns400ForThatField(string query, string field)
    {
        await _kit.AddOrderAsync("REP1", BaseTime);

        var response = await GetAsync(query);

        // Thông báo riêng của tham số lặp phân biệt lỗi này với lỗi giá trị lạ của chuỗi gộp `a,b`.
        await AssertValidationErrorForFieldAsync(response, field, "chỉ được gửi một lần");
    }

    [Theory]
    [InlineData("b-123")]
    [InlineData("AB-12")]
    // Một phần mã đơn tìm được đơn, không phân biệt hoa thường.
    public async Task Search_PartialOrderCode_FindsOrder(string keyword)
    {
        await _kit.AddOrderAsync("AB-12345", BaseTime.AddMinutes(1));
        await _kit.AddOrderAsync("CD-99999", BaseTime.AddMinutes(2));

        Assert.Equal(["AB-12345"], await SearchCodesAsync(keyword));
    }

    [Theory]
    [InlineData("nguyen")]
    [InlineData("NGUYEN")]
    [InlineData("Nguyễn")]
    // Tên người nhận lưu có dấu. Từ khóa không dấu, viết hoa hoặc có dấu đều tìm được đơn.
    public async Task Search_RecipientName_IgnoresDiacriticsAndCase(string keyword)
    {
        await _kit.AddOrderAsync("NM-1", BaseTime.AddMinutes(1), recipientName: "Nguyễn Văn An");
        await _kit.AddOrderAsync("NM-2", BaseTime.AddMinutes(2), recipientName: "Trần Thị Bình");

        Assert.Equal(["NM-1"], await SearchCodesAsync(keyword));
    }

    [Fact]
    // Chữ `Đ` không tách được thành `D` cộng dấu. `duc` phải tìm được `Đức` nhờ bảng quy tắc của `unaccent`.
    public async Task Search_RecipientNameWithStroke_FindsDuc()
    {
        await _kit.AddOrderAsync("DT-1", BaseTime.AddMinutes(1), recipientName: "Đức Minh");
        await _kit.AddOrderAsync("DT-2", BaseTime.AddMinutes(2), recipientName: "Lan Anh");

        Assert.Equal(["DT-1"], await SearchCodesAsync("duc"));
    }

    [Fact]
    // Một phần số điện thoại tìm được đơn.
    public async Task Search_PartialPhone_FindsOrder()
    {
        await _kit.AddOrderAsync("PH-1", BaseTime.AddMinutes(1), recipientPhone: "0987654321");
        await _kit.AddOrderAsync("PH-2", BaseTime.AddMinutes(2), recipientPhone: "0911111111");

        Assert.Equal(["PH-1"], await SearchCodesAsync("876543"));
    }

    [Fact]
    // Một phần email tìm được đơn, không phân biệt hoa thường.
    public async Task Search_PartialEmail_FindsOrder()
    {
        await _kit.AddOrderAsync("EM-1", BaseTime.AddMinutes(1), recipientEmail: "lan.tran@example.com");
        await _kit.AddOrderAsync("EM-2", BaseTime.AddMinutes(2), recipientEmail: "khac@example.com");

        Assert.Equal(["EM-1"], await SearchCodesAsync("LAN.TR"));
    }

    [Fact]
    // `%` trong từ khóa là ký tự thường. Đơn `100x` không được khớp từ khóa `100%`.
    public async Task Search_PercentSign_MatchesOnlyLiteralPercent()
    {
        await _kit.AddOrderAsync("PCT-1", BaseTime.AddMinutes(1), recipientName: "Giảm 100% An");
        await _kit.AddOrderAsync("PCT-2", BaseTime.AddMinutes(2), recipientName: "Giảm 100x An");

        Assert.Equal(["PCT-1"], await SearchCodesAsync("100%"));
    }

    [Fact]
    // `_` trong từ khóa là ký tự thường. Đơn `axb` không được khớp từ khóa `a_b`.
    public async Task Search_Underscore_MatchesOnlyLiteralUnderscore()
    {
        await _kit.AddOrderAsync("UND-1", BaseTime.AddMinutes(1), recipientName: "Bạn a_b");
        await _kit.AddOrderAsync("UND-2", BaseTime.AddMinutes(2), recipientName: "Bạn axb");

        Assert.Equal(["UND-1"], await SearchCodesAsync("a_b"));
    }

    [Fact]
    // `search` kết hợp với `status`, `paymentStatus`, `flag`, `from` và `to` theo AND. Mỗi đơn nhiễu sai đúng một điều kiện.
    public async Task Search_CombinesWithEveryOtherFilterUsingAnd()
    {
        var inRange = BaseTime.AddMinutes(1);
        await _kit.AddOrderAsync(
            "AND-HIT", inRange, OrderStatus.Shipping, PaymentStatus.Paid, flags: ["underpaid"], recipientName: "Nguyễn An");
        await _kit.AddOrderAsync(
            "AND-STATUS", inRange, OrderStatus.Delivered, PaymentStatus.Paid, flags: ["underpaid"], recipientName: "Nguyễn An");
        await _kit.AddOrderAsync(
            "AND-PAY", inRange, OrderStatus.Shipping, PaymentStatus.Unpaid, flags: ["underpaid"], recipientName: "Nguyễn An");
        await _kit.AddOrderAsync(
            "AND-FLAG", inRange, OrderStatus.Shipping, PaymentStatus.Paid, flags: ["overpaid"], recipientName: "Nguyễn An");
        await _kit.AddOrderAsync(
            "AND-DATE", BaseTime.AddDays(10), OrderStatus.Shipping, PaymentStatus.Paid, flags: ["underpaid"], recipientName: "Nguyễn An");
        await _kit.AddOrderAsync(
            "AND-NAME", inRange, OrderStatus.Shipping, PaymentStatus.Paid, flags: ["underpaid"], recipientName: "Trần Bình");

        var codes = await SearchCodesAsync(
            "nguyen", "&status=shipping&paymentStatus=paid&flag=underpaid&from=2026-10-05&to=2026-10-05");

        Assert.Equal(["AND-HIT"], codes);
    }

    [Fact]
    // `total` đếm cùng điều kiện tìm. `limit` nhỏ hơn số dòng khớp chỉ cắt dòng trả về.
    public async Task Search_TotalCountsMatchingOrdersNotPageRows()
    {
        await _kit.AddOrderAsync("CNT-1", BaseTime.AddMinutes(1), recipientName: "Lê Văn Tổng");
        await _kit.AddOrderAsync("CNT-2", BaseTime.AddMinutes(2), recipientName: "Lê Văn Tổng");
        await _kit.AddOrderAsync("CNT-3", BaseTime.AddMinutes(3), recipientName: "Lê Văn Tổng");
        await _kit.AddOrderAsync("CNT-4", BaseTime.AddMinutes(4), recipientName: "Người khác");

        var data = await ReadDataAsync(await GetAsync($"search={Uri.EscapeDataString("le van tong")}&limit=1"));

        Assert.Single(CodesOf(data));
        Assert.Equal(3, data.GetProperty("total").GetInt32());
    }

    [Fact]
    // Đơn có `contact` thiếu `email` vẫn trả 200. Đơn đó khớp theo tên và chỉ không khớp theo email.
    public async Task Search_OrderWithoutEmail_IsNotAnErrorAndMatchesOtherFields()
    {
        await _kit.AddOrderAsync("NOEM-1", BaseTime.AddMinutes(1), recipientName: "Phạm Không Email", omitRecipientEmail: true);
        await _kit.AddOrderAsync("NOEM-2", BaseTime.AddMinutes(2), recipientName: "Người Có Email", recipientEmail: "co.email@example.com");

        var byName = await SearchCodesAsync("pham khong");
        var byEmail = await SearchCodesAsync("co.email");

        Assert.Equal(["NOEM-1"], byName);
        Assert.Equal(["NOEM-2"], byEmail);
    }

    [Fact]
    // `search` lặp lại và từ khóa 101 ký tự nhận 400 `VALIDATION_ERROR` cho trường `search`. Đúng 100 ký tự vẫn đạt.
    public async Task Search_RepeatedOrTooLong_Returns400()
    {
        await _kit.AddOrderAsync("SRCH-1", BaseTime);

        var repeated = await GetAsync("search=a&search=b");
        var tooLong = await GetAsync($"search={new string('a', 101)}");
        var exact = await GetAsync($"search={new string('a', 100)}");

        await AssertValidationErrorForFieldAsync(repeated, "search");
        await AssertValidationErrorForFieldAsync(tooLong, "search");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [Fact]
    // Từ khóa trống được coi như không gửi. Form lọc để trống ô không gây lỗi và không lọc.
    public async Task Search_BlankValue_ReturnsEveryOrder()
    {
        await _kit.AddOrderAsync("BLK-1", BaseTime.AddMinutes(1));
        await _kit.AddOrderAsync("BLK-2", BaseTime.AddMinutes(2));

        var data = await ReadDataAsync(await GetAsync("search=%20%20"));

        Assert.Equal(2, data.GetProperty("total").GetInt32());
    }
}
