using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Pickory.Api;

// Chế độ của trang tài liệu. Chế độ quyết định người đọc có gửi được request từ Swagger UI hay không.
public enum ApiDocumentationMode
{
    // Development: có nút "Try it out" để dev gọi thử API qua proxy của Vite.
    Interactive,

    // Môi trường khác: người đọc chỉ xem route và schema. Trang không có nút gửi request.
    ReadOnly,
}

// Gom tên và đường dẫn của tài liệu API. Test và `Program.cs` dùng chung một nguồn.
public static class ApiDocumentation
{
    public const string DocumentName = "v1";
    public const string Title = "Pickory API";
    public const string DocumentPath = "/api/openapi/v1.json";
    public const string UiRoutePrefix = "api/docs";

    // Trả null khi không gắn tài liệu. Môi trường khác Development chỉ có tài liệu khi người triển khai bật `API_DOCS_ENABLED`.
    public static ApiDocumentationMode? SelectMode(bool isDevelopment, bool apiDocsEnabled)
    {
        if (isDevelopment)
        {
            return ApiDocumentationMode.Interactive;
        }

        if (apiDocsEnabled)
        {
            return ApiDocumentationMode.ReadOnly;
        }

        return null;
    }

    public static void Map(WebApplication app, ApiDocumentationMode mode)
    {
        // Tài liệu JSON nằm dưới `/api` để proxy của Vite chuyển tiếp. Trang Swagger mở qua cổng web sẽ gửi `Origin` của web.
        app.MapOpenApi("/api/openapi/{documentName}.json");

        // Giao diện đọc tài liệu ở trên. Đường dẫn `/api/docs` cũng đi qua proxy của Vite.
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = UiRoutePrefix;
            options.DocumentTitle = Title;
            options.SwaggerEndpoint(DocumentPath, Title);

            // Danh sách phương thức rỗng ẩn nút "Try it out". Người xem tài liệu production không gửi request từ trang docs.
            if (mode == ApiDocumentationMode.ReadOnly)
            {
                options.SupportedSubmitMethods(Array.Empty<SubmitMethod>());
            }
        });
    }
}
