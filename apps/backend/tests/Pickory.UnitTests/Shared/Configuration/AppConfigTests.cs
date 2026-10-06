using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// Kiểm tra toàn diện hành vi nạp cấu hình và xác thực dữ liệu đầu vào.
public class AppConfigTests
{
    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    // Nạp đầy đủ thông số cấu hình hợp lệ khi cung cấp đầy đủ các biến.
    public void Load_DocThanhCongCauHinh_KhiDuLieuHopLe()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["PORT"] = "3001",
            ["WEB_ORIGIN"] = "http://localhost:5174",
            ["API_BASE_URL"] = "http://localhost:3001",
            ["TRUST_PROXY"] = "1",
        });

        var result = AppConfig.Load(config);

        Assert.Equal(3001, result.Port);
        Assert.Equal("http://localhost:5174", result.WebOrigin);
        Assert.Equal("http://localhost:3001", result.ApiBaseUrl);
        Assert.Equal("postgresql://pickory:pickory@localhost:5432/pickory", result.DatabaseUrl);
        Assert.Equal("1", result.TrustProxy);
    }

    [Fact]
    // Thiếu thông tin Google thì dùng giá trị giả của bản Node. Môi trường dev khởi động được khi chưa có ứng dụng Google thật.
    public void Load_GoogleCredentialsUseNodeDefaultsWhenMissing()
    {
        // Cấu hình chỉ có biến bắt buộc `DATABASE_URL`.
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
        });

        var result = AppConfig.Load(config);

        // Hai giá trị giả khớp bản Node, nên test tích hợp đọc được `client_id` cố định.
        Assert.Equal("mock_google_client_id", result.GoogleClientId);
        Assert.Equal("mock_google_client_secret", result.GoogleClientSecret);
    }

    [Fact]
    // Biến môi trường Google ghi đè giá trị giả. Production dùng ứng dụng Google thật.
    public void Load_GoogleCredentialsReadFromEnvironment()
    {
        // Đặt `GOOGLE_CLIENT_ID` và `GOOGLE_CLIENT_SECRET` thật.
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["GOOGLE_CLIENT_ID"] = "real-client-id",
            ["GOOGLE_CLIENT_SECRET"] = "real-client-secret",
        });

        var result = AppConfig.Load(config);

        // Cấu hình nhận đúng hai giá trị từ môi trường.
        Assert.Equal("real-client-id", result.GoogleClientId);
        Assert.Equal("real-client-secret", result.GoogleClientSecret);
    }

    [Fact]
    // Dừng khởi động ứng dụng và ném lỗi rõ ràng khi thiếu biến DATABASE_URL.
    public void Load_NemNgoaiLe_KhiThieuBienDatabaseUrl()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["PORT"] = "3000",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(config));
        Assert.Contains("DATABASE_URL", exception.Message);
    }

    [Theory]
    [InlineData("khong-phai-so")]
    [InlineData("0")]
    [InlineData("-5")]
    // Ném ngoại lệ khi biến PORT sai định dạng hoặc không phải là số nguyên dương.
    public void Load_NemNgoaiLe_KhiPortKhongHopLe(string invalidPort)
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["PORT"] = invalidPort,
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(config));
        Assert.Contains("PORT", exception.Message);
    }

    [Theory]
    [InlineData("dia-chi-sai")]
    [InlineData("/duong-dan-tuong-doi")]
    // Ném ngoại lệ khi biến WEB_ORIGIN không phải là một địa chỉ URL tuyệt đối.
    public void Load_NemNgoaiLe_KhiWebOriginKhongPhaiUrlTuyetDoi(string invalidOrigin)
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["WEB_ORIGIN"] = invalidOrigin,
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(config));
        Assert.Contains("WEB_ORIGIN", exception.Message);
    }

    [Theory]
    [InlineData("khong-hop-le")]
    [InlineData("ftp://api.pickory.local")]
    // Ném ngoại lệ khi biến API_BASE_URL không phải là một URL giao thức HTTP tuyệt đối.
    public void Load_NemNgoaiLe_KhiApiBaseUrlKhongPhaiUrlTuyetDoi(string invalidBaseUrl)
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["API_BASE_URL"] = invalidBaseUrl,
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(config));
        Assert.Contains("API_BASE_URL", exception.Message);
    }

    [Fact]
    // Ném ngoại lệ khi TRUST_PROXY nhận giá trị true để tránh rò rỉ tin cậy proxy giả.
    public void Load_NemNgoaiLe_KhiTrustProxyLaTrue()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["TRUST_PROXY"] = "true",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(config));
        Assert.Contains("TRUST_PROXY", exception.Message);
    }

    [Theory]
    [InlineData("0", "false")]
    [InlineData("false", "false")]
    [InlineData("", "false")]
    [InlineData("2", "2")]
    [InlineData("10.0.0.1, 192.168.1.1", "10.0.0.1,192.168.1.1")]
    // Thiết lập cấu hình trust proxy chính xác theo các trường hợp quy định.
    public void Load_ThietLapDungTrustProxy_TheoCacMauGiaTri(string rawValue, string expected)
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["TRUST_PROXY"] = rawValue,
        });

        var result = AppConfig.Load(config);
        Assert.Equal(expected, result.TrustProxy);
    }

    [Fact]
    // Ném ngoại lệ khi số hop của TRUST_PROXY là số âm.
    public void Load_NemNgoaiLe_KhiTrustProxyLaSoAm()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["TRUST_PROXY"] = "-1",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(config));
        Assert.Contains("TRUST_PROXY", exception.Message);
    }
}
