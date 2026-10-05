using System.Text.Json;

namespace Pickory.Shared.Json;

// Thiết lập cấu hình mặc định cho việc tuần tự hóa JSON của toàn bộ ứng dụng.
public static class PickoryJsonDefaults
{
    // Cung cấp tùy chọn cấu hình JSON dùng chung trong API và kiểm thử.
    public static readonly JsonSerializerOptions Options = CreateOptions();

    // Áp dụng quy tắc camelCase và định dạng thời gian ISO 8601 UTC.
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new DateTimeUtcJsonConverter());
        options.Converters.Add(new DateTimeOffsetUtcJsonConverter());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }
}
