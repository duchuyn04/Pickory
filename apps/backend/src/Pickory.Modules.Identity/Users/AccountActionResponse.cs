namespace Pickory.Modules.Identity.Users;

// Kết quả của thao tác trên tài khoản. Giao diện hiển thị `message` cho người dùng, giống bản Node.
public sealed record AccountActionResponse(bool Success, string Message);
