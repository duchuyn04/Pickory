namespace Pickory.Modules.Identity.Features.Logout;

// Frontend kiểm tra `success` sau khi đăng xuất, giống bản Node.
public sealed record LogoutResponse(bool Success);
