namespace Pickory.Modules.Identity.Features.ResetPassword;

// `UserKind` cho giao diện biết cần đưa người dùng tới trang đăng nhập của khách hay của nhân viên.
public sealed record ResetPasswordResponse(bool Success, string UserKind, string Message);
