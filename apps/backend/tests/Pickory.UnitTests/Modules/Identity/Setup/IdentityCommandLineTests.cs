using Pickory.Modules.Identity.Setup;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Setup;

// Đọc tham số của lệnh tạo admin giống `scripts/admin-create.ts` của bản Node.
public class IdentityCommandLineTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoEnvironment = new Dictionary<string, string?>();

    [Theory]
    [InlineData("identity:seed", true)]
    [InlineData("identity:create-admin", true)]
    [InlineData("--urls", false)]
    // Chỉ hai lệnh này chạy rồi thoát. Tham số khác vẫn khởi động web API như bình thường.
    public void IsCommand_RecognizesOnlyIdentityCommands(string firstArgument, bool expected)
    {
        Assert.Equal(expected, IdentityCommandLine.IsCommand([firstArgument]));
    }

    [Fact]
    // Tham số dòng lệnh thắng biến môi trường. Cả hai dạng `--email value` và `--email=value` đều được nhận.
    public void ReadAdminOptions_PrefersArgumentsOverEnvironment()
    {
        var environment = new Dictionary<string, string?> { ["ADMIN_EMAIL"] = "env@pickory.local", ["ADMIN_PASSWORD"] = "EnvPassword1!" };

        var options = IdentityCommandLine.ReadAdminOptions(
            ["identity:create-admin", "--email", "cli@pickory.local", "--password=CliPassword1!"], environment, isProduction: true);

        Assert.Equal("cli@pickory.local", options.Email);
        Assert.Equal("CliPassword1!", options.Password);
        Assert.Equal("Quản trị viên", options.Name);
    }

    [Fact]
    // Môi trường dev có email và mật khẩu mặc định của bản Node. Người dev tạo admin không cần gõ tham số.
    public void ReadAdminOptions_UsesDevelopmentDefaultsOutsideProduction()
    {
        var options = IdentityCommandLine.ReadAdminOptions(["identity:create-admin"], NoEnvironment, isProduction: false);

        Assert.Equal("admin@pickory.local", options.Email);
        Assert.Equal("Admin@123456", options.Password);
    }

    [Fact]
    // Production không có giá trị mặc định. Lệnh dừng khi thiếu email, không tạo admin với mật khẩu ai cũng biết.
    public void ReadAdminOptions_RequiresEmailInProduction()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            IdentityCommandLine.ReadAdminOptions(["identity:create-admin"], NoEnvironment, isProduction: true));

        Assert.Equal("Vui lòng cung cấp email admin qua --email hoặc ADMIN_EMAIL", exception.Message);
    }
}
