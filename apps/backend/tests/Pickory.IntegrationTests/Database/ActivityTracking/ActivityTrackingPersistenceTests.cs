using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.ActivityTracking.Entities;
using Pickory.Modules.ActivityTracking.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database.ActivityTracking;

// ACT-04: bảng `daily_counters` đúng kiểu cột và khóa chính, DB tự từ chối dòng trùng, và schema không có cột định danh khách.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ActivityTrackingPersistenceTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    // Ngày xa quá khứ mà không luồng thật nào ghi. Test đếm lượt commit dòng của hôm nay vào cùng DB test, nên ngày gần đây dễ trùng khóa chính.
    private static readonly DateOnly UnusedCounterDate = new(2000, 1, 1);

    [Fact]
    public async Task DailyCounters_HasExactlyThreeNotNullColumnsWithExpectedTypes()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var columns = await ReadRowsAsync(context, """
            SELECT column_name || ':' || data_type || ':' || is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'daily_counters'
            ORDER BY column_name
            """);

        // Chỉ ba cột và cả ba `NOT NULL`. Cột thừa là chỗ có thể lọt dữ liệu định danh khách.
        Assert.Equal(["date:date:NO", "metric:text:NO", "value:bigint:NO"], columns);
    }

    [Fact]
    public async Task DailyCounters_PrimaryKeyIsNamedAndCoversDateThenMetric()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var primaryKeys = await ReadRowsAsync(context, """
            SELECT constraint_row.conname || ':' || string_agg(attribute_row.attname, ',' ORDER BY key_row.position)
            FROM pg_constraint constraint_row
            JOIN pg_class table_row ON table_row.oid = constraint_row.conrelid
            JOIN pg_namespace schema_row ON schema_row.oid = table_row.relnamespace
            CROSS JOIN LATERAL unnest(constraint_row.conkey) WITH ORDINALITY AS key_row(attribute_number, position)
            JOIN pg_attribute attribute_row
              ON attribute_row.attrelid = constraint_row.conrelid AND attribute_row.attnum = key_row.attribute_number
            WHERE schema_row.nspname = 'public' AND table_row.relname = 'daily_counters' AND constraint_row.contype = 'p'
            GROUP BY constraint_row.conname
            """);

        // Thứ tự `date` rồi `metric` quyết định đọc theo khoảng ngày dùng được khóa chính làm chỉ mục.
        Assert.Equal(["daily_counters_pkey:date,metric"], primaryKeys);
    }

    [Fact]
    public async Task DailyCounters_RejectsSecondRowWithSameDateAndMetric()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ dòng thử để rollback. Lỗi khóa chính làm giao dịch hỏng nên không dùng tiếp sau đó.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO daily_counters (date, metric, value) VALUES ({UnusedCounterDate}, 'product_view', 1)");

            var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
                $"INSERT INTO daily_counters (date, metric, value) VALUES ({UnusedCounterDate}, 'product_view', 2)"));

            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            Assert.Equal("daily_counters_pkey", exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task DailyCounters_WritesAndFreshlyReadsDateMetricAndBigValue()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Giá trị vượt `int` để chứng minh cột `bigint` giữ đủ độ lớn, vì bộ đếm chỉ tăng.
            const long valueAboveInt = 5_000_000_000;
            context.DailyCounters.Add(new DailyCounter { Date = UnusedCounterDate, Metric = "add_to_cart", Value = valueAboveInt });
            await context.SaveChangesAsync();
            // Xóa bộ nhớ theo dõi để lần đọc sau lấy dòng từ DB, không lấy từ đối tượng đã ghi.
            context.ChangeTracker.Clear();

            // Đọc theo đúng khóa chính đã ghi, để dòng khác trong bảng không làm test đỏ.
            var stored = await context.DailyCounters.AsNoTracking()
                .SingleAsync(counter => counter.Date == UnusedCounterDate && counter.Metric == "add_to_cart");

            Assert.Equal(UnusedCounterDate, stored.Date);
            Assert.Equal("add_to_cart", stored.Metric);
            Assert.Equal(valueAboveInt, stored.Value);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    // Luật riêng tư: bộ đếm chỉ lưu số gộp theo ngày. Không bảng hay cột nào của module được chứa mã khách.
    public async Task Model_HasNoTableOrColumnNamedAfterAnonymousIdOrUserId()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();

        var names = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .SelectMany(property => new[] { property.Name, property.GetColumnName() })
                .Append(entityType.GetTableName() ?? string.Empty)
                .Append(entityType.ClrType.Name))
            .ToList();

        // Có tên để duyệt thì phép kiểm bên dưới mới có nghĩa. Model rỗng sẽ cho kết quả xanh giả.
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => name.Contains("anonymousid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("userid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    // ACT-13: cùng luật riêng tư với test model, nhưng đọc schema thật của các bảng trong model.
    // Test bắt được cột mà migration thêm lệch khỏi model.
    public async Task Database_HasNoTableOrColumnNamedAfterAnonymousIdOrUserId()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var schema = await ReadModuleSchemaAsync(context);

        // Mọi bảng của module phải đọc được từ DB. Danh sách rỗng sẽ làm phép kiểm bên dưới xanh giả.
        Assert.NotEmpty(schema.ExpectedTables);
        Assert.Equal(schema.ExpectedTables.Order(), schema.FoundTables.Order());
        // Cột `value` của `daily_counters` chắc chắn có. Truy vấn cột trả rỗng, ví dụ do thiếu quyền đọc, sẽ làm test đỏ thay vì xanh giả.
        Assert.Contains("value", schema.ColumnNames);
        Assert.Empty(FindCustomerIdentifierNames(schema.Names));
    }

    // Tên bảng và tên cột của module trong DB thật. Gồm bảng của model và bảng lịch sử migration của module.
    private sealed record ModuleSchema(
        IReadOnlyList<string> ExpectedTables,
        IReadOnlyList<string> FoundTables,
        IReadOnlyList<string> ColumnNames,
        IReadOnlyList<string> Names);

    private static async Task<ModuleSchema> ReadModuleSchemaAsync(ActivityTrackingDbContext context)
    {
        var expectedTables = context.Model.GetEntityTypes()
            // Entity không có bảng bị bỏ qua, vì không có cột nào để kiểm.
            .Where(entityType => entityType.GetTableName() is not null)
            .Select(entityType => (Schema: entityType.GetSchema() ?? "public", Table: entityType.GetTableName()!))
            .ToList();
        // Bảng lịch sử migration nằm ở schema riêng. Bảng này cũng không được chứa mã khách, nên phải kiểm cùng bảng của model.
        var history = context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();
        expectedTables.Add((history.MigrationsHistoryTableSchema ?? "public", history.MigrationsHistoryTableName ?? "__EFMigrationsHistory"));
        // Nhiều entity có thể dùng chung một bảng. `Distinct` gộp để so sánh bảng kỳ vọng với DB không báo lặp.
        expectedTables = expectedTables.Distinct().ToList();

        // Tên bảng đến từ model và cấu hình của module, không từ người dùng. Vẫn nhân đôi dấu nháy để câu SQL luôn đúng cú pháp.
        var tableFilter = string.Join(
            " OR ",
            expectedTables.Select(table =>
                $"(table_schema = '{table.Schema.Replace("'", "''")}' AND table_name = '{table.Table.Replace("'", "''")}')"));

        // Truy vấn bảng đọc từ DB để so với bảng kỳ vọng. Bảng thiếu trong DB làm test đỏ.
        var foundTables = await ReadRowsAsync(context, $"""
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE {tableFilter}
            ORDER BY table_schema, table_name
            """);

        // Đọc cột bằng truy vấn riêng. Ghép `schema.bảng:cột` rồi tách chuỗi sẽ cắt sai khi tên chứa dấu `.` hoặc `:`.
        var columnNames = await ReadRowsAsync(context, $"""
            SELECT column_name
            FROM information_schema.columns
            WHERE {tableFilter}
            ORDER BY table_schema, table_name, ordinal_position
            """);

        // Tên cần kiểm gồm tên bảng kỳ vọng và tên cột đọc từ DB. `Distinct` bỏ tên lặp giữa các bảng.
        var names = expectedTables.Select(table => table.Table).Concat(columnNames).Distinct().ToList();
        return new ModuleSchema(
            expectedTables.Select(table => $"{table.Schema}.{table.Table}").ToList(), foundTables, columnNames, names);
    }

    // Bỏ dấu `_` và không phân biệt hoa thường, để `anonymous_id` và `UserId` đều bị bắt.
    private static List<string> FindCustomerIdentifierNames(IEnumerable<string> names)
    {
        return names
            .Where(name =>
            {
                var normalized = name.Replace("_", string.Empty).ToLowerInvariant();
                return normalized.Contains("anonymousid") || normalized.Contains("userid");
            })
            .ToList();
    }

    private static async Task<List<string>> ReadRowsAsync(ActivityTrackingDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var rows = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
