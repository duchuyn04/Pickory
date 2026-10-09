namespace Pickory.Modules.Identity.Features.VerifyEmail;

// Giao diện hiển thị `message` cho người dùng, giống bản Node.
public sealed record VerifyEmailResponse(bool Success, string Message);
