using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// Tài liệu OpenAPI và Swagger UI chỉ có ở Development. Test này kiểm tài liệu đủ route, đủ tag và tắt ở Production.
[Collection(IntegrationDatabaseCollection.Name)]
public class OpenApiDocumentationTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>
{
    private const string DocumentPath = "/api/openapi/v1.json";
    private const string DocsPath = "/api/docs";

    private static readonly string[] AllowedTags = ["System", "Identity", "AI Gateway", "Catalog", "Shipping"];

    private async Task<JsonDocument> ReadDocumentAsync()
    {
        var response = await factory.CreateClient().GetAsync(DocumentPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Document_InDevelopment_ReturnsOpenApiJsonWithTitleAndVersion()
    {
        using var document = await ReadDocumentAsync();

        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        var info = document.RootElement.GetProperty("info");
        Assert.Equal("Pickory API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Document_UsesRelativeServerUrl_SoTryItOutKeepsTheWebOrigin()
    {
        using var document = await ReadDocumentAsync();

        // Địa chỉ tuyệt đối làm trình duyệt gọi thẳng cổng API và bị chặn CORS. Địa chỉ tương đối giữ origin của trang web.
        var serverUrls = document.RootElement.GetProperty("servers").EnumerateArray()
            .Select(server => server.GetProperty("url").GetString())
            .ToArray();
        Assert.Equal(new[] { "/" }, serverUrls);
    }

    [Fact]
    public async Task Document_ContainsEveryMappedApiRoute()
    {
        using var document = await ReadDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        // Danh sách route lấy từ `EndpointDataSource` của app, không viết tay, để route mới tự bị kiểm.
        var missingRoutes = new List<string>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
            if (IsExcludedFromDocument(pattern)) continue;

            var openApiPath = ToOpenApiPath(pattern);
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods)
            {
                var documented = paths.TryGetProperty(openApiPath, out var pathItem)
                    && pathItem.TryGetProperty(method.ToLowerInvariant(), out _);
                if (!documented) missingRoutes.Add($"{method} {pattern}");
            }
        }

        Assert.Empty(missingRoutes);
    }

    [Fact]
    public async Task Document_GivesEveryOperationExactlyOneKnownTag()
    {
        using var document = await ReadDocumentAsync();

        var invalidOperations = new List<string>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var tags = operation.Value.TryGetProperty("tags", out var tagArray)
                    ? tagArray.EnumerateArray().Select(tag => tag.GetString()).ToArray()
                    : [];
                if (tags.Length != 1 || !AllowedTags.Contains(tags[0]))
                {
                    invalidOperations.Add($"{operation.Name.ToUpperInvariant()} {path.Name}: [{string.Join(", ", tags)}]");
                }
            }
        }

        Assert.Empty(invalidOperations);
    }

    [Fact]
    public async Task Document_TagsRoutesByModule()
    {
        using var document = await ReadDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal("System", FirstTag(paths, "/api/health", "get"));
        Assert.Equal("Identity", FirstTag(paths, "/api/me", "get"));
        Assert.Equal("Catalog", FirstTag(paths, "/api/products", "get"));
        Assert.Equal("AI Gateway", FirstTag(paths, "/api/admin/ai/usage", "get"));
        Assert.Equal("Shipping", FirstTag(paths, "/api/addresses/provinces", "get"));
    }

    [Fact]
    public async Task Docs_InDevelopment_ReturnsSwaggerUiPointingAtTheDocument()
    {
        var response = await factory.CreateClient().GetAsync(DocsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger-ui", html);
        // Trang HTML nạp cấu hình từ `index.js`. Địa chỉ tài liệu nằm trong tệp này.
        var configuration = await factory.CreateClient().GetStringAsync($"{DocsPath}/index.js");
        Assert.Contains(DocumentPath, configuration);
    }

    [Theory]
    [InlineData(DocumentPath)]
    [InlineData(DocsPath)]
    [InlineData("/api/docs/index.html")]
    public async Task Documentation_InProduction_Returns404(string path)
    {
        await using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Production dừng khi thiếu khóa Cloudinary. Giá trị giả đủ để API khởi động, vì test thay kho ảnh bằng bản giả.
            builder.UseSetting("CLOUDINARY_CLOUD_NAME", "test-cloud");
            builder.UseSetting("CLOUDINARY_API_KEY", "test-key");
            builder.UseSetting("CLOUDINARY_API_SECRET", "test-secret");
            // Production cũng dừng khi thiếu biến GHN. Giá trị giả đủ để API khởi động, vì test không gọi GHN.
            builder.UseSetting("GHN_TOKEN", "test-ghn-token");
            builder.UseSetting("GHN_SHOP_ID", "test-ghn-shop");
            builder.UseSetting("GHN_WEBHOOK_SECRET", "test-ghn-webhook-secret");
            // Production cũng dừng khi thiếu khóa SePay. Giá trị giả đủ để API khởi động, vì test không nhận webhook thật.
            builder.UseSetting("SEPAY_WEBHOOK_API_KEY", "test-sepay-api-key");
        });

        var response = await productionFactory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Route tài liệu và hai route trang SEO trả HTML, nên không thuộc tài liệu API.
    private static bool IsExcludedFromDocument(string pattern) =>
        pattern.StartsWith("/api/openapi", StringComparison.Ordinal)
        || pattern.StartsWith("/api/docs", StringComparison.Ordinal)
        || pattern.StartsWith("/p/", StringComparison.Ordinal)
        || pattern.StartsWith("/c/", StringComparison.Ordinal);

    // OpenAPI ghi tham số đường dẫn không kèm ràng buộc. Mẫu `{id:guid}` thành `{id}`.
    private static string ToOpenApiPath(string pattern) =>
        Regex.Replace(pattern, @"\{(\*?)([^}:?]+)[^}]*\}", "{$2}");

    private static string? FirstTag(JsonElement paths, string path, string method) =>
        paths.GetProperty(path).GetProperty(method).GetProperty("tags")[0].GetString();
}
