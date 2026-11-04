using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Persistence.Configurations;

namespace Pickory.Modules.SupportDesk.Scheduling;

// Dòng cấu hình CSKH đã đọc từ DB. Chỉ chứa trường mà bước mở phiên và job nhắc lại cần.
internal sealed record SupportSettingsView(
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    IReadOnlyCollection<DateOnly> Holidays,
    int WaitNudgeMinutes)
{
    // Chuyển sang cấu hình của hàm thuần, để bên gọi không tự ghép lại bộ tham số giờ làm việc.
    public WorkingHoursSettings ToWorkingHours() => new(OpensAt, ClosesAt, Holidays);
}

// Đọc dòng cấu hình duy nhất. Khung giờ, ngày nghỉ và mốc nhắc lại do admin sửa, nên module không dùng hằng số trong mã nguồn.
internal sealed class SupportSettingsReader(SupportDeskDbContext database)
{
    public async Task<SupportSettingsView> ReadAsync(CancellationToken cancellationToken)
    {
        var settings = await database.Settings.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == SupportSettingsConfiguration.SingletonId, cancellationToken);
        // Migration tạo sẵn dòng mặc định. Thiếu dòng là lỗi dữ liệu, vì đoán giá trị sẽ báo sai giờ hỗ trợ cho khách.
        if (settings is null)
        {
            throw new InvalidOperationException("Bảng `support_settings` thiếu dòng cấu hình do migration tạo.");
        }

        return new SupportSettingsView(
            settings.OpensAt, settings.ClosesAt, settings.Holidays, settings.WaitNudgeMinutes);
    }
}
