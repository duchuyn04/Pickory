using Pickory.Api.E2e;
using Xunit;

namespace Pickory.UnitTests.Api.E2e;

// Lệnh `e2e:start` xóa và dựng lại schema. Lệnh chỉ được chạy trên DB E2E riêng, giống `scripts/e2e-start.ts` của bản Node.
public class E2eTargetTests
{
    private const string E2eDatabaseUrl = "postgresql://pickory:pickory@localhost:5432/pickory_e2e";

    [Fact]
    // DB tên `pickory_e2e` và hai biến môi trường trùng nhau thì lệnh được chạy.
    public void Read_AcceptsDedicatedE2eDatabase()
    {
        var target = E2eTarget.Read(E2eDatabaseUrl, E2eDatabaseUrl, "e2e-identity-admin@pickory.local", "MatKhauE2E@123");

        Assert.Equal("e2e-identity-admin@pickory.local", target.AdminEmail);
        Assert.Equal("MatKhauE2E@123", target.AdminPassword);
    }

    [Theory]
    [InlineData("postgresql://pickory:pickory@localhost:5432/pickory", "postgresql://pickory:pickory@localhost:5432/pickory")]
    [InlineData(E2eDatabaseUrl, "postgresql://pickory:pickory@localhost:5432/pickory_e2e_khac")]
    [InlineData(E2eDatabaseUrl, null)]
    // DB khác tên hoặc thiếu `E2E_DATABASE_URL` có thể là DB dev. Lệnh dừng trước khi xóa dữ liệu.
    public void Read_RejectsOtherDatabases(string databaseUrl, string? e2eDatabaseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            E2eTarget.Read(databaseUrl, e2eDatabaseUrl, "e2e-identity-admin@pickory.local", "MatKhauE2E@123"));

        Assert.Equal("E2E chỉ được migrate và reset DB pickory_e2e đã được cấu hình riêng.", exception.Message);
    }

    [Theory]
    [InlineData(null, "MatKhauE2E@123")]
    [InlineData("e2e-identity-admin@pickory.local", "ngan")]
    // Admin fixture chỉ dùng thông tin do Playwright cấp. Lệnh không dùng admin của môi trường dev.
    public void Read_RequiresAdminFixtureCredentials(string? adminEmail, string? adminPassword)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            E2eTarget.Read(E2eDatabaseUrl, E2eDatabaseUrl, adminEmail, adminPassword));

        Assert.Equal("Thiếu email hoặc mật khẩu admin fixture E2E hợp lệ.", exception.Message);
    }
}
