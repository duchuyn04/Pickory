namespace Pickory.Modules.Identity.Features.DeleteAccount;

// `Password` có thể thiếu. Tài khoản chỉ dùng Google xác nhận bằng lần đăng nhập Google gần đây.
public sealed record DeleteAccountRequest(string? Password);
