using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.AiGateway.Entities;

namespace Pickory.Modules.AiGateway.Persistence.Configurations;

internal sealed class AiSettingConfiguration : IEntityTypeConfiguration<AiSetting>
{
    public void Configure(EntityTypeBuilder<AiSetting> entity)
    {
        // Giữ khóa mặc định của cấu hình. `UpdatedAt` do ứng dụng cung cấp, không do DB sinh.
        entity.ToTable("ai_settings");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasDefaultValue("singleton");
        entity.Property(value => value.MonthlyBudgetUsd).HasColumnType("double precision");
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
