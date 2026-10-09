namespace Pickory.Modules.Identity.Sessions;

// Token gốc chỉ dùng để ghi cookie. DB không lưu giá trị này.
public sealed record IssuedSession(string RawToken, DateTime ExpiresAt);
