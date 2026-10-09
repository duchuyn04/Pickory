namespace Pickory.Modules.Identity.Features.ForgotPassword;

// Body chỉ có email. API không cho biết email này có tài khoản hay không.
public sealed record ForgotPasswordRequest(string? Email);
