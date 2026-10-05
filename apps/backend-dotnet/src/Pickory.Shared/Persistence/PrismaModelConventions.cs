using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Pickory.Shared.Persistence;

public static class PrismaModelConventions
{
    public static void Configure(ModelConfigurationBuilder configurationBuilder)
    {
        // Prisma không tự tạo index khóa ngoại. Chỉ giữ index đã khai báo trong migration Node.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));

        // Áp dụng cả cột nullable để thời gian đọc ra dùng UTC (Coordinated Universal Time).
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcTimestampConverter>()
            .HaveColumnType("timestamp(3) without time zone")
            .HavePrecision(3);
    }

    public static void Apply(ModelBuilder modelBuilder)
    {
        // Giữ tên cột camelCase của Prisma. Không áp dụng quy ước snake_case của các hệ thống khác.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(char.ToLowerInvariant(property.Name[0]) + property.Name[1..]);

                // Prisma dùng `TEXT` không giới hạn độ dài, kể cả khóa chính chứa mã định danh.
                if (property.ClrType == typeof(string))
                {
                    property.SetColumnType("text");
                }
            }

            // Tên khóa chính phải khớp DB để migration sau mốc không đổi tên ràng buộc.
            entityType.FindPrimaryKey()?.SetName(entityType.GetTableName() + "_pkey");
        }
    }
}
