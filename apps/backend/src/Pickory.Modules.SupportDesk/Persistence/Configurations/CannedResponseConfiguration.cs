using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class CannedResponseConfiguration : IEntityTypeConfiguration<CannedResponse>
{
    // Tên hai chỉ mục phím tắt. `CannedResponseService` dùng lại để chỉ đổi đúng hai vi phạm này thành 409.
    public const string PersonalShortcutIndexName = "canned_responses_ownerId_shortcut_personal_key";
    public const string SharedShortcutIndexName = "canned_responses_shortcut_shared_key";

    public void Configure(EntityTypeBuilder<CannedResponse> entity)
    {
        entity.ToTable("canned_responses");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        // Mẫu dùng chung của cả đội không có chủ, nên `ownerId` được rỗng.
        entity.Property(value => value.OwnerId).IsRequired(false);
        entity.Property(value => value.Shortcut).IsRequired();
        entity.Property(value => value.Body).IsRequired();
        // Cột không có mặc định ở DB, vì EF Core thay giá trị `false` bằng mặc định đó. Mã nguồn luôn đặt giá trị.
        entity.Property(value => value.Shared).IsRequired();
        entity.Property(value => value.CreatedAt).IsRequired();
        entity.Property(value => value.UpdatedAt).IsRequired();

        // Mỗi nhân viên có tối đa một mẫu cá nhân cho mỗi phím tắt. Hai request cùng lúc không tạo được hai mẫu trùng.
        entity.HasIndex(value => new { value.OwnerId, value.Shortcut })
            .IsUnique()
            .HasFilter("\"shared\" = false")
            .HasDatabaseName(PersonalShortcutIndexName);

        // Tập mẫu dùng chung có tối đa một mẫu cho mỗi phím tắt. Mẫu cá nhân được cùng phím tắt với mẫu dùng chung.
        entity.HasIndex(value => value.Shortcut)
            .IsUnique()
            .HasFilter("\"shared\" = true")
            .HasDatabaseName(SharedShortcutIndexName);
    }
}
