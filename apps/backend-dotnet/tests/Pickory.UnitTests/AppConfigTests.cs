using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests;

// Kiểm tra hành vi nạp cấu hình và xác thực biến môi trường bắt buộc.
public class AppConfigTests
{
    [Fact]
    // Nạp đầy đủ thông số cấu hình và gán giá trị mặc định cho biến tùy chọn.
    public void Load_DocThanhCongCauHinh_KhiCoBienDatabaseUrl()
    {
        // Khởi tạo bộ dữ liệu cấu hình mô phỏng môi trường thực tế.
        var memoryConfig = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["PORT"] = "3001",
            ["WEB_ORIGIN"] = "http://localhost:5174",
            ["API_BASE_URL"] = "http://localhost:3001",
            ["TRUST_PROXY"] = "1",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(memoryConfig)
            .Build();

        // Nạp cấu hình từ nguồn tham số vào đối tượng AppConfig.
        var config = AppConfig.Load(configuration);

        // Khẳng định toàn bộ thuộc tính được gán đúng giá trị tương ứng.
        Assert.Equal(3001, config.Port);
        Assert.Equal("http://localhost:5174", config.WebOrigin);
        Assert.Equal("http://localhost:3001", config.ApiBaseUrl);
        Assert.Equal("postgresql://pickory:pickory@localhost:5432/pickory", config.DatabaseUrl);
        Assert.Equal("1", config.TrustProxy);
    }

    [Fact]
    // Dừng khởi động ứng dụng và ném lỗi rõ ràng khi thiếu biến DATABASE_URL.
    public void Load_NemNgoaiLeRoRang_KhiThieuBienDatabaseUrl()
    {
        // Khởi tạo nguồn cấu hình không chứa biến DATABASE_URL bắt buộc.
        var emptyConfig = new Dictionary<string, string?>
        {
            ["PORT"] = "3000",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(emptyConfig)
            .Build();

        // Khẳng định phương thức ném ngoại lệ InvalidOperationException với thông điệp rõ.
        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(configuration));
        Assert.Contains("DATABASE_URL", exception.Message);
    }
}
