using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class OAuthAccountConfiguration : IEntityTypeConfiguration<OAuthAccount>
{
    public void Configure(EntityTypeBuilder<OAuthAccount> entity)
    {
        // Giữ khóa duy nhất theo nhà cung cấp để một danh tính ngoài không liên kết hai lần.
        entity.ToTable("oauth_accounts");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Provider).IsRequired();
        entity.Property(value => value.ProviderUserId).IsRequired();
        entity.Property(value => value.UserId).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.UserId).HasDatabaseName("oauth_accounts_userId_idx");
        entity.HasIndex(value => new { value.Provider, value.ProviderUserId })
            .IsUnique()
            .HasDatabaseName("oauth_accounts_provider_providerUserId_key");
        // Xóa danh tính ngoài cùng tài khoản để không còn liên kết đăng nhập của tài khoản cũ.
        entity.HasOne(value => value.User)
            .WithMany(user => user.OAuthAccounts)
            .HasForeignKey(value => value.UserId)
            .HasConstraintName("oauth_accounts_userId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
