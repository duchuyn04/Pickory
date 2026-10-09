using Pickory.Modules.Identity.Contracts.Actors;

namespace Pickory.Modules.Identity.Features.GetMe;

// Hình dạng JSON giữ nguyên bản Node. Hook `useActor` của frontend đọc trực tiếp các trường này.
public sealed record MeResponse(
    string AnonymousId,
    string? UserId,
    string? Email,
    string? Name,
    string? Phone,
    string Kind,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    ConsentState Consent);
