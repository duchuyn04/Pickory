// EF Core sinh ảnh chụp model để migration sau mốc so sánh với schema hiện có.
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pickory.Modules.AiGateway.Persistence;

#nullable disable

namespace Pickory.Modules.AiGateway.Persistence.Migrations
{
    [DbContext(typeof(AiGatewayDbContext))]
    partial class AiGatewayDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            // Giữ `vector` trong ảnh chụp để lần so model sau mốc không báo thiếu extension.
            NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "public", "vector");
            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Pickory.Modules.AiGateway.Entities.AiEmbeddingCache", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasPrecision(3)
                        .HasColumnType("timestamp(3) without time zone")
                        .HasColumnName("createdAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.PrimitiveCollection<double[]>("Embedding")
                        .HasColumnType("double precision[]")
                        .HasColumnName("embedding");

                    b.Property<string>("Key")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("key");

                    b.Property<string>("Model")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("model");

                    b.HasKey("Id")
                        .HasName("ai_embedding_cache_pkey");

                    b.HasIndex("Key")
                        .IsUnique()
                        .HasDatabaseName("ai_embedding_cache_key_key");

                    b.HasIndex("Model")
                        .HasDatabaseName("ai_embedding_cache_model_idx");

                    b.ToTable("ai_embedding_cache", (string)null);
                });

            modelBuilder.Entity("Pickory.Modules.AiGateway.Entities.AiSetting", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasDefaultValue("singleton")
                        .HasColumnName("id");

                    b.Property<int>("ChatDailyLimitAnon")
                        .HasColumnType("integer")
                        .HasColumnName("chatDailyLimitAnon");

                    b.Property<int>("ChatDailyLimitUser")
                        .HasColumnType("integer")
                        .HasColumnName("chatDailyLimitUser");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasPrecision(3)
                        .HasColumnType("timestamp(3) without time zone")
                        .HasColumnName("createdAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<double>("MonthlyBudgetUsd")
                        .HasColumnType("double precision")
                        .HasColumnName("monthlyBudgetUsd");

                    b.Property<DateTime>("UpdatedAt")
                        .HasPrecision(3)
                        .HasColumnType("timestamp(3) without time zone")
                        .HasColumnName("updatedAt");

                    b.HasKey("Id")
                        .HasName("ai_settings_pkey");

                    b.ToTable("ai_settings", (string)null);
                });

            modelBuilder.Entity("Pickory.Modules.AiGateway.Entities.AiUsageLog", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("ActorKeyHash")
                        .HasColumnType("text")
                        .HasColumnName("actorKeyHash");

                    b.Property<int>("CachedTokens")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("cachedTokens");

                    b.Property<double>("CostUsd")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("double precision")
                        .HasDefaultValue(0.0)
                        .HasColumnName("costUsd");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasPrecision(3)
                        .HasColumnType("timestamp(3) without time zone")
                        .HasColumnName("createdAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<string>("ErrorCode")
                        .HasColumnType("text")
                        .HasColumnName("errorCode");

                    b.Property<string>("Feature")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("feature");

                    b.Property<int>("InputTokens")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("inputTokens");

                    b.Property<int>("LatencyMs")
                        .HasColumnType("integer")
                        .HasColumnName("latencyMs");

                    b.Property<string>("Model")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("model");

                    b.Property<int>("OutputTokens")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("outputTokens");

                    b.Property<string>("PromptVersion")
                        .HasColumnType("text")
                        .HasColumnName("promptVersion");

                    b.Property<string>("Provider")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("provider");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("status");

                    b.HasKey("Id")
                        .HasName("ai_usage_logs_pkey");

                    b.HasIndex("ActorKeyHash")
                        .HasDatabaseName("ai_usage_logs_actorKeyHash_idx");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("ai_usage_logs_createdAt_idx");

                    b.HasIndex("Feature")
                        .HasDatabaseName("ai_usage_logs_feature_idx");

                    b.HasIndex("Status")
                        .HasDatabaseName("ai_usage_logs_status_idx");

                    b.ToTable("ai_usage_logs", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
