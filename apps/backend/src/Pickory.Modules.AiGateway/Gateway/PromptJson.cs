using System.Text.Encodings.Web;
using System.Text.Json;

namespace Pickory.Modules.AiGateway.Gateway;

// Tùy chọn JSON cho dữ liệu đưa vào prompt.
public static class PromptJson
{
    // Giữ nguyên chữ tiếng Việt thay vì mã `\u`. Model đọc đúng text và token không tăng vô ích.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
