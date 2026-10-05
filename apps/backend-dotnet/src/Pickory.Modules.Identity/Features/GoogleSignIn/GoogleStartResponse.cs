namespace Pickory.Modules.Identity.Features.GoogleSignIn;

// Chế độ JSON trả URL của Google. Client tự điều hướng thay vì nhận chuyển hướng 302.
public sealed record GoogleStartResponse(string AuthUrl, string State);
