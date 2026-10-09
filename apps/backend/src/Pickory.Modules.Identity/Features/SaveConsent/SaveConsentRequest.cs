namespace Pickory.Modules.Identity.Features.SaveConsent;

// Kiểu nullable phân biệt trường bị thiếu với giá trị `false`. Validator áp giá trị mặc định của bản Node.
public sealed record SaveConsentRequest(bool? Necessary, bool? Personalization, bool? Marketing);
