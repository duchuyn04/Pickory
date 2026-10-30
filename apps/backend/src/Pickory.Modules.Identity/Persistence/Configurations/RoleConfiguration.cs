using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> entity)
    {
        // Giữ `CURRENT_TIMESTAMP` của DB để mọi bản ghi vai trò dùng cùng nguồn thời gian tạo.
        entity.ToTable("roles");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Code).IsRequired();
        entity.Property(value => value.Name).IsRequired();
        entity.Property(value => value.IsSystem).HasDefaultValue(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        // Lưu thời điểm xóa mềm để truy vết. Dòng đã xóa không còn được gán cho nhân viên.
        entity.Property(value => value.DeletedAt).IsRequired(false);
        // Ẩn role đã xóa khỏi danh sách và mọi lần kiểm quyền.
        entity.HasQueryFilter(value => value.DeletedAt == null);
        entity.HasIndex(value => value.Code).IsUnique().HasDatabaseName("roles_code_key")
            // Chỉ chặn trùng mã giữa các role còn sống. Dòng đã xóa nhường mã cho role mới.
            .HasFilter("\"deletedAt\" IS NULL");
    }
}
