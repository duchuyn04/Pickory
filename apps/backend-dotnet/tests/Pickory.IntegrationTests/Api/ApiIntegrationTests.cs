using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// Kiểm tra toàn bộ hợp đồng API và hành vi middleware qua WebApplicationFactory.
[Collection(IntegrationDatabaseCollection.Name)]
public class ApiIntegrationTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PickoryWebApplicationFactory _factory;

    public ApiIntegrationTests(PickoryWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    // Endpoint /api/health trả HTTP 200 và cấu trúc dữ liệu JSON đúng như bản Node.
    public async Task GetHealth_TraVeHttp200_VaKetQuaOkGiongBanNode()
    {
        // Gửi yêu cầu GET tới endpoint kiểm tra sức khỏe của dịch vụ.
        var response = await _client.GetAsync("/api/health");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var status = document.RootElement.GetProperty("data").GetProperty("status").GetString();

        Assert.Equal("ok", status);
    }

    [Fact]
    // Gọi sai phương thức HTTP trên route có sẵn phải trả 404 NOT_FOUND giống bản Node.
    public async Task PostHealth_SaiPhuongThucHttp_TraVeHttp404VaMaNotFound()
    {
        // Gửi yêu cầu POST tới endpoint vốn chỉ chấp nhận phương thức GET.
        // Request ghi cần `Origin` của web để qua bước chống CSRF, giống trình duyệt thật.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/health");
        request.Headers.Add("Origin", "http://localhost:5173");
        var response = await _client.SendAsync(request);

        // Khẳng định mã trạng thái là 404 Not Found thay vì 405 rỗng.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal("Không tìm thấy tài nguyên.", error.GetProperty("message").GetString());
    }

    [Fact]
    // Route không tồn tại trả HTTP 404 kèm mã lỗi NOT_FOUND và thông điệp chuẩn.
    public async Task RouteKhongTonTai_TraVeHttp404_VoiMaNotFound()
    {
        // Gửi yêu cầu tới đường dẫn không tồn tại trong hệ thống.
        var response = await _client.GetAsync("/api/route-khong-ton-tai");

        // Khẳng định mã trạng thái là 404 Not Found.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal("Không tìm thấy tài nguyên.", error.GetProperty("message").GetString());
    }

    [Fact]
    // Lỗi ném trong pipeline của app phải thành JSON ở Development. Trang lỗi dev từng trả HTML 500 thay cho JSON.
    public async Task LoiTrongPipelineCuaApp_OMoiTruongDevelopment_TraJsonKhongTraTrangLoiDev()
    {
        var environment = (IHostEnvironment)_factory.Services.GetService(typeof(IHostEnvironment))!;
        Assert.True(environment.IsDevelopment());

        // Actor middleware nằm trong pipeline của app. Request ghi từ nguồn lạ làm middleware ném `AppException`.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/health");
        request.Headers.Add("Origin", "https://malicious-site.com");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN_ORIGIN", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Lỗi nghiệp vụ trả đúng mã HTTP và cấu trúc error code cùng message tiếng Việt.
    public async Task LoiNghiepVu_TraVeDungHttpCode_VaCauTrucErrorJson()
    {
        // Gửi yêu cầu tới endpoint kích hoạt ngoại lệ nghiệp vụ mẫu.
        var response = await _client.GetAsync("/api/test/business-error");

        // Khẳng định mã trạng thái do ngoại lệ chỉ định là 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("SAMPLE_BUSINESS_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("Thao tác không hợp lệ.", error.GetProperty("message").GetString());
    }

    [Fact]
    // Lỗi xác thực trả HTTP 400 kèm mã VALIDATION_ERROR và chuỗi mô tả đúng định dạng.
    public async Task LoiXacThuc_TraVeHttp400_VaMaValidationErrorTheoMau()
    {
        // Gửi yêu cầu tới endpoint kích hoạt ngoại lệ xác thực FluentValidation.
        var response = await _client.GetAsync("/api/test/validation-error");

        // Khẳng định mã trạng thái trả về là 400 Bad Request.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());

        var message = error.GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.StartsWith("Dữ liệu không hợp lệ (", message);
        Assert.Contains("specs.brand: Thương hiệu không hợp lệ", message);
        Assert.Contains("body: Nội dung yêu cầu bắt buộc", message);
    }

    [Fact]
    // Lỗi không lường trước trả HTTP 500 kèm thông điệp chung và không để lộ stack trace.
    public async Task LoiKhongLuongTruoc_TraVeHttp500_VaKhongDeLoStackTrace()
    {
        // Gửi yêu cầu tới endpoint ném ra ngoại lệ máy chủ không lường trước.
        var response = await _client.GetAsync("/api/test/unhandled-error");

        // Khẳng định mã trạng thái trả về là 500 Internal Server Error.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("Đã có lỗi xảy ra, vui lòng thử lại sau.", error.GetProperty("message").GetString());

        // Khẳng định phản hồi không chứa chi tiết hay chuỗi bí mật của ngoại lệ nội bộ.
        Assert.DoesNotContain("Bí mật nội bộ", body);
        Assert.DoesNotContain("StackTrace", body);
    }

    [Fact]
    // Thuộc tính thời gian DateTime được xuất theo chuẩn ISO 8601 UTC và camelCase.
    public async Task DateTime_XuatChuanIso8601Utc_VaThuocTinhCamelCase()
    {
        // Gửi yêu cầu tới endpoint trả về đối tượng có trường thời gian.
        var response = await _client.GetAsync("/api/test/datetime");

        // Khẳng định mã trạng thái 200 và định dạng chuỗi đúng quy ước.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        var timestamp = data.GetProperty("timestamp").GetString();
        Assert.Equal("2026-10-05T07:00:00.000Z", timestamp);
    }

    [Fact]
    // Yêu cầu có kích thước body vượt quá 1 MB bị từ chối với HTTP 413 PAYLOAD_TOO_LARGE.
    public async Task BodyVuotQua1MB_TraVeHttp413_VaMaPayloadTooLarge()
    {
        // Tạo chuỗi nội dung vượt ngưỡng 1 MB (xấp xỉ 1.2 MB).
        var largeContent = new string('x', 1200 * 1024);
        var requestContent = new StringContent(largeContent, Encoding.UTF8, "text/plain");

        // Gửi yêu cầu POST chứa dữ liệu vượt kích thước quy định.
        var response = await _client.PostAsync("/api/test/echo-body", requestContent);

        // Khẳng định mã trạng thái trả về là 413 Payload Too Large.
        Assert.Equal((HttpStatusCode)413, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("PAYLOAD_TOO_LARGE", error.GetProperty("code").GetString());
        Assert.Equal("Kích thước dữ liệu vượt quá giới hạn 1 MB cho phép.", error.GetProperty("message").GetString());
    }

    [Fact]
    // Kiểm tra cấu hình ứng dụng trong môi trường test sử dụng cơ sở dữ liệu test riêng.
    public void CauHinh_SuDungCoSoDuLieuTestRieng_KhongDungDatabaseDev()
    {
        // Lấy đối tượng cấu hình đã được đăng ký trong container dịch vụ của ứng dụng test.
        var config = _factory.Services.GetService(typeof(AppConfig)) as AppConfig;

        // Khẳng định đường dẫn cơ sở dữ liệu trỏ tới cơ sở dữ liệu pickory_test.
        Assert.NotNull(config);
        Assert.Contains("pickory_test", config.DatabaseUrl);
    }
}
