using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class StaffPresenceConfiguration : IEntityTypeConfiguration<StaffPresence>
{
    public void Configure(EntityTypeBuilder<StaffPresence> entity)
    {
        entity.ToTable("staff_presence");
        // Mỗi nhân viên có đúng một dòng. Mã nhân viên do `identity` cấp, nên khóa chính không tự sinh.
        entity.HasKey(value => value.StaffId);
        entity.Property(value => value.StaffId).ValueGeneratedNever();
        entity.Property(value => value.Status).IsRequired();
        entity.Property(value => value.LastSeenAt).IsRequired();
    }
}
