using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.ActivityTracking.Counters;
using Pickory.Modules.ActivityTracking.Features.CollectEvents;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Entities;
using Pickory.Shared.Calendar;
using Xunit;

namespace Pickory.IntegrationTests.ActivityTracking;

// ACT-06: `POST /api/events` cộng bộ đếm `product_view` của ngày giờ Việt Nam. Test dùng đồng hồ giả chọn một ngày riêng từ năm 2031, riêng test đồng hồ thật ghi vào ngày hôm nay.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CollectEventsTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string ProductViewMetric = "product_view";
    private const string ProductViewBody = """{"type":"product_view"}""";
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string GooglebotUserAgent = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Mỗi host có limiter và đồng hồ riêng. Giải phóng host để test sau không giữ kết nối DB của test trước.
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Post_ValidBody_Returns204AndIncrementsCounterByOneEachTime()
    {
        var day = new DateOnly(2031, 1, 5);
        var (client, _) = await CreateHostAsync(day);

        var first = await SendAsync(client, ProductViewBody);
        var afterFirst = await ReadCounterAsync(day);
        var second = await SendAsync(client, ProductViewBody);
        var afterSecond = await ReadCounterAsync(day);

        // Lần hai đi qua nhánh `ON CONFLICT`, vì dòng của ngày đã có từ lần một.
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(1, afterFirst);
        Assert.Equal(2, afterSecond);
    }

    [Fact]
    public async Task Post_WithSystemClock_IncrementsTodayVietnamCounterByOne()
    {
        // Factory không thay `TimeProvider`. Test này kiểm host thật ghi vào bộ đếm của ngày Việt Nam hôm nay.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var today = VietnamCalendar.ToVietnamDate(DateTime.UtcNow);
        // Ngày có thể đổi giữa lúc gửi. Cộng cả hai ngày liền nhau để tổng vẫn tăng đúng 1.
        var before = await ReadCounterAsync(today) + await ReadCounterAsync(today.AddDays(1));

        var response = await SendAsync(client, ProductViewBody);
        var after = await ReadCounterAsync(today) + await ReadCounterAsync(today.AddDays(1));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before + 1, after);
    }

    [Theory]
    [InlineData("""{"type":"category_view"}""")]
    [InlineData("""{"type":"PRODUCT_VIEW"}""")]
    [InlineData("{}")]
    [InlineData("""{"type":null}""")]
    [InlineData("""{"type":1}""")]
    [InlineData("""{"type":"product_view","productId":"p1"}""")]
    [InlineData("[]")]
    [InlineData("\"product_view\"")]
    [InlineData("not json")]
    [InlineData("{\"type\":")]
    [InlineData("")]
    public async Task Post_InvalidBody_Returns400ValidationErrorAndKeepsCounter(string rawBody)
    {
        var day = new DateOnly(2031, 1, 6);
        var (client, _) = await CreateHostAsync(day);

        var response = await SendAsync(client, rawBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_BodyOver1KiB_Returns413PayloadTooLargeAndKeepsCounter()
    {
        var day = new DateOnly(2031, 1, 13);
        var (client, _) = await CreateHostAsync(day);
        // Body 2 KB có `type` hợp lệ. Route vẫn từ chối vì body vượt giới hạn 1 KB.
        var oversizedBody = "{\"type\":\"product_view\",\"padding\":\"" + new string('a', 2048) + "\"}";

        var response = await SendAsync(client, oversizedBody);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("PAYLOAD_TOO_LARGE", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_InvalidBodyDoesNotConsumeRateLimit()
    {
        var day = new DateOnly(2031, 1, 7);
        var (client, _) = await CreateHostAsync(day);
        var anonymousId = NewAnonymousId();

        // 61 request sai không được tính lượt, nên request đúng sau đó vẫn qua.
        for (var i = 0; i <= 60; i++)
        {
            var invalid = await SendAsync(client, "{}", anonymousId);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        var valid = await SendAsync(client, ProductViewBody, anonymousId);

        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Equal(1, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_TwentyParallelRequests_IncrementsCounterByExactlyTwenty()
    {
        var day = new DateOnly(2031, 1, 8);
        var (client, _) = await CreateHostAsync(day);

        // 20 request cùng IP vẫn dưới hạn mức 60 mỗi phút, nên giới hạn tần suất không chặn request nào.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => SendAsync(client, ProductViewBody)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Equal(20, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_StaffSession_Returns204AndKeepsCounter()
    {
        var day = new DateOnly(2031, 1, 9);
        var (client, _) = await CreateHostAsync(day);
        var (_, staffSession) = await IdentityTestData.CreateStaffSessionAsync(database, "admin");

        var response = await SendAsync(client, ProductViewBody, sessionToken: staffSession);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_CustomerSession_Returns204AndIncrementsCounter()
    {
        var day = new DateOnly(2031, 1, 10);
        var (client, _) = await CreateHostAsync(day);
        var customer = await IdentityTestData.CreateUserAsync(
            database, IdentityTestData.NewEmail("view-customer"), UserKind.Customer, "customer", emailVerified: true);
        var customerSession = await IdentityTestData.CreateSessionAsync(database, customer.Id);

        var response = await SendAsync(client, ProductViewBody, sessionToken: customerSession);

        // Chỉ nhân viên bị bỏ qua. Khách đã đăng nhập vẫn là khách mua hàng nên được đếm.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_CrawlerUserAgent_Returns204AndKeepsCounter()
    {
        var day = new DateOnly(2031, 1, 11);
        var (client, _) = await CreateHostAsync(day);

        var response = await SendAsync(client, ProductViewBody, userAgent: GooglebotUserAgent);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_SixtyOneRequestsInOneMinuteFromSameIp_ReturnsTooManyRequestsOnTheLastOne()
    {
        var day = new DateOnly(2031, 1, 12);
        var (client, clock) = await CreateHostAsync(day);

        for (var i = 0; i < 60; i++)
        {
            var allowed = await SendAsync(client, ProductViewBody);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var limited = await SendAsync(client, ProductViewBody);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(limited);
        Assert.Equal("TOO_MANY_REQUESTS", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        // Request bị chặn không cộng bộ đếm.
        Assert.Equal(60, await ReadCounterAsync(day));

        // Hết cửa sổ 1 phút thì IP đã hết lượt được gửi lại.
        clock.Advance(TimeSpan.FromMinutes(1));
        var afterWindow = await SendAsync(client, ProductViewBody);
        Assert.Equal(HttpStatusCode.NoContent, afterWindow.StatusCode);
        Assert.Equal(61, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_NewAnonymousIdOnEveryRequestFromSameIp_StillLimitedAtTheSixtyFirst()
    {
        var day = new DateOnly(2031, 1, 15);
        var (client, _) = await CreateHostAsync(day);

        // Mỗi request mang `pk_anon` mới. Khóa theo `AnonymousId` sẽ không bao giờ chặn được client này.
        for (var i = 0; i < 60; i++)
        {
            var allowed = await SendAsync(client, ProductViewBody);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var limited = await SendAsync(client, ProductViewBody);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(limited);
        Assert.Equal("TOO_MANY_REQUESTS", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(60, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_IpOutOfQuota_OtherIpStillReturns204()
    {
        var day = new DateOnly(2031, 1, 16);
        var (client, _) = await CreateHostAsync(day, trustProxy: true);
        const string exhaustedIp = "198.51.100.1";

        for (var i = 0; i < 60; i++)
        {
            var allowed = await SendAsync(client, ProductViewBody, clientIp: exhaustedIp);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var limited = await SendAsync(client, ProductViewBody, clientIp: exhaustedIp);
        var otherIp = await SendAsync(client, ProductViewBody, clientIp: "198.51.100.2");

        // Mỗi IP có hạn mức riêng. IP đã hết lượt không chặn IP khác.
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, otherIp.StatusCode);
        Assert.Equal(61, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_TwoCustomersOnSameIp_EachHasOwnQuota()
    {
        var day = new DateOnly(2031, 1, 17);
        var (client, _) = await CreateHostAsync(day);
        var customerA = await CreateCustomerSessionAsync();
        var customerB = await CreateCustomerSessionAsync();

        for (var i = 0; i < 60; i++)
        {
            var allowed = await SendAsync(client, ProductViewBody, sessionToken: customerA);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var limitedA = await SendAsync(client, ProductViewBody, sessionToken: customerA);
        var allowedB = await SendAsync(client, ProductViewBody, sessionToken: customerB);

        // Khách đã đăng nhập đi theo `UserId`. Hai khách dùng chung IP không chia nhau hạn mức.
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedA.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, allowedB.StatusCode);
        Assert.Equal(61, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_CustomerOutOfQuota_GuestOnSameIpStillReturns204()
    {
        var day = new DateOnly(2031, 1, 18);
        var (client, _) = await CreateHostAsync(day);
        var customer = await CreateCustomerSessionAsync();

        for (var i = 0; i < 60; i++)
        {
            var allowed = await SendAsync(client, ProductViewBody, sessionToken: customer);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var limitedCustomer = await SendAsync(client, ProductViewBody, sessionToken: customer);
        var guest = await SendAsync(client, ProductViewBody);

        // Khách vãng lai đi theo IP, khách đã đăng nhập đi theo `UserId`. Hai khóa này không dùng chung hạn mức.
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, guest.StatusCode);
        Assert.Equal(61, await ReadCounterAsync(day));
    }

    [Fact]
    public async Task Post_AcrossVietnamMidnight_CountsOnTheVietnamDayOfEachRequest()
    {
        var firstDay = new DateOnly(2031, 3, 10);
        var secondDay = new DateOnly(2031, 3, 11);
        // 16:59:59 UTC là 23:59:59 ngày 10/03 giờ Việt Nam.
        var (client, clock) = await CreateHostAsync(firstDay, new DateTimeOffset(2031, 3, 10, 16, 59, 59, TimeSpan.Zero));
        await ClearCounterAsync(secondDay);

        var beforeMidnight = await SendAsync(client, ProductViewBody);
        var firstDayAfterFirstRequest = await ReadCounterAsync(firstDay);

        // 17:00:00 UTC là 00:00:00 ngày 11/03 giờ Việt Nam.
        clock.SetUtcNow(new DateTimeOffset(2031, 3, 10, 17, 0, 0, TimeSpan.Zero));
        var afterMidnight = await SendAsync(client, ProductViewBody);

        Assert.Equal(HttpStatusCode.NoContent, beforeMidnight.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, afterMidnight.StatusCode);
        Assert.Equal(1, firstDayAfterFirstRequest);
        Assert.Equal(1, await ReadCounterAsync(firstDay));
        Assert.Equal(1, await ReadCounterAsync(secondDay));
    }

    [Fact]
    public async Task HandleAsync_RequestAbortedAfterBodyIsRead_StillIncrementsCounter()
    {
        var day = new DateOnly(2031, 1, 14);
        var (host, _, _) = await CreateHostWithServicesAsync(day);
        using var abort = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.SetActor(new Actor
        {
            AnonymousId = NewAnonymousId(),
            Kind = ActorKind.Guest,
            Roles = [],
            Permissions = [],
            Consent = ConsentState.AllDisabled,
        });
        // Stream hủy token ngay khi body đã đọc xong. Cách này giả lập client ngắt kết nối trước bước ghi bộ đếm.
        context.Request.Body = new AbortAfterBodyStream(Encoding.UTF8.GetBytes(ProductViewBody), abort);
        await using var scope = host.Services.CreateAsyncScope();

        var result = await CollectEventsEndpoints.HandleAsync(
            context,
            scope.ServiceProvider.GetRequiredService<ProductViewRateLimiter>(),
            scope.ServiceProvider.GetRequiredService<DailyCounterWriter>(),
            abort.Token);

        // Body đã đọc trọn trước khi token hủy, nên lượt xem hợp lệ và bộ đếm vẫn phải tăng.
        Assert.True(abort.IsCancellationRequested);
        Assert.IsType<NoContent>(result);
        Assert.Equal(1, await ReadCounterAsync(day));
    }

    // Dựng host riêng với đồng hồ giả. Mỗi host có limiter singleton riêng nên hạn mức không lẫn giữa các test.
    private async Task<(HttpClient Client, FakeTimeProvider Clock)> CreateHostAsync(
        DateOnly day, DateTimeOffset? now = null, bool trustProxy = false)
    {
        var (_, client, clock) = await CreateHostWithServicesAsync(day, now, trustProxy);
        return (client, clock);
    }

    // Trả thêm host để test gọi handler trực tiếp qua `Services`. Các test HTTP chỉ cần client và đồng hồ.
    private async Task<(WebApplicationFactory<Program> Host, HttpClient Client, FakeTimeProvider Clock)> CreateHostWithServicesAsync(
        DateOnly day,
        DateTimeOffset? now = null,
        bool trustProxy = false)
    {
        // Mặc định đặt đồng hồ vào 12:00 giờ Việt Nam (05:00 UTC) để cả 1 phút tiến đồng hồ vẫn nằm trong cùng ngày.
        var startTime = now ?? new DateTimeOffset(day.Year, day.Month, day.Day, 5, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var host = factory.WithWebHostBuilder(builder =>
        {
            // Tin một proxy thì API đọc IP client từ `X-Forwarded-For`. Test giả được nhiều IP khác nhau.
            if (trustProxy)
            {
                builder.UseSetting("TRUST_PROXY", "1");
            }

            builder.ConfigureTestServices(services =>
            {
                // Đồng hồ giả chỉ thay trong host của test, module vẫn đăng ký `TimeProvider.System` ở production.
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        });
        _hosts.Add(host);
        // Xóa dòng cũ của ngày để test chạy lại trên DB đã dùng vẫn bắt đầu từ 0.
        await ClearCounterAsync(day);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return (host, client, clock);
    }

    private static string NewAnonymousId() => Guid.NewGuid().ToString();

    private async Task<string> CreateCustomerSessionAsync()
    {
        var customer = await IdentityTestData.CreateUserAsync(
            database, IdentityTestData.NewEmail("view-customer"), UserKind.Customer, "customer", emailVerified: true);
        return await IdentityTestData.CreateSessionAsync(database, customer.Id);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string rawBody,
        string? anonymousId = null,
        string? sessionToken = null,
        string userAgent = BrowserUserAgent,
        string? clientIp = null)
    {
        var cookie = $"pk_anon={anonymousId ?? NewAnonymousId()}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        using var request = ActorHttp.CreateRequest(HttpMethod.Post, "/api/events", cookie);
        // Request ghi từ trình duyệt luôn có `Origin` của web. Thiếu header này thì actor middleware trả 403.
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        // Chỉ host có `TRUST_PROXY=1` đọc header này. Host mặc định bỏ qua và dùng địa chỉ kết nối.
        if (clientIp is not null)
        {
            request.Headers.Add("X-Forwarded-For", clientIp);
        }

        request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private async Task<long> ReadCounterAsync(DateOnly day)
    {
        await using var context = database.CreateActivityTrackingContext();
        // Không có dòng nghĩa là bộ đếm bằng 0.
        return await context.Database
            .SqlQuery<long>($"SELECT value AS \"Value\" FROM daily_counters WHERE date = {day} AND metric = {ProductViewMetric}")
            .SingleOrDefaultAsync();
    }

    private async Task ClearCounterAsync(DateOnly day)
    {
        await using var context = database.CreateActivityTrackingContext();
        await context.Database.ExecuteSqlAsync($"DELETE FROM daily_counters WHERE date = {day} AND metric = {ProductViewMetric}");
    }

    // Trả body một lần, rồi hủy token ở lần đọc kế tiếp (0 byte). Handler thấy hết body trước khi token bị hủy.
    private sealed class AbortAfterBodyStream(byte[] body, CancellationTokenSource abort) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = body.Length - _position;
            if (remaining == 0)
            {
                // Hết body thì client coi như đã gửi xong và ngắt kết nối.
                abort.Cancel();
                return ValueTask.FromResult(0);
            }

            var count = Math.Min(remaining, buffer.Length);
            body.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
