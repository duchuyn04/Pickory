using Microsoft.EntityFrameworkCore.Migrations;

namespace Pickory.Modules.AiGateway.Persistence.Migrations;

public partial class PrismaBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Dùng `AlterDatabase` để Npgsql nạp lại cache kiểu khi extension `vector` được tạo trong `public`.
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:public.vector", ",,");

        // Giữ mô tả extension theo `reference-schema.sql` để dump schema không bị sai lệch.
        migrationBuilder.Sql("""
            COMMENT ON EXTENSION vector IS 'vector data type and ivfflat and hnsw access methods';
            """);

        // Giữ cấu trúc `ai_embedding_cache` theo schema mốc. Khóa `key` duy nhất ngăn trùng bản ghi cache.
        migrationBuilder.Sql("""
            CREATE TABLE public.ai_embedding_cache (
                id text NOT NULL,
                key text NOT NULL,
                model text NOT NULL,
                embedding double precision[],
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.ai_embedding_cache
                ADD CONSTRAINT ai_embedding_cache_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX ai_embedding_cache_key_key ON public.ai_embedding_cache USING btree (key);
            CREATE INDEX ai_embedding_cache_model_idx ON public.ai_embedding_cache USING btree (model);
            """);

        // Giữ cấu trúc `ai_settings` theo schema mốc. Giá trị mặc định `singleton` giữ khóa của cấu hình hiện có.
        migrationBuilder.Sql("""
            CREATE TABLE public.ai_settings (
                id text DEFAULT 'singleton'::text NOT NULL,
                "monthlyBudgetUsd" double precision NOT NULL,
                "chatDailyLimitUser" integer NOT NULL,
                "chatDailyLimitAnon" integer NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL
            );

            ALTER TABLE ONLY public.ai_settings
                ADD CONSTRAINT ai_settings_pkey PRIMARY KEY (id);
            """);

        // Giữ cấu trúc `ai_usage_logs` theo schema mốc để kiểu chi phí và index không đổi.
        migrationBuilder.Sql("""
            CREATE TABLE public.ai_usage_logs (
                id text NOT NULL,
                feature text NOT NULL,
                provider text NOT NULL,
                model text NOT NULL,
                "promptVersion" text,
                "inputTokens" integer DEFAULT 0 NOT NULL,
                "outputTokens" integer DEFAULT 0 NOT NULL,
                "cachedTokens" integer DEFAULT 0 NOT NULL,
                "costUsd" double precision DEFAULT 0 NOT NULL,
                "latencyMs" integer NOT NULL,
                status text NOT NULL,
                "errorCode" text,
                "actorKeyHash" text,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.ai_usage_logs
                ADD CONSTRAINT ai_usage_logs_pkey PRIMARY KEY (id);

            CREATE INDEX "ai_usage_logs_actorKeyHash_idx" ON public.ai_usage_logs USING btree ("actorKeyHash");
            CREATE INDEX "ai_usage_logs_createdAt_idx" ON public.ai_usage_logs USING btree ("createdAt");
            CREATE INDEX ai_usage_logs_feature_idx ON public.ai_usage_logs USING btree (feature);
            CREATE INDEX ai_usage_logs_status_idx ON public.ai_usage_logs USING btree (status);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Chỉ gỡ bảng thuộc AiGateway để `Down` không xóa bảng của module khác.
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS public.ai_embedding_cache;
            DROP TABLE IF EXISTS public.ai_settings;
            DROP TABLE IF EXISTS public.ai_usage_logs;
            """);

        // Gỡ `vector` sau các bảng sở hữu. Không dùng `CASCADE` để tránh xóa đối tượng ngoài module.
        migrationBuilder.Sql("""
            DROP EXTENSION IF EXISTS vector;
            """);
    }
}
