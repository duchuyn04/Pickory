using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Pickory.IntegrationTests;

internal static class PrismaDatabaseSchema
{
    internal static readonly string[] IdentityTables =
    [
        "audit_logs", "consent_records", "email_tokens", "oauth_accounts", "role_permissions",
        "roles", "sessions", "user_roles", "users"
    ];

    internal static readonly string[] CatalogTables =
    [
        "brands", "categories", "image_deletion_jobs", "inventory_movements", "product_images",
        "product_slug_history", "products", "skus"
    ];

    internal static readonly string[] AiGatewayTables = ["ai_embedding_cache", "ai_settings", "ai_usage_logs"];

    internal static async Task RequireMigratedSchemaAsync(DbContext context)
    {
        // Thiếu bảng phải báo điều kiện tiên quyết, không tự thay đổi schema do Prisma quản lý.
        var actual = await ReadAsync(context);
        var missing = IdentityTables.Concat(CatalogTables).Concat(AiGatewayTables)
            .Where(table => !actual.ContainsKey($"table:public.{table}")).ToArray();
        Assert.True(missing.Length == 0,
            "TEST_DATABASE_URL must point to a database already migrated by Prisma through " +
            "20261005090100_catalog_indexes. Missing public tables: " + string.Join(", ", missing) +
            ". These tests never create or migrate the Prisma schema.");

        // Lịch sử Prisma phân biệt schema đã migrate với schema được dựng thủ công hoặc còn thiếu migration.
        var hasPrismaHistory = false;
        await ReadRowsAsync(context, "SELECT to_regclass('public._prisma_migrations') IS NOT NULL",
            reader => hasPrismaHistory = reader.GetBoolean(0));
        Assert.True(hasPrismaHistory,
            "TEST_DATABASE_URL must contain Prisma migration history. Run Prisma migrations on the test database first.");
        var appliedMigrations = new HashSet<string>(StringComparer.Ordinal);
        await ReadRowsAsync(context, """
            SELECT migration_name FROM public._prisma_migrations
            WHERE finished_at IS NOT NULL AND rolled_back_at IS NULL
            """, reader => appliedMigrations.Add(reader.GetString(0)));
        string[] requiredMigrations =
        [
            "20261004071944", "20261005080000_ai_gateway",
            "20261005090000_catalog", "20261005090100_catalog_indexes"
        ];
        var missingMigrations = requiredMigrations.Where(name => !appliedMigrations.Contains(name)).ToArray();
        Assert.True(missingMigrations.Length == 0,
            "TEST_DATABASE_URL is not fully migrated by Prisma. Missing successful migrations: " +
            string.Join(", ", missingMigrations) + ". No migrations are applied by these tests.");
    }

    internal static Dictionary<string, string> ReadModel(IEnumerable<DbContext> contexts)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            // `IDesignTimeModel` giữ metadata ràng buộc mà model thực thi có thể loại bỏ.
            var model = context.GetService<IDesignTimeModel>().Model;
            foreach (var table in model.GetRelationalModel().Tables)
            {
                var tableName = $"{table.Schema ?? "public"}.{table.Name}";
                entries.Add($"table:{tableName}", "");
                foreach (var column in table.Columns)
                {
                    // Literal của provider phản ánh enum hoặc giá trị mặc định sau chuyển đổi kiểu.
                    var defaultSql = column.DefaultValueSql;
                    if (defaultSql is null && column.TryGetDefaultValue(out var defaultValue) && defaultValue is not null)
                    {
                        defaultSql = column.PropertyMappings.First().TypeMapping.GenerateSqlLiteral(defaultValue);
                    }

                    entries.Add($"column:{tableName}.{column.Name}",
                        ColumnSignature(column.StoreType, column.IsNullable, defaultSql));
                }

                foreach (var constraint in table.UniqueConstraints)
                {
                    var columns = string.Join(",", constraint.Columns.Select(column => column.Name));
                    var kind = constraint.GetIsPrimaryKey() ? "primary" : "unique";
                    entries.Add($"{kind}:{tableName}.{constraint.Name}", columns);
                    entries.Add($"index:{tableName}.{constraint.Name}",
                        IndexSignature(true, "btree", constraint.Columns.Select(column => column.Name),
                            Enumerable.Repeat(false, constraint.Columns.Count), null, []));
                }

                foreach (var index in table.Indexes)
                {
                    var mappedIndex = index.MappedIndexes.First();
                    // Danh sách rỗng biểu thị thứ tự giảm dần cho mọi cột theo hợp đồng EF.
                    var descending = index.IsDescending is null
                        ? Enumerable.Repeat(false, index.Columns.Count)
                        : index.IsDescending.Count == 0
                            ? Enumerable.Repeat(true, index.Columns.Count)
                            : index.IsDescending;
                    var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);
                    var includedColumns = (mappedIndex.GetIncludeProperties() ?? [])
                        .Select(name => mappedIndex.DeclaringEntityType.FindProperty(name)!.GetColumnName(storeObject)!);
                    entries.Add($"index:{tableName}.{index.Name}",
                        IndexSignature(index.IsUnique, mappedIndex.GetMethod() ?? "btree",
                            index.Columns.Select(column => column.Name), descending, index.Filter, includedColumns));
                }

                foreach (var foreignKey in table.ForeignKeyConstraints)
                {
                    var principalTable = $"{foreignKey.PrincipalTable.Schema ?? "public"}.{foreignKey.PrincipalTable.Name}";
                    entries.Add($"foreign:{tableName}.{foreignKey.Name}",
                        $"{string.Join(",", foreignKey.Columns.Select(column => column.Name))}->" +
                        $"{principalTable}({string.Join(",", foreignKey.PrincipalColumns.Select(column => column.Name))})" +
                        $";delete={DeleteAction(foreignKey.OnDeleteAction)}");
                }

                foreach (var check in table.CheckConstraints)
                {
                    entries.Add($"check:{tableName}.{check.Name}", NormalizeCheck(check.Sql));
                }
            }

            foreach (var postgresEnum in model.GetPostgresEnums())
            {
                entries.Add($"enum:{postgresEnum.Schema ?? "public"}.{postgresEnum.Name}",
                    string.Join(",", postgresEnum.Labels));
            }
        }

        return entries;
    }

    internal static async Task<Dictionary<string, string>> ReadAsync(DbContext context)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        // Các bảng Prisma thuộc `public`. Lịch sử EF nằm trong schema riêng của từng module.
        await ReadRowsAsync(context, """
            SELECT n.nspname, c.relname
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
              AND c.relname <> '_prisma_migrations'
            ORDER BY c.relname
            """, reader => entries.Add($"table:{reader.GetString(0)}.{reader.GetString(1)}", ""));

        // `format_type` giữ độ chính xác thời gian, phân biệt mảng, enum và `jsonb`.
        await ReadRowsAsync(context, """
            SELECT n.nspname, c.relname, a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod),
                   NOT a.attnotnull, pg_catalog.pg_get_expr(d.adbin, d.adrelid)
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = c.oid AND d.adnum = a.attnum
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
              AND c.relname <> '_prisma_migrations' AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY c.relname, a.attnum
            """, reader => entries.Add($"column:{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}",
                ColumnSignature(reader.GetString(3), reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetString(5))));

        // Thứ tự `indkey` phát hiện các cột bị đảo trong chỉ mục tổng hợp.
        await ReadRowsAsync(context, """
            SELECT n.nspname, t.relname, i.relname, ix.indisunique, am.amname,
                   ARRAY(SELECT COALESCE(a.attname, pg_get_indexdef(i.oid, key.ordinality::int, true))
                         FROM unnest(ix.indkey) WITH ORDINALITY AS key(attnum, ordinality)
                         LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = key.attnum
                         WHERE key.ordinality <= ix.indnkeyatts ORDER BY key.ordinality),
                   ARRAY(SELECT (option & 1) = 1 FROM unnest(ix.indoption) WITH ORDINALITY AS flags(option, ordinality)
                         ORDER BY ordinality),
                   pg_get_expr(ix.indpred, t.oid),
                   ARRAY(SELECT a.attname FROM unnest(ix.indkey) WITH ORDINALITY AS key(attnum, ordinality)
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = key.attnum
                         WHERE key.ordinality > ix.indnkeyatts ORDER BY key.ordinality)
            FROM pg_catalog.pg_index ix
            JOIN pg_catalog.pg_class t ON t.oid = ix.indrelid
            JOIN pg_catalog.pg_class i ON i.oid = ix.indexrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_catalog.pg_am am ON am.oid = i.relam
            WHERE n.nspname = 'public' AND t.relname <> '_prisma_migrations'
            ORDER BY t.relname, i.relname
            """, reader => entries.Add($"index:{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}",
                IndexSignature(reader.GetBoolean(3), reader.GetString(4), reader.GetFieldValue<string[]>(5),
                    reader.GetFieldValue<bool[]>(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetFieldValue<string[]>(8))));

        // So sánh tên, cột và hành vi ràng buộc để phát hiện sai lệch có cùng số lượng.
        await ReadRowsAsync(context, """
            SELECT n.nspname, t.relname, con.conname, con.contype::text,
                   ARRAY(SELECT a.attname FROM unnest(con.conkey) WITH ORDINALITY AS key(attnum, ordinality)
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = key.attnum ORDER BY key.ordinality),
                   pn.nspname, pt.relname,
                   ARRAY(SELECT a.attname FROM unnest(con.confkey) WITH ORDINALITY AS key(attnum, ordinality)
                         JOIN pg_attribute a ON a.attrelid = pt.oid AND a.attnum = key.attnum ORDER BY key.ordinality),
                   con.confdeltype::text, con.confupdtype::text, pg_get_constraintdef(con.oid, true)
            FROM pg_catalog.pg_constraint con
            JOIN pg_catalog.pg_class t ON t.oid = con.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
            LEFT JOIN pg_catalog.pg_class pt ON pt.oid = con.confrelid
            LEFT JOIN pg_catalog.pg_namespace pn ON pn.oid = pt.relnamespace
            WHERE n.nspname = 'public' AND t.relname <> '_prisma_migrations' AND con.contype IN ('p', 'u', 'f', 'c')
            ORDER BY t.relname, con.conname
            """, reader =>
        {
            var constraintName = $"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}";
            var columns = string.Join(",", reader.GetFieldValue<string[]>(4));
            switch (reader.GetString(3))
            {
                case "p":
                    entries.Add($"primary:{constraintName}", columns);
                    break;
                case "u":
                    entries.Add($"unique:{constraintName}", columns);
                    break;
                case "f":
                    // EF không biểu diễn `ON UPDATE`; kiểm tra trực tiếp hợp đồng `CASCADE` của migration Prisma.
                    Assert.True(reader.GetString(9) == "c",
                        $"Foreign key {constraintName}: Prisma requires ON UPDATE CASCADE, actual action is [{reader.GetString(9)}].");
                    entries.Add($"foreign:{constraintName}",
                        $"{columns}->{reader.GetString(5)}.{reader.GetString(6)}" +
                        $"({string.Join(",", reader.GetFieldValue<string[]>(7))})" +
                        $";delete={reader.GetString(8)}");
                    break;
                case "c":
                    entries.Add($"check:{constraintName}", NormalizeCheck(reader.GetString(10)));
                    break;
            }
        });

        // `enumsortorder` quyết định thứ tự so sánh các nhãn enum trong PostgreSQL.
        await ReadRowsAsync(context, """
            SELECT n.nspname, t.typname, array_agg(e.enumlabel::text ORDER BY e.enumsortorder)
            FROM pg_catalog.pg_type t
            JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
            JOIN pg_catalog.pg_enum e ON e.enumtypid = t.oid
            WHERE n.nspname = 'public'
            GROUP BY n.nspname, t.typname ORDER BY t.typname
            """, reader =>
        {
            var labels = reader.GetFieldValue<string[]>(2);
            entries.Add($"enum:{reader.GetString(0)}.{reader.GetString(1)}",
                string.Join(",", labels));
        });
        return entries;
    }

    internal static void AssertEqual(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        // Báo mọi sai lệch trong một lần để tránh bỏ sót lỗi schema.
        var differences = new List<string>();
        foreach (var key in expected.Keys.Union(actual.Keys).Order(StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(key, out var expectedValue))
            {
                differences.Add($"Unexpected {key}: {actual[key]}");
                continue;
            }
            if (!actual.TryGetValue(key, out var actualValue))
            {
                differences.Add($"Missing {key}: expected {expectedValue}");
                continue;
            }
            if (expectedValue != actualValue)
            {
                differences.Add($"Different {key}: EF=[{expectedValue}], PostgreSQL=[{actualValue}]");
            }
        }
        Assert.True(differences.Count == 0, "EF/Prisma schema differences:\n" + string.Join("\n", differences));
    }

    private static async Task ReadRowsAsync(DbContext context, string sql, Action<DbDataReader> addRow)
    {
        // Kết nối của context dùng DB test đã được tiêm, không chọn DB phát triển.
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            addRow(reader);
        }
    }

    private static string ColumnSignature(string storeType, bool nullable, string? defaultSql)
        => $"type={storeType.Replace("\"", "", StringComparison.Ordinal)};nullable={nullable};default={NormalizeDefault(defaultSql)}";

    private static string IndexSignature(bool unique, string method, IEnumerable<string> columns,
        IEnumerable<bool> descending, string? filter, IEnumerable<string> includedColumns)
        => $"unique={unique};method={method};columns={string.Join(",", columns)};" +
           $"descending={string.Join(",", descending)};filter={NormalizeCheck(filter ?? "")};" +
           $"include={string.Join(",", includedColumns)}";

    private static string NormalizeDefault(string? sql)
    {
        if (sql is null)
        {
            return "<none>";
        }

        // PostgreSQL thêm phép ép kiểu vào hằng số nhưng giữ nguyên giá trị mặc định.
        var normalized = Regex.Replace(sql.Trim(),
            "::(?:\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_]*)(?:\\[\\])?(?:\\s+precision)?", "").Trim();
        while (normalized.StartsWith('(') && normalized.EndsWith(')'))
        {
            normalized = normalized[1..^1].Trim();
        }

        // Khác biệt chữ hoa của literal boolean không làm đổi giá trị mặc định trong PostgreSQL.
        if (bool.TryParse(normalized, out var booleanValue))
        {
            return booleanValue ? "true" : "false";
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue))
        {
            return numericValue.ToString("R", CultureInfo.InvariantCulture);
        }

        return normalized.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase)
            ? "CURRENT_TIMESTAMP"
            : normalized;
    }

    private static string NormalizeCheck(string sql)
        => Regex.Replace(Regex.Replace(sql, "^CHECK\\s*", "", RegexOptions.IgnoreCase), "[\\s()\"]", "");

    private static string DeleteAction(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "c",
        ReferentialAction.Restrict => "r",
        ReferentialAction.SetNull => "n",
        ReferentialAction.SetDefault => "d",
        ReferentialAction.NoAction => "a",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported foreign key delete action")
    };
}
