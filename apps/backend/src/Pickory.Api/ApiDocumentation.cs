using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;

namespace Pickory.Api;

// Gom tên và đường dẫn của tài liệu API. Test và `Program.cs` dùng chung một nguồn.
public static class ApiDocumentation
{
    public const string DocumentName = "v1";
    public const string Title = "Pickory API";
    public const string DocumentPath = "/api/openapi/v1.json";
    public const string UiRoutePrefix = "api/docs";

    public static void Map(WebApplication app)
    {
        // Tài liệu JSON nằm dưới `/api` để proxy của Vite chuyển tiếp. Trang Swagger mở qua cổng web sẽ gửi `Origin` của web.
        app.MapOpenApi("/api/openapi/{documentName}.json");

        // Giao diện đọc tài liệu ở trên. Đường dẫn `/api/docs` cũng đi qua proxy của Vite.
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = UiRoutePrefix;
            options.DocumentTitle = Title;
            options.SwaggerEndpoint(DocumentPath, Title);
        });
    }
}
