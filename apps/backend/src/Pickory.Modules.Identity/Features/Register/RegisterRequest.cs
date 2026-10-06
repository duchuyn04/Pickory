namespace Pickory.Modules.Identity.Features.Register;

// `Website` và `Honeypot` là hai ô ẩn bẫy bot. Người thật không thấy hai ô này nên không điền.
public sealed record RegisterRequest(
    string? Email,
    string? Password,
    string? Name,
    string? Phone,
    string? Website,
    string? Honeypot);
