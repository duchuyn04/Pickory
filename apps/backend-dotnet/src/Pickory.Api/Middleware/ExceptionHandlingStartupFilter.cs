using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Pickory.Api.Middleware;

// Bộ lọc khởi động bảo đảm ExceptionHandlingMiddleware luôn bao bọc toàn bộ pipeline.
public class ExceptionHandlingStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Đăng ký middleware bắt lỗi toàn cục ở lớp ngoài cùng của toàn bộ ứng dụng.
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            next(app);
        };
    }
}
