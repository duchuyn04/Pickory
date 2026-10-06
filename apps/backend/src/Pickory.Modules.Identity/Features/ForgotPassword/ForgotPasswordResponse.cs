namespace Pickory.Modules.Identity.Features.ForgotPassword;

// Mọi request hợp lệ nhận cùng `message`. Response không tiết lộ email nào đã đăng ký.
public sealed record ForgotPasswordResponse(string Message);
