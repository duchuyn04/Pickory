using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        // Giữ cột nullable để tài khoản chưa khóa hoặc chưa xóa không nhận thời gian giả.
        entity.ToTable("users");
        entity.HasKey(value => value.Id);
        // Sinh UUID (Universally Unique Identifier) khi thêm bản ghi để tránh cấp phát lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Email).IsRequired();
        entity.Property(value => value.EmailVerified).HasDefaultValue(false);
        entity.Property(value => value.PasswordHash).IsRequired(false);
        entity.Property(value => value.Name).IsRequired(false);
        entity.Property(value => value.Phone).IsRequired(false);
        entity.Property(value => value.Kind).IsRequired();
        entity.Property(value => value.LockedAt).IsRequired(false);
        entity.Property(value => value.DeletedAt).IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();
        entity.HasIndex(value => value.Email).IsUnique().HasDatabaseName("users_email_key");
    }
}
