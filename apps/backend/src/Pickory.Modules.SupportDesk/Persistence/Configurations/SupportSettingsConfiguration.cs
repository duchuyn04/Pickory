using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class SupportSettingsConfiguration : IEntityTypeConfiguration<SupportSettings>
{
    // Mã của dòng duy nhất. Mã nguồn luôn đọc và ghi dòng này, nên không cần sinh mã ngẫu nhiên.
    public const string SingletonId = "singleton";

    public void Configure(EntityTypeBuilder<SupportSettings> entity)
    {
        entity.ToTable("support_settings");
        // Bảng chỉ có một dòng nên mã dòng do mã nguồn đặt cố định, không sinh ngẫu nhiên.
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).ValueGeneratedNever();
        // Một khung giờ mỗi ngày nên dùng hai cột `time` tường minh, thay cho một cột JSON mà spec gốc ghi. Kiểu `time` không mang múi giờ.
        entity.Property(value => value.OpensAt).HasColumnType("time without time zone").IsRequired();
        entity.Property(value => value.ClosesAt).HasColumnType("time without time zone").IsRequired();
        // Kiểu `date` không có giờ, nên ngày nghỉ không lệch theo múi giờ của máy chủ.
        entity.Property(value => value.Holidays).HasColumnType("date[]").IsRequired();
        entity.Property(value => value.WaitNudgeMinutes).IsRequired();
        entity.Property(value => value.AutoCloseMinutes).IsRequired();
        entity.Property(value => value.UpdatedAt).IsRequired();

        // Dòng mặc định nằm trong migration, nên DB mới có đủ cấu hình mà không cần job khởi tạo.
        // Giờ mặc định 8:00 đến 21:00, nhắc lại sau 10 phút chờ và tự đóng phiên sau 30 phút im lặng.

        // Không sửa hay bỏ `HasData` này trong migration sau. EF sẽ ghi đè hoặc xóa cấu hình admin đã lưu.
        entity.HasData(new SupportSettings
        {
            Id = SingletonId,
            OpensAt = new TimeOnly(8, 0),
            ClosesAt = new TimeOnly(21, 0),
            Holidays = [],
            WaitNudgeMinutes = 10,
            AutoCloseMinutes = 30,
            UpdatedAt = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
