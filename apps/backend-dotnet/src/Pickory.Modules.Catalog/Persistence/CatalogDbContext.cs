using Microsoft.EntityFrameworkCore;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sku> Skus => Set<Sku>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductSlugHistory> ProductSlugHistories => Set<ProductSlugHistory>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<ImageDeletionJob> ImageDeletionJobs => Set<ImageDeletionJob>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Tách lịch sử khỏi Prisma. Npgsql phải dùng kiểu enum hiện có, không dùng số nguyên.
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_catalog");
            postgres.MapEnum<ProductStatus>("ProductStatus");
            postgres.MapEnum<InventoryMovementType>("InventoryMovementType");
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ UTC (Coordinated Universal Time) để các module không áp múi giờ máy chủ khi đọc DB.
        PrismaModelConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Giữ thứ tự nhãn PostgreSQL vì phép so sánh enum dùng thứ tự khai báo, không dùng thứ tự chữ cái.
        modelBuilder.HasPostgresEnum("ProductStatus", ["draft", "active", "hidden", "discontinued"]);
        modelBuilder.HasPostgresEnum("InventoryMovementType", ["manual", "order_reserve", "order_release", "return"]);

        // Giữ `ParentId` nullable để danh mục gốc không cần tham chiếu danh mục khác.
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(value => value.Id);
            // Sinh UUID (Universally Unique Identifier) khi thêm bản ghi để tránh cấp phát lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.ParentId).IsRequired(false);
            entity.Property(value => value.Name).IsRequired();
            entity.Property(value => value.Slug).IsRequired();
            entity.Property(value => value.Position).HasDefaultValue(0);
            entity.Property(value => value.Attributes).HasColumnType("jsonb");
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.UpdatedAt).IsRequired();

            entity.HasIndex(value => value.Slug)
                .IsUnique()
                .HasDatabaseName("categories_slug_key");

            // Chặn xóa danh mục đang có con để cây danh mục không mất liên kết.
            entity.HasOne(value => value.Parent)
                .WithMany(value => value.Children)
                .HasForeignKey(value => value.ParentId)
                .HasConstraintName("categories_parentId_fkey")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Giữ logo nullable để thương hiệu chưa có ảnh vẫn đọc được.
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("brands");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.Name).IsRequired();
            entity.Property(value => value.Slug).IsRequired();
            entity.Property(value => value.LogoPublicId).IsRequired(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.UpdatedAt).IsRequired();

            entity.HasIndex(value => value.Slug)
                .IsUnique()
                .HasDatabaseName("brands_slug_key");
        });

        // Giữ `jsonb` cho nội dung có cấu trúc để Node và EF đọc cùng dữ liệu.
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.CategoryId).IsRequired();
            entity.Property(value => value.BrandId).IsRequired();
            entity.Property(value => value.Name).IsRequired();
            entity.Property(value => value.Slug).IsRequired();
            entity.Property(value => value.ShortDesc).IsRequired();
            entity.Property(value => value.Description).HasColumnType("jsonb");
            entity.Property(value => value.Specs).HasColumnType("jsonb");
            entity.Property(value => value.Options).HasColumnType("jsonb");
            entity.Property(value => value.WarrantyMonths).HasDefaultValue(12);
            entity.Property(value => value.Status).HasDefaultValue(ProductStatus.Draft);
            entity.Property(value => value.MinPrice).IsRequired(false);
            entity.Property(value => value.InStock).HasDefaultValue(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.UpdatedAt).IsRequired();

            entity.HasIndex(value => value.Slug)
                .IsUnique()
                .HasDatabaseName("products_slug_key");

            entity.HasIndex(value => new { value.CategoryId, value.Status })
                .HasDatabaseName("products_categoryId_status_idx");

            // Giữ phương thức GIN (Generalized Inverted Index) để tìm thuộc tính JSON theo index hiện có.
            entity.HasIndex(value => value.Specs)
                .HasMethod("gin")
                .HasDatabaseName("products_specs_gin_idx");

            entity.HasIndex(value => value.MinPrice)
                .HasDatabaseName("products_minPrice_idx");

            entity.HasIndex(value => value.CreatedAt)
                .HasDatabaseName("products_createdAt_idx");

            // Chặn xóa danh mục đang có sản phẩm để sản phẩm không mất danh mục.
            entity.HasOne(value => value.Category)
                .WithMany(value => value.Products)
                .HasForeignKey(value => value.CategoryId)
                .HasConstraintName("products_categoryId_fkey")
                .OnDelete(DeleteBehavior.Restrict);

            // Chặn xóa thương hiệu đang có sản phẩm để sản phẩm không mất thương hiệu.
            entity.HasOne(value => value.Brand)
                .WithMany(value => value.Products)
                .HasForeignKey(value => value.BrandId)
                .HasConstraintName("products_brandId_fkey")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Giữ ràng buộc tồn kho không âm của migration Node, dù Prisma không biểu diễn ràng buộc này.
        modelBuilder.Entity<Sku>(entity =>
        {
            entity.ToTable("skus", table =>
            {
                table.HasCheckConstraint("skus_stock_non_negative", "\"stock\" >= 0");
            });
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.ProductId).IsRequired();
            entity.Property(value => value.Code).IsRequired();
            entity.Property(value => value.OptionValues).HasColumnType("jsonb");
            entity.Property(value => value.Price).IsRequired();
            entity.Property(value => value.CompareAtPrice).IsRequired(false);
            entity.Property(value => value.Stock).HasDefaultValue(0);
            entity.Property(value => value.Enabled).HasDefaultValue(true);
            entity.Property(value => value.WeightGrams).IsRequired(false);
            entity.Property(value => value.LengthCm).IsRequired(false);
            entity.Property(value => value.WidthCm).IsRequired(false);
            entity.Property(value => value.HeightCm).IsRequired(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.UpdatedAt).IsRequired();

            entity.HasIndex(value => value.Code)
                .IsUnique()
                .HasDatabaseName("skus_code_key");

            entity.HasIndex(value => value.ProductId)
                .HasDatabaseName("skus_productId_idx");

            // Xóa biến thể cùng sản phẩm để không còn biến thể tham chiếu sản phẩm đã xóa.
            entity.HasOne(value => value.Product)
                .WithMany(value => value.Skus)
                .HasForeignKey(value => value.ProductId)
                .HasConstraintName("skus_productId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Giữ `SkuId` nullable để ảnh có thể thuộc sản phẩm mà không thuộc biến thể.
        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("product_images");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.ProductId).IsRequired();
            entity.Property(value => value.SkuId).IsRequired(false);
            entity.Property(value => value.PublicId).IsRequired();
            entity.Property(value => value.Alt).IsRequired();
            entity.Property(value => value.Position).HasDefaultValue(0);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(value => new { value.ProductId, value.Position })
                .HasDatabaseName("product_images_productId_position_idx");

            // Xóa ảnh cùng sản phẩm để không còn ảnh tham chiếu sản phẩm đã xóa.
            entity.HasOne(value => value.Product)
                .WithMany(value => value.Images)
                .HasForeignKey(value => value.ProductId)
                .HasConstraintName("product_images_productId_fkey")
                .OnDelete(DeleteBehavior.Cascade);

            // Giữ ảnh khi biến thể bị xóa. DB chỉ bỏ liên kết biến thể.
            entity.HasOne(value => value.Sku)
                .WithMany(value => value.Images)
                .HasForeignKey(value => value.SkuId)
                .HasConstraintName("product_images_skuId_fkey")
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Giữ đường dẫn cũ duy nhất để một đường dẫn không chuyển hướng đến hai sản phẩm.
        modelBuilder.Entity<ProductSlugHistory>(entity =>
        {
            entity.ToTable("product_slug_history");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.OldSlug).IsRequired();
            entity.Property(value => value.ProductId).IsRequired();
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(value => value.OldSlug)
                .IsUnique()
                .HasDatabaseName("product_slug_history_oldSlug_key");

            entity.HasIndex(value => value.ProductId)
                .HasDatabaseName("product_slug_history_productId_idx");

            // Xóa lịch sử cùng sản phẩm để không còn chuyển hướng đến sản phẩm đã xóa.
            entity.HasOne(value => value.Product)
                .WithMany(value => value.SlugHistory)
                .HasForeignKey(value => value.ProductId)
                .HasConstraintName("product_slug_history_productId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cho phép `OrderId` nullable để biến động thủ công không cần đơn hàng.
        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.ToTable("inventory_movements");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.SkuId).IsRequired();
            entity.Property(value => value.Delta).IsRequired();
            entity.Property(value => value.Type).IsRequired();
            entity.Property(value => value.OrderId).IsRequired(false);
            entity.Property(value => value.ActorId).IsRequired(false);
            entity.Property(value => value.Reason).IsRequired();
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(value => new { value.SkuId, value.CreatedAt })
                .HasDatabaseName("inventory_movements_skuId_createdAt_idx");

            // Giữ khóa duy nhất theo đơn hàng để cùng loại biến động không được ghi hai lần.
            entity.HasIndex(value => new { value.OrderId, value.SkuId, value.Type })
                .IsUnique()
                .HasDatabaseName("inventory_movements_orderId_skuId_type_key");

            // Chặn xóa biến thể đã có biến động để giữ lịch sử tồn kho.
            entity.HasOne(value => value.Sku)
                .WithMany(value => value.Movements)
                .HasForeignKey(value => value.SkuId)
                .HasConstraintName("inventory_movements_skuId_fkey")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Giữ mặc định retry của DB để EF không đổi cấu trúc hàng đợi Node.
        modelBuilder.Entity<ImageDeletionJob>(entity =>
        {
            entity.ToTable("image_deletion_jobs");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.PublicId).IsRequired();
            entity.Property(value => value.Status).HasDefaultValue("pending");
            entity.Property(value => value.Attempts).HasDefaultValue(0);
            entity.Property(value => value.MaxAttempts).HasDefaultValue(8);
            entity.Property(value => value.NextRetryAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.LastError).IsRequired(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.UpdatedAt).IsRequired();

            entity.HasIndex(value => new { value.Status, value.NextRetryAt })
                .HasDatabaseName("image_deletion_jobs_status_nextRetryAt_idx");
        });

        // Áp dụng quy ước Prisma cho tên cột và khóa chính sau khi định nghĩa toàn bộ thực thể.
        PrismaModelConventions.Apply(modelBuilder);
    }
}
