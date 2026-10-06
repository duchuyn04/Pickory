namespace Pickory.Modules.Identity.Features.Login;

// Đăng nhập khách và đăng nhập nhân viên dùng cùng dạng body.
public sealed record LoginRequest(string? Email, string? Password);
