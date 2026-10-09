namespace Pickory.Modules.Identity.Features.ResetPassword;

// `Token` đến từ link quên mật khẩu hoặc link mời nhân viên.
public sealed record ResetPasswordRequest(string? Token, string? NewPassword);
