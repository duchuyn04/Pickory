namespace Pickory.Modules.Identity.Features.SaveConsent;

// Bản Node bọc lựa chọn trong `{ consent }` và luôn trả `necessary: true`.
public sealed record SaveConsentResponse(ConsentChoice Consent);
