namespace Pickory.Modules.Identity.GoogleOAuth;

// `Sub` là mã cố định của Google, không đổi khi người dùng đổi email.
// `EmailVerified` quyết định tài khoản có được tự liên kết với tài khoản sẵn có hay không.
public sealed record GoogleUserInfo(string Sub, string Email, bool EmailVerified, string? Name);
