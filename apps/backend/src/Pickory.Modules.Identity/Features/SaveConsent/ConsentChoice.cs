namespace Pickory.Modules.Identity.Features.SaveConsent;

// `Necessary` luôn là `true`. Nhóm cần thiết giữ web hoạt động và không tắt được.
public sealed record ConsentChoice(bool Necessary, bool Personalization, bool Marketing);
