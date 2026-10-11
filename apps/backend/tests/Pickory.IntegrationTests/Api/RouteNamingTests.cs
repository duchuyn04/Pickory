using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// Luật 20 của plan REST: route đặt tên theo tài nguyên, không theo hành động. Test này giữ luật cho mọi module, kể cả module mới.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class RouteNamingTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>
{
    // Mỗi mục là một ngoại lệ có lý do. Không thêm route `commerce` tương lai vào đây, route đó phải đặt tên theo tài nguyên.
    private static readonly IReadOnlyDictionary<string, string> AllowedActionRoutes = new Dictionary<string, string>
    {
        // Chuyển hướng trình duyệt sang Google, không phải lời gọi API JSON (quyết định 6 của plan REST).
        ["GET /api/auth/google/start"] = "Chuyển hướng trình duyệt sang Google.",
        // Địa chỉ này đã đăng ký với Google. Đổi route buộc đổi cấu hình ngoài repo (quyết định 6 của plan REST).
        ["GET /api/auth/google/callback"] = "Địa chỉ redirect đã đăng ký với Google.",
    };

    [Fact]
    public void MappedRoutes_DoNotUseActionVerbSegments()
    {
        // Danh sách route lấy từ `EndpointDataSource` của app, không đọc mã nguồn, để route mới tự bị kiểm.
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var violations = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"];
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;

            var verbSegment = ActionRouteRule.FindActionSegment(pattern);
            if (verbSegment is null) continue;

            foreach (var method in methods)
            {
                // Chỉ route nằm trong danh sách ngoại lệ được giữ lại. Mọi route khác bị liệt kê để sửa.
                var routeLabel = $"{method} {pattern}";
                if (AllowedActionRoutes.ContainsKey(routeLabel)) continue;
                violations.Add($"{routeLabel} (đoạn '{verbSegment}')");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Route đặt tên theo hành động. Đổi sang tài nguyên:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AllowedActionRoutes_AreStillMapped()
    {
        // Ngoại lệ không còn route thật là ngoại lệ chết. Xóa nó để danh sách luôn phản ánh code.
        var mappedLabels = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .ToHashSet();

        var deadExceptions = AllowedActionRoutes.Keys.Where(label => !mappedLabels.Contains(label)).ToList();

        Assert.True(deadExceptions.Count == 0, "Ngoại lệ không còn route: " + string.Join(", ", deadExceptions));
    }
}

// Logic thuần, không cần DB hay HTTP. Tách riêng để unit test chứng minh phép so khớp theo đoạn, không theo chuỗi con.
public static class ActionRouteRule
{
    private static readonly string[] ActionVerbs =
    [
        "login", "logout", "register", "lock", "unlock", "adjust-stock", "cancel", "confirm",
        "verify-email", "forgot-password", "reset-password", "match", "hide", "show", "report", "start", "callback",
    ];

    public static string? FindActionSegment(string routePattern)
    {
        // Tách theo `/` và so từng đoạn nguyên vẹn. So chuỗi con làm `blocklist` bị nhận nhầm là `lock`.
        var segments = routePattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.FirstOrDefault(segment => ActionVerbs.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class ActionRouteRuleTests
{
    [Theory]
    [InlineData("/api/admin/customers/{id}/lock", "lock")]
    [InlineData("/api/orders/{code}/cancel", "cancel")]
    [InlineData("/api/auth/login", "login")]
    [InlineData("/api/admin/products/{id}/adjust-stock", "adjust-stock")]
    [InlineData("/api/auth/LOGOUT", "LOGOUT")]
    [InlineData("/api/auth/google/start", "start")]
    public void FindActionSegment_ReturnsVerbSegment(string pattern, string expectedSegment)
    {
        Assert.Equal(expectedSegment, ActionRouteRule.FindActionSegment(pattern));
    }

    [Theory]
    [InlineData("/api/admin/blocklist")]
    [InlineData("/api/admin/cancellations")]
    [InlineData("/api/admin/customers/{id}")]
    [InlineData("/api/sessions")]
    public void FindActionSegment_ReturnsNull_WhenNoSegmentIsAVerb(string pattern)
    {
        Assert.Null(ActionRouteRule.FindActionSegment(pattern));
    }
}
