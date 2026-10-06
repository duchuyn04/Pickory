using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class EmailTokenConfiguration : IEntityTypeConfiguration<EmailToken>
{
    public void Configure(EntityTypeBuilder<EmailToken> entity)
    {
        // Giữ token duy nhất để cùng token không cấp quyền cho nhiều bản ghi.
        entity.ToTable("email_tokens");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.TokenHash).IsRequired();
        entity.Property(value => value.UserId).IsRequired();
        entity.Property(value => value.Purpose).IsRequired();
        entity.Property(value => value.ExpiresAt).IsRequired();
        entity.Property(value => value.UsedAt).IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.TokenHash).IsUnique().HasDatabaseName("email_tokens_tokenHash_key");
        entity.HasIndex(value => value.UserId).HasDatabaseName("email_tokens_userId_idx");
        // Xóa token cùng tài khoản để token không còn tham chiếu tài khoản đã xóa.
        entity.HasOne(value => value.User)
            .WithMany(user => user.EmailTokens)
            .HasForeignKey(value => value.UserId)
            .HasConstraintName("email_tokens_userId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
