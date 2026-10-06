using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> entity)
    {
        // Giữ token duy nhất để hai phiên không có cùng thông tin xác thực.
        entity.ToTable("sessions");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.TokenHash).IsRequired();
        entity.Property(value => value.UserId).IsRequired();
        entity.Property(value => value.ExpiresAt).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.TokenHash).IsUnique().HasDatabaseName("sessions_tokenHash_key");
        entity.HasIndex(value => value.UserId).HasDatabaseName("sessions_userId_idx");
        // Xóa phiên cùng tài khoản để phiên không còn tham chiếu tài khoản đã xóa.
        entity.HasOne(value => value.User)
            .WithMany(user => user.Sessions)
            .HasForeignKey(value => value.UserId)
            .HasConstraintName("sessions_userId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
