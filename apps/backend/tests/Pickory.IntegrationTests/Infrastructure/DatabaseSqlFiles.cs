using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pickory.IntegrationTests.Infrastructure;

internal static class DatabaseSqlFiles
{
    internal static string Load(string fileName, bool removeTransaction = false)
    {
        // SQL được sao chép cùng dự án để kiểm thử không phụ thuộc thư mục làm việc.
        var path = Path.Combine(AppContext.BaseDirectory, "db", fileName);
        var sql = new StringBuilder();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            // Npgsql không nhận lệnh psql hoặc tham số riêng của công cụ dump.
            if (trimmed.StartsWith('\\') || trimmed.StartsWith("--", StringComparison.Ordinal) ||
                trimmed.StartsWith("SET ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("SELECT pg_catalog.set_config(", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            // Giao dịch bên ngoài giữ quyền rollback khi kiểm thử dấu lịch sử trên schema có sẵn.
            if (removeTransaction && (trimmed.Equals("BEGIN;", StringComparison.OrdinalIgnoreCase) ||
                                      trimmed.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            sql.AppendLine(line);
        }
        return sql.ToString();
    }

    internal static async Task<Dictionary<string, string>> ReadReferenceAsync(DbContext context)
    {
        // Schema ngẫu nhiên tránh trùng đối tượng với schema ứng dụng đang kiểm tra.
        var schema = $"dn01_reference_{Guid.NewGuid():N}";
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Định danh schema không thể truyền như giá trị SQL. Provider đặt dấu trích dẫn để giữ ranh giới định danh.
            var delimitedSchema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(schema);
            var createSchemaSql =
                $"CREATE SCHEMA {delimitedSchema} AUTHORIZATION pg_database_owner; " +
                $"COMMENT ON SCHEMA {delimitedSchema} IS 'standard public schema';";
            // Schema tham chiếu giữ metadata mặc định của PostgreSQL, không sao chép từ DB đang kiểm tra.
            await context.Database.ExecuteSqlRawAsync(createSchemaSql);
            // Chỉ thay định danh schema cố định để PostgreSQL dựng bản tham chiếu cách ly.
            var referenceSql = Load("reference-schema.sql").Replace("public.", $"\"{schema}\".", StringComparison.Ordinal);
            // PostgreSQL diễn giải toàn bộ SQL tham chiếu; bài kiểm thử không phân tích cấu trúc từng bảng.
            await context.Database.ExecuteSqlRawAsync(referenceSql);
            return await ApplicationDatabaseSchema.ReadAsync(context, schema);
        }
        finally
        {
            // Rollback loại bỏ schema tham chiếu kể cả khi đọc metadata lỗi.
            await transaction.RollbackAsync();
        }
    }
}
