namespace Pickory.Modules.Identity.Features.ChangePassword;

// `CurrentPassword` có thể thiếu. Tài khoản chỉ dùng Google chưa có mật khẩu cũ để nhập.
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
