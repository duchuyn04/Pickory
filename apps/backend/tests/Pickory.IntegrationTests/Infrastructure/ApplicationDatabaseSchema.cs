using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Pickory.IntegrationTests.Infrastructure;

internal static class ApplicationDatabaseSchema
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

    // Bảng và enum của shipping không có trong `reference-schema.sql` (bản chụp schema Prisma). Phép so với file đó phải bỏ qua chúng.
    internal static readonly string[] ShippingTables =
    [
        "address_provinces", "address_wards", "shipment_events", "shipments", "shipping_settings"
    ];

    internal static readonly string[] ShippingEnums = ["ShipmentStatus", "ShipmentEventSource"];

    // Bảng và enum của commerce cũng không có trong `reference-schema.sql`, nên phép so với file đó phải bỏ qua chúng.
    internal static readonly string[] CommerceTables =
    [
        "cart_items", "carts", "commerce_settings", "order_items", "order_status_history", "orders",
        "payment_transactions", "reviews", "saved_addresses"
    ];

    internal static readonly string[] CommerceEnums = ["OrderStatus", "PaymentStatus", "PaymentMethod"];

    // Khóa metadata thuộc commerce có cùng dạng với khóa của shipping. Khớp theo ranh giới tên, không theo tiền tố.
    internal static bool IsCommerceObject(string key)
    {
        return CommerceTables.Any(table => key.EndsWith($":public.{table}", StringComparison.Ordinal) ||
                                           key.Contains($":public.{table}.", StringComparison.Ordinal)) ||
               CommerceEnums.Any(name => key.EndsWith($":public.{name}", StringComparison.Ordinal));
    }

    // Khóa metadata thuộc shipping có dạng `loại:public.tên` hoặc `loại:public.tên.phần`. Khớp theo ranh giới tên, không theo tiền tố.
    internal static bool IsShippingObject(string key)
    {
        return ShippingTables.Any(table => key.EndsWith($":public.{table}", StringComparison.Ordinal) ||
                                           key.Contains($":public.{table}.", StringComparison.Ordinal)) ||
               ShippingEnums.Any(name => key.EndsWith($":public.{name}", StringComparison.Ordinal));
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

    internal static async Task<Dictionary<string, string>> ReadAsync(DbContext context, string schema = "public")
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        // Lịch sử Entity Framework Core tách khỏi schema ứng dụng. Bản tham chiếu dùng schema riêng trong cùng giao dịch.
        await ReadRowsAsync(context, """
            SELECT n.nspname, c.relname
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'p')
              AND c.relname <> '_prisma_migrations'
            ORDER BY c.relname
            """, reader => entries.Add($"table:public.{reader.GetString(1)}", ""), schema);

        // `format_type` giữ độ chính xác thời gian, phân biệt mảng, enum và `jsonb`.
        await ReadRowsAsync(context, """
            SELECT n.nspname, c.relname, a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod),
                   NOT a.attnotnull, pg_catalog.pg_get_expr(d.adbin, d.adrelid), a.attnum
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = c.oid AND d.adnum = a.attnum
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'p')
              AND c.relname <> '_prisma_migrations' AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY c.relname, a.attnum
            """, reader =>
        {
            var columnName = $"public.{reader.GetString(1)}.{reader.GetString(2)}";
            entries.Add($"column:{columnName}",
                ColumnSignature(Canonicalize(reader.GetString(3), schema), reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : Canonicalize(reader.GetString(5), schema)));
            entries.Add($"ordinal:{columnName}", reader.GetInt16(6).ToString(CultureInfo.InvariantCulture));
        }, schema);

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
            WHERE n.nspname = @schema AND t.relname <> '_prisma_migrations'
            ORDER BY t.relname, i.relname
            """, reader => entries.Add($"index:public.{reader.GetString(1)}.{reader.GetString(2)}",
                IndexSignature(reader.GetBoolean(3), reader.GetString(4), reader.GetFieldValue<string[]>(5),
                    reader.GetFieldValue<bool[]>(6), reader.IsDBNull(7) ? null : Canonicalize(reader.GetString(7), schema),
                    reader.GetFieldValue<string[]>(8))), schema);

        // Định nghĩa gốc giữ opclass và thứ tự `NULL` mà chữ ký model không biểu diễn.
        await ReadRowsAsync(context, """
            SELECT t.relname, i.relname, pg_get_indexdef(i.oid)
            FROM pg_index ix
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @schema AND t.relname <> '_prisma_migrations'
            ORDER BY t.relname, i.relname
            """, reader => entries.Add($"definition:index:public.{reader.GetString(0)}.{reader.GetString(1)}",
                Canonicalize(reader.GetString(2), schema)), schema);

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
            WHERE n.nspname = @schema AND t.relname <> '_prisma_migrations' AND con.contype IN ('p', 'u', 'f', 'c')
            ORDER BY t.relname, con.conname
            """, reader =>
        {
            var constraintName = $"public.{reader.GetString(1)}.{reader.GetString(2)}";
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
                    entries.Add($"foreign:{constraintName}",
                        $"{columns}->{(reader.GetString(5) == schema ? "public" : reader.GetString(5))}.{reader.GetString(6)}" +
                        $"({string.Join(",", reader.GetFieldValue<string[]>(7))})" +
                        $";delete={reader.GetString(8)};update={reader.GetString(9)}");
                    break;
                case "c":
                    entries.Add($"check:{constraintName}", NormalizeCheck(reader.GetString(10)));
                    break;
            }
        }, schema);

        // `enumsortorder` quyết định thứ tự so sánh các nhãn enum trong PostgreSQL.
        await ReadRowsAsync(context, """
            SELECT n.nspname, t.typname, array_agg(e.enumlabel::text ORDER BY e.enumsortorder)
            FROM pg_catalog.pg_type t
            JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
            JOIN pg_catalog.pg_enum e ON e.enumtypid = t.oid
            WHERE n.nspname = @schema
            GROUP BY n.nspname, t.typname ORDER BY t.typname
            """, reader =>
        {
            var labels = reader.GetFieldValue<string[]>(2);
            entries.Add($"enum:public.{reader.GetString(1)}",
                string.Join(",", labels));
        }, schema);
        // Schema và mô tả extension thuộc hợp đồng dump, không suy ra từ model Entity Framework Core.
        await ReadRowsAsync(context, """
            SELECT e.extname, n.nspname, COALESCE(obj_description(e.oid, 'pg_extension'), '')
            FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace
            WHERE e.extname <> 'plpgsql' ORDER BY e.extname
            """, reader => entries.Add($"extension:{reader.GetString(0)}",
                $"schema={reader.GetString(1)};comment={reader.GetString(2)}"), schema);
        // Mô tả bảng, cột, kiểu và schema không được mất khi chuyển quyền sở hữu migration.
        await ReadRowsAsync(context, """
            SELECT 'schema:public', COALESCE(obj_description(n.oid, 'pg_namespace'), '')
            FROM pg_namespace n WHERE n.nspname = @schema
            UNION ALL
            SELECT 'comment:table:public.' || c.relname, COALESCE(obj_description(c.oid, 'pg_class'), '')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'p') AND c.relname <> '_prisma_migrations'
            UNION ALL
            SELECT 'comment:column:public.' || c.relname || '.' || a.attname,
                   COALESCE(col_description(c.oid, a.attnum), '')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'p') AND c.relname <> '_prisma_migrations'
              AND a.attnum > 0 AND NOT a.attisdropped
            UNION ALL
            SELECT 'comment:enum:public.' || t.typname, COALESCE(obj_description(t.oid, 'pg_type'), '')
            FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = @schema AND t.typtype = 'e'
            """, reader => entries.Add(reader.GetString(0), reader.GetString(1)), schema);
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
                differences.Add($"Different {key}: expected=[{expectedValue}], actual=[{actualValue}]");
            }
        }
        Assert.True(differences.Count == 0, "Application schema differences:\n" + string.Join("\n", differences));
    }

    private static async Task ReadRowsAsync(DbContext context, string sql, Action<DbDataReader> addRow, string schema)
    {
        // Kết nối của context dùng DB test dưới khóa collection.
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "schema";
        parameter.Value = schema;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            addRow(reader);
        }
    }

    private static string Canonicalize(string value, string schema)
        => value.Replace($"\"{schema}\".", "", StringComparison.Ordinal)
            .Replace($"{schema}.", "", StringComparison.Ordinal);

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
