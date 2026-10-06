using Pickory.Modules.Identity.Passwords;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Passwords;

// Kiểm tra hash Argon2id tương thích hai chiều với bản Node. Người dùng cũ không phải đặt lại mật khẩu.
public class PasswordHasherTests
{
    // `@node-rs/argon2` 2.2.1 của bản Node tạo hash này cho mật khẩu `MatKhauCu@2026`.
    private const string NodeHash = "$argon2id$v=19$m=19456,t=2,p=1$TVVsomMIC0TkoRrkfUsmRA$S2YF1A+J4lsR7ZPzxUwbauXFhAueJ42gCYarSKBJwfU";

    [Fact]
    // Hash do bản Node tạo vẫn kiểm tra được. Người dùng cũ đăng nhập bằng mật khẩu cũ.
    public void Verify_AcceptsHashCreatedByNodeBackend()
    {
        Assert.True(PasswordHasher.Verify(NodeHash, "MatKhauCu@2026"));
    }

    [Fact]
    // Mật khẩu sai một ký tự không khớp hash của bản Node.
    public void Verify_RejectsWrongPasswordForNodeHash()
    {
        Assert.False(PasswordHasher.Verify(NodeHash, "MatKhauCu@2027"));
    }

    [Fact]
    // Hash mới dùng tham số Argon2id giống bản Node. Mọi hash trong DB vì vậy có cùng chi phí băm.
    public void Hash_UsesNodeArgon2idParametersAndVerifies()
    {
        var hash = PasswordHasher.Hash("MatKhauMoi@2026");

        // Tiền tố chứa thuật toán, phiên bản và tham số chi phí.
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash);
        // Hash khớp mật khẩu gốc và không khớp mật khẩu khác.
        Assert.True(PasswordHasher.Verify(hash, "MatKhauMoi@2026"));
        Assert.False(PasswordHasher.Verify(hash, "MatKhauKhac@2026"));
    }

    [Fact]
    // Cùng mật khẩu cho hai hash khác nhau nhờ salt ngẫu nhiên. Kẻ đọc được DB không biết ai dùng chung mật khẩu.
    public void Hash_UsesRandomSaltForSamePassword()
    {
        Assert.NotEqual(PasswordHasher.Hash("CungMatKhau@1"), PasswordHasher.Hash("CungMatKhau@1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong-phai-hash")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$hong")]
    // Hash hỏng trả false thay vì ném lỗi. Lỗi nội bộ không lộ ra cho người đăng nhập.
    public void Verify_ReturnsFalseForMalformedHash(string malformedHash)
    {
        Assert.False(PasswordHasher.Verify(malformedHash, "MatKhauCu@2026"));
    }

    [Fact]
    // `DummyHash` không khớp mật khẩu nào, nên nhánh không có tài khoản không bao giờ đăng nhập được.
    public void Verify_DummyHashNeverMatches()
    {
        // Hash giả chỉ dùng để làm chậm nhánh không có tài khoản. Hash này không được khớp mật khẩu nào.
        Assert.False(PasswordHasher.Verify(PasswordHasher.DummyHash, "MatKhauCu@2026"));
    }
}
