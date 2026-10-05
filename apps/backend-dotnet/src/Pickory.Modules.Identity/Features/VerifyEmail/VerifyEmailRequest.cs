namespace Pickory.Modules.Identity.Features.VerifyEmail;

// `Token` là token gốc trong link của thư xác thực. DB chỉ lưu hash của token này.
public sealed record VerifyEmailRequest(string? Token);
