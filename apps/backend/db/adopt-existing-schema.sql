-- Dừng ngay khi SQL thất bại để `psql` không ghi nhận mốc sau một lỗi.
\set ON_ERROR_STOP on

-- Giữ hai lịch sử trong cùng giao dịch để lỗi không để lại mốc áp dụng một phần.
BEGIN;
-- Khóa giao dịch ngăn hai lệnh ghi nhận cùng sửa lịch sử của DB.
SELECT pg_advisory_xact_lock(731001, 1);

DO $baseline$
DECLARE
    missing_tables text[];
BEGIN
    -- Chỉ ghi nhận schema đã có đủ bảng để migration không bỏ qua bảng còn thiếu.
    SELECT array_agg(expected.table_name ORDER BY expected.table_name)
    INTO missing_tables
    FROM unnest(ARRAY[
        'users', 'roles', 'role_permissions', 'user_roles', 'sessions',
        'oauth_accounts', 'email_tokens', 'consent_records', 'audit_logs',
        'categories', 'brands', 'products', 'skus', 'product_images',
        'product_slug_history', 'inventory_movements', 'image_deletion_jobs'
    ]) AS expected(table_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_class existing
        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = existing.relnamespace
        WHERE namespace.nspname = 'public' AND existing.relkind = 'r'
          AND existing.relname = expected.table_name
    );

    -- Từ chối DB trống trước khi tạo lịch sử để lần migrate sau vẫn chạy toàn bộ mốc.
    IF missing_tables IS NOT NULL THEN
        RAISE EXCEPTION 'Schema hiện có thiếu bảng: %. Không ghi nhận migration mốc.', missing_tables;
    END IF;

    -- Giữ thứ tự nhãn enum để Npgsql không gán nhãn sai khi đọc dữ liệu hiện có.
    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('UserKind', ARRAY['customer', 'staff']),
            ('EmailTokenPurpose', ARRAY['verify', 'reset', 'invite']),
            ('ProductStatus', ARRAY['draft', 'active', 'hidden', 'discontinued']),
            ('InventoryMovementType', ARRAY['manual', 'order_reserve', 'order_release', 'return'])
        ) AS expected(enum_name, labels)
        WHERE expected.labels IS DISTINCT FROM (
            SELECT array_agg(label.enumlabel::text ORDER BY label.enumsortorder)
            FROM pg_catalog.pg_type enum_type
            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = enum_type.typnamespace
            JOIN pg_catalog.pg_enum label ON label.enumtypid = enum_type.oid
            WHERE namespace.nspname = 'public' AND enum_type.typname = expected.enum_name
        )
    ) THEN
        RAISE EXCEPTION 'Enum hiện có không khớp schema mốc. Không ghi nhận migration.';
    END IF;
END
$baseline$;

-- Tách lịch sử khỏi bảng ứng dụng để dữ liệu hiện có không đổi.
CREATE SCHEMA IF NOT EXISTS ef_identity;
CREATE TABLE IF NOT EXISTS ef_identity."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);
-- Bỏ qua mốc đã ghi để lệnh có thể chạy lại mà không nhân bản lịch sử.
INSERT INTO ef_identity."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005125639_PrismaBaseline', '10.0.12')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Giữ lịch sử Catalog riêng để module chỉ theo dõi migration thuộc Catalog.
CREATE SCHEMA IF NOT EXISTS ef_catalog;
CREATE TABLE IF NOT EXISTS ef_catalog."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);
-- Bỏ qua mốc đã ghi để lần chạy lại giữ nguyên lịch sử Catalog.
INSERT INTO ef_catalog."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005125646_PrismaBaseline', '10.0.12')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Công bố hai mốc cùng lúc sau khi tất cả điều kiện đã đạt.
COMMIT;
