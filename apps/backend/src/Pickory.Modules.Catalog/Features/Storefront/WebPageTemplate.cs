namespace Pickory.Modules.Catalog.Features.Storefront;

// Template `index.html` của SPA, đọc một lần khi khởi động. Bản build của web không đổi khi API đang chạy.
public sealed record WebPageTemplate(string Html, string WebOrigin)
{
    // Đường dẫn sai là lỗi cấu hình, nên dừng khởi động với thông báo rõ thay vì trả trang lỗi cho khách.
    public static WebPageTemplate Load(string path, string webOrigin)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"WEB_INDEX_HTML_PATH trỏ tới file không tồn tại: {path}");
        }

        return new WebPageTemplate(File.ReadAllText(path), webOrigin);
    }
}
