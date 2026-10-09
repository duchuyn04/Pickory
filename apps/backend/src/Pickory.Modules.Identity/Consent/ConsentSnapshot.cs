namespace Pickory.Modules.Identity.Consent;

// Lựa chọn consent kèm thời điểm tạo. `ConsentResolver` cần thời điểm để chọn bản mới hơn.
public sealed record ConsentSnapshot(bool Personalization, bool Marketing, DateTime CreatedAt);
