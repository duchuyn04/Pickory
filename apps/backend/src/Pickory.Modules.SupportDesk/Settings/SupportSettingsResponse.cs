using System.Globalization;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Settings;

// Response của `GET` và `PUT /api/admin/support/settings`. Giờ ghi `HH:mm`, ngày nghỉ ghi `yyyy-MM-dd`.
internal sealed record SupportSettingsResponse(
    string OpensAt,
    string ClosesAt,
    IReadOnlyList<string> Holidays,
    int WaitNudgeMinutes,
    int AutoCloseMinutes,
    DateTime UpdatedAt)
{
    public static SupportSettingsResponse From(SupportSettings settings) =>
        new(
            settings.OpensAt.ToString(UpdateSupportSettingsRequest.TimeFormat, CultureInfo.InvariantCulture),
            settings.ClosesAt.ToString(UpdateSupportSettingsRequest.TimeFormat, CultureInfo.InvariantCulture),
            settings.Holidays
                .Select(day => day.ToString(UpdateSupportSettingsRequest.DateFormat, CultureInfo.InvariantCulture))
                .ToList(),
            settings.WaitNudgeMinutes,
            settings.AutoCloseMinutes,
            settings.UpdatedAt);
}
