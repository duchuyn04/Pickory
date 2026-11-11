using System.Globalization;
using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Settings;

// Giá trị cấu hình đã qua kiểm dữ liệu. Service chỉ nhận kiểu này nên không phải đọc lại JSON.
internal sealed record SupportSettingsValues(
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    DateOnly[] Holidays,
    int WaitNudgeMinutes,
    int AutoCloseMinutes);

// Body của `PUT /api/admin/support/settings`. Mọi trường giữ nguyên `JsonElement`,
// vì kiểu mạnh làm lỗi đọc body (500) khi client gửi sai kiểu JSON.
internal sealed record UpdateSupportSettingsRequest(
    JsonElement OpensAt,
    JsonElement ClosesAt,
    JsonElement Holidays,
    JsonElement WaitNudgeMinutes,
    JsonElement AutoCloseMinutes)
{
    public const string TimeFormat = "HH:mm";
    public const string DateFormat = "yyyy-MM-dd";
    public const int MinMinutes = 1;
    // Giới hạn đã duyệt ngày 2026-11-04 trong mục "Giới hạn" của hợp đồng: mốc phút từ 1 đến 240.
    public const int MaxMinutes = 240;
    public const int MaxHolidays = 366;

    // Chỉ chuỗi đúng dạng `HH:mm` mới là giờ hợp lệ. Thiếu trường, sai kiểu hay sai dạng đều cho `null`.
    public TimeOnly? OpensAtValue => ReadTime(OpensAt);
    public TimeOnly? ClosesAtValue => ReadTime(ClosesAt);

    // Chỉ số nguyên JSON mới là mốc phút. Số thập phân, chuỗi hay thiếu trường đều cho `null`.
    public int? WaitNudgeValue => ReadInteger(WaitNudgeMinutes);
    public int? AutoCloseValue => ReadInteger(AutoCloseMinutes);

    // Trả `null` khi trường không phải mảng. Phần tử không phải chuỗi thành `null` để validator báo sai dạng.
    public IReadOnlyList<string?>? HolidayTexts =>
        Holidays.ValueKind == JsonValueKind.Array
            ? Holidays.EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : null)
                .ToList()
            : null;

    // Chỉ gọi sau khi validator đã xác nhận mọi trường. Ngày nghỉ sắp tăng dần để dữ liệu lưu có thứ tự ổn định.
    public SupportSettingsValues ToValues() => new(
        OpensAtValue!.Value,
        ClosesAtValue!.Value,
        HolidayTexts!.Select(text => ParseDate(text)!.Value).Order().ToArray(),
        WaitNudgeValue!.Value,
        AutoCloseValue!.Value);

    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    public static bool IsMinutesInRange(int? minutes) => minutes is >= MinMinutes and <= MaxMinutes;

    private static TimeOnly? ReadTime(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
        && TimeOnly.TryParseExact(
            element.GetString(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    private static int? ReadInteger(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : null;
}

internal sealed class UpdateSupportSettingsRequestValidator : AbstractValidator<UpdateSupportSettingsRequest>
{
    public UpdateSupportSettingsRequestValidator()
    {
        // Tên ghi đè giữ khóa lỗi khớp với trường trong body để giao diện tô đúng ô nhập.
        RuleFor(request => request.OpensAtValue)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Giờ mở cửa phải có dạng HH:mm.")
            // Chỉ so thứ tự khi cả hai giờ đã đọc được. Giờ sai dạng đã có lỗi riêng.
            .Must((request, opensAt) => request.ClosesAtValue is null || opensAt < request.ClosesAtValue)
            .WithMessage("Giờ mở cửa phải trước giờ đóng cửa.")
            .OverridePropertyName("opensAt");
        RuleFor(request => request.ClosesAtValue)
            .NotNull()
            .WithMessage("Giờ đóng cửa phải có dạng HH:mm.")
            .OverridePropertyName("closesAt");

        RuleFor(request => request.WaitNudgeValue)
            .Cascade(CascadeMode.Stop)
            .Must(UpdateSupportSettingsRequest.IsMinutesInRange)
            .WithMessage(MinutesRangeMessage("Mốc nhắc lại"))
            // Chỉ so thứ tự khi cả hai mốc nằm trong khoảng. Mốc ngoài khoảng đã có lỗi riêng.
            .Must((request, waitNudge) => !UpdateSupportSettingsRequest.IsMinutesInRange(request.AutoCloseValue)
                || waitNudge < request.AutoCloseValue)
            .WithMessage("Mốc nhắc lại phải nhỏ hơn mốc tự đóng.")
            .OverridePropertyName("waitNudgeMinutes");
        RuleFor(request => request.AutoCloseValue)
            .Must(UpdateSupportSettingsRequest.IsMinutesInRange)
            .WithMessage(MinutesRangeMessage("Mốc tự đóng"))
            .OverridePropertyName("autoCloseMinutes");

        RuleFor(request => request.HolidayTexts)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Ngày nghỉ phải là một mảng chuỗi.")
            .Must(texts => texts!.Count <= UpdateSupportSettingsRequest.MaxHolidays)
            .WithMessage($"Tối đa {UpdateSupportSettingsRequest.MaxHolidays} ngày nghỉ.")
            .Must(texts => texts!.All(text => UpdateSupportSettingsRequest.ParseDate(text) is not null))
            .WithMessage("Mỗi ngày nghỉ phải có dạng yyyy-MM-dd và có trên lịch.")
            // Mỗi ngày hợp lệ chỉ có một cách viết, nên so chuỗi đủ để phát hiện ngày trùng.
            .Must(texts => texts!.Distinct(StringComparer.Ordinal).Count() == texts!.Count)
            .WithMessage("Ngày nghỉ không được trùng nhau.")
            .OverridePropertyName("holidays");
    }

    private static string MinutesRangeMessage(string label) =>
        $"{label} phải là số nguyên từ {UpdateSupportSettingsRequest.MinMinutes} đến {UpdateSupportSettingsRequest.MaxMinutes} phút.";
}
