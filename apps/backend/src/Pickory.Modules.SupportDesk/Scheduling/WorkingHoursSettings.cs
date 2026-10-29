namespace Pickory.Modules.SupportDesk.Scheduling;

// Cấu hình giờ làm việc theo giờ Việt Nam. `ClosesAt` không thuộc khung giờ, vì khung giờ là nửa mở.
internal sealed record WorkingHoursSettings(
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    IReadOnlyCollection<DateOnly> Holidays);
