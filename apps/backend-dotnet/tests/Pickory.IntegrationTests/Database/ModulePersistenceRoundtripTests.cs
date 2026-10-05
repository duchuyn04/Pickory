using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Entities;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ModulePersistenceRoundtripTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly DateTime WrittenAt = new(2026, 10, 5, 12, 34, 56, 789, DateTimeKind.Utc);
    private static readonly DateTime ChangedAt = new(2026, 10, 6, 1, 2, 3, 456, DateTimeKind.Utc);

    [Fact]
    public async Task Identity_WritesAndFreshlyReadsEveryTableIncludingEnumsJsonAndNullableUtcDates()
    {
        // Đăng ký dịch vụ phải chọn schema do Entity Framework Core tạo trước giao dịch ghi dữ liệu.
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ toàn bộ dữ liệu thử nghiệm để rollback khi assertion lỗi.
        await using var transaction = await context.Database.BeginTransactionAsync();
        using var before = JsonDocument.Parse("{\"name\":\"old\",\"enabled\":false}");
        using var after = JsonDocument.Parse("{\"enabled\":true,\"name\":\"new\",\"nested\":{\"values\":[1,null,2]}}");
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Email = $"dn01-{uniqueSuffix}@example.test", EmailVerified = true,
            PasswordHash = "test-only-password-hash", Name = "Người kiểm thử", Phone = "0900000000",
            Kind = UserKind.Customer, LockedAt = null, DeletedAt = WrittenAt,
            CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };
        // Đăng ký bản ghi cha trước để EF Core cấp khóa chính cho các bản ghi phụ thuộc.
        context.Set<User>().Add(user);
        var role = new Role
        {
            Code = $"dn01-{uniqueSuffix}", Name = "Test role", IsSystem = true, CreatedAt = WrittenAt
        };
        context.Set<Role>().Add(role);
        var permission = new RolePermission { RoleId = role.Id, Permission = "catalog:read" };
        var userRole = new UserRole { UserId = user.Id, RoleId = role.Id };
        var session = new Session
        {
            TokenHash = $"session-{uniqueSuffix}", UserId = user.Id, ExpiresAt = ChangedAt, CreatedAt = WrittenAt
        };
        var oauth = new OAuthAccount
        {
            Provider = "test-provider", ProviderUserId = uniqueSuffix, UserId = user.Id, CreatedAt = WrittenAt
        };
        var emailToken = new EmailToken
        {
            TokenHash = $"email-{uniqueSuffix}", UserId = user.Id, Purpose = EmailTokenPurpose.Invite,
            ExpiresAt = ChangedAt, UsedAt = WrittenAt, CreatedAt = WrittenAt
        };
        var consent = new ConsentRecord
        {
            AnonymousId = uniqueSuffix, UserId = user.Id, Personalization = true, Marketing = false,
            PolicyVersion = "dn01", CreatedAt = WrittenAt
        };
        var audit = new AuditLog
        {
            ActorId = user.Id, Action = "dn01:roundtrip", TargetType = "user", TargetId = user.Id,
            Before = before, After = after, CreatedAt = WrittenAt
        };

        context.Set<RolePermission>().Add(permission);
        context.Set<UserRole>().Add(userRole);
        context.Set<Session>().Add(session);
        context.Set<OAuthAccount>().Add(oauth);
        context.Set<EmailToken>().Add(emailToken);
        context.Set<ConsentRecord>().Add(consent);
        context.Set<AuditLog>().Add(audit);
        // Ghi vào DB thật để phát hiện sai kiểu cột hoặc khóa ngoại.
        await context.SaveChangesAsync();
        AssertPersistedIds(user.Id, role.Id, session.Id, oauth.Id, emailToken.Id, consent.Id, audit.Id);

        // `Clear` buộc EF đọc từ DB, không dùng giá trị được theo dõi trong bộ nhớ.
        context.ChangeTracker.Clear();
        user = await AssertFreshValuesAsync(context, user);
        await AssertFreshValuesAsync(context, role);
        await AssertFreshValuesAsync(context, permission);
        await AssertFreshValuesAsync(context, userRole);
        await AssertFreshValuesAsync(context, session);
        await AssertFreshValuesAsync(context, oauth);
        emailToken = await AssertFreshValuesAsync(context, emailToken);
        consent = await AssertFreshValuesAsync(context, consent);
        audit = await AssertFreshValuesAsync(context, audit);

        // Cập nhật bao phủ hai chiều của thời gian nullable cùng thay đổi nhãn enum.
        user.Kind = UserKind.Staff;
        user.LockedAt = ChangedAt;
        user.DeletedAt = null;
        user.Name = "Tên đã đổi";
        user.UpdatedAt = ChangedAt;
        emailToken.Purpose = EmailTokenPurpose.Reset;
        emailToken.UsedAt = null;
        consent.UserId = null;
        consent.Marketing = true;
        audit.Before = null;
        audit.After = before;
        // Cập nhật trong DB thật để kiểm tra bộ chuyển đổi khi giá trị thay đổi.
        await context.SaveChangesAsync();
        // `Clear` buộc lần đọc tiếp theo kiểm tra dữ liệu đã lưu.
        context.ChangeTracker.Clear();
        await AssertFreshValuesAsync(context, user);
        await AssertFreshValuesAsync(context, emailToken);
        await AssertFreshValuesAsync(context, consent);
        await AssertFreshValuesAsync(context, audit);

        // Rollback giữ dữ liệu có sẵn khi nhiều bài kiểm thử dùng chung DB.
        await transaction.RollbackAsync();
        // `Clear` loại bỏ các bản ghi đã bị rollback khỏi bộ nhớ của EF.
        context.ChangeTracker.Clear();
        Assert.False(await context.Set<User>().AnyAsync(row => row.Id == user.Id));
        Assert.False(await context.Set<AuditLog>().AnyAsync(row => row.Id == audit.Id));
    }

    [Fact]
    public async Task Catalog_WritesAndFreshlyReadsEveryTableIncludingJsonEnumsAndUpdatedAtMilliseconds()
    {
        // Kiểm thử không được tạo schema thuộc quyền quản lý của Prisma.
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ toàn bộ dữ liệu thử nghiệm để rollback khi assertion lỗi.
        await using var transaction = await context.Database.BeginTransactionAsync();
        using var attributes = JsonDocument.Parse("[{\"name\":\"memory\",\"required\":true}]");
        using var description = JsonDocument.Parse("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"text\":\"Mô tả\"}]}");
        using var specifications = JsonDocument.Parse("{\"ram_gb\":16,\"ports\":[\"usb-c\",\"hdmi\"]}");
        using var options = JsonDocument.Parse("[{\"name\":\"Storage\",\"values\":[\"512GB\",\"1TB\"]}]");
        using var optionValues = JsonDocument.Parse("{\"Storage\":\"1TB\"}");
        using var updatedSpecifications = JsonDocument.Parse("{\"ram_gb\":32,\"ports\":[]}");
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var category = new Category
        {
            Name = "Test category", Slug = $"category-{uniqueSuffix}", ParentId = null,
            Position = 2, Attributes = attributes, CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };
        // EF Core cấp khóa chính cho bản ghi cha trước khi các bản ghi phụ thuộc nhận khóa ngoại.
        context.Set<Category>().Add(category);
        var brand = new Brand
        {
            Name = "Test brand", Slug = $"brand-{uniqueSuffix}", LogoPublicId = "dn01/logo",
            CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };
        context.Set<Brand>().Add(brand);
        var product = new Product
        {
            CategoryId = category.Id, BrandId = brand.Id, Name = "Test product", Slug = $"product-{uniqueSuffix}",
            ShortDesc = "Short description", Description = description, Specs = specifications, Options = options,
            WarrantyMonths = 24, Status = ProductStatus.Active, MinPrice = 123456, InStock = true,
            CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };
        context.Set<Product>().Add(product);
        var sku = new Sku
        {
            ProductId = product.Id, Code = $"SKU-{uniqueSuffix}", OptionValues = optionValues,
            Price = 123456, CompareAtPrice = 234567, Stock = 5, Enabled = true,
            WeightGrams = 1500, LengthCm = 30, WidthCm = 20, HeightCm = 2,
            CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };
        context.Set<Sku>().Add(sku);
        var image = new ProductImage
        {
            ProductId = product.Id, SkuId = sku.Id, PublicId = "dn01/image", Alt = "Ảnh kiểm thử",
            Position = 3, CreatedAt = WrittenAt
        };
        var history = new ProductSlugHistory
        {
            ProductId = product.Id, OldSlug = $"old-{uniqueSuffix}", CreatedAt = WrittenAt
        };
        var movement = new InventoryMovement
        {
            SkuId = sku.Id, Delta = -2, Type = InventoryMovementType.OrderReserve,
            OrderId = $"order-{uniqueSuffix}", ActorId = "test-actor", Reason = "dn01", CreatedAt = WrittenAt
        };
        var job = new ImageDeletionJob
        {
            PublicId = "dn01/deleted", Status = "pending", Attempts = 2, MaxAttempts = 8,
            NextRetryAt = ChangedAt, LastError = "Test error", CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };

        context.Set<ProductImage>().Add(image);
        context.Set<ProductSlugHistory>().Add(history);
        context.Set<InventoryMovement>().Add(movement);
        context.Set<ImageDeletionJob>().Add(job);
        // Ghi vào DB thật để phát hiện sai kiểu cột hoặc khóa ngoại.
        await context.SaveChangesAsync();
        AssertPersistedIds(category.Id, brand.Id, product.Id, sku.Id, image.Id, history.Id, movement.Id, job.Id);
        // `Clear` buộc lần đọc tiếp theo kiểm tra dữ liệu đã lưu.
        context.ChangeTracker.Clear();
        await AssertFreshValuesAsync(context, category);
        await AssertFreshValuesAsync(context, brand);
        product = await AssertFreshValuesAsync(context, product);
        sku = await AssertFreshValuesAsync(context, sku);
        image = await AssertFreshValuesAsync(context, image);
        await AssertFreshValuesAsync(context, history);
        movement = await AssertFreshValuesAsync(context, movement);
        job = await AssertFreshValuesAsync(context, job);

        // Lần ghi thứ hai kiểm tra `jsonb` đổi, giá trị null và thời gian do ứng dụng cung cấp.
        product.Status = ProductStatus.Hidden;
        product.Specs = updatedSpecifications;
        product.MinPrice = null;
        product.InStock = false;
        product.UpdatedAt = ChangedAt;
        sku.CompareAtPrice = null;
        sku.Stock = 0;
        sku.Enabled = false;
        sku.WeightGrams = null;
        sku.LengthCm = null;
        sku.WidthCm = null;
        sku.HeightCm = null;
        sku.UpdatedAt = ChangedAt;
        image.SkuId = null;
        movement.Type = InventoryMovementType.Return;
        movement.Delta = 2;
        movement.OrderId = null;
        movement.ActorId = null;
        job.Status = "completed";
        job.LastError = null;
        job.NextRetryAt = WrittenAt;
        job.UpdatedAt = ChangedAt;
        // Cập nhật trong DB thật để kiểm tra bộ chuyển đổi khi giá trị thay đổi.
        await context.SaveChangesAsync();
        // `Clear` buộc lần đọc tiếp theo kiểm tra dữ liệu đã lưu.
        context.ChangeTracker.Clear();
        await AssertFreshValuesAsync(context, product);
        await AssertFreshValuesAsync(context, sku);
        await AssertFreshValuesAsync(context, image);
        await AssertFreshValuesAsync(context, movement);
        await AssertFreshValuesAsync(context, job);

        // Sản phẩm kiểm thử không được tồn tại sau khi giao dịch bị hủy.
        await transaction.RollbackAsync();
        // `Clear` loại bỏ các bản ghi đã bị rollback khỏi bộ nhớ của EF.
        context.ChangeTracker.Clear();
        Assert.False(await context.Set<Product>().AnyAsync(row => row.Id == product.Id));
        Assert.False(await context.Set<ImageDeletionJob>().AnyAsync(row => row.Id == job.Id));
    }

    [Fact]
    public async Task AiGateway_RoundtripsDoublePrecisionArraysIncludingEmptyAndNullWithoutChangingExistingSettings()
    {
        // Context được tiêm chỉ được dùng DB test do resolver hiện có chọn.
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ toàn bộ dữ liệu thử nghiệm để rollback khi assertion lỗi.
        await using var transaction = await context.Database.BeginTransactionAsync();
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var usage = new AiUsageLog
        {
            Feature = "dn01", Provider = "test-provider", Model = "test-model", PromptVersion = "v1",
            InputTokens = 123, OutputTokens = 45, CachedTokens = 12, CostUsd = 0.0123456789,
            LatencyMs = 321, Status = "success", ErrorCode = null, ActorKeyHash = uniqueSuffix, CreatedAt = WrittenAt
        };
        var settings = new AiSetting
        {
            Id = $"dn01-{uniqueSuffix}", MonthlyBudgetUsd = 123.456789,
            ChatDailyLimitUser = 20, ChatDailyLimitAnon = 5, CreatedAt = WrittenAt, UpdatedAt = WrittenAt
        };
        var embedding = new AiEmbeddingCache
        {
            Key = $"embedding-{uniqueSuffix}", Model = "test-embedding",
            Embedding = [0.123456789012345, -0.987654321012345, 0, 1], CreatedAt = WrittenAt
        };
        var emptyEmbedding = new AiEmbeddingCache
        {
            Key = $"empty-{uniqueSuffix}", Model = "test-embedding", Embedding = [], CreatedAt = WrittenAt
        };
        var nullEmbedding = new AiEmbeddingCache
        {
            Key = $"null-{uniqueSuffix}", Model = "test-embedding", Embedding = null, CreatedAt = WrittenAt
        };

        // ID riêng của cấu hình tránh thay đổi bản ghi `singleton` có sẵn.
        context.Set<AiUsageLog>().Add(usage);
        context.Set<AiSetting>().Add(settings);
        context.Set<AiEmbeddingCache>().AddRange(embedding, emptyEmbedding, nullEmbedding);
        // Ghi vào DB thật để phát hiện sai kiểu cột hoặc giá trị mảng.
        await context.SaveChangesAsync();
        AssertPersistedIds(usage.Id, embedding.Id, emptyEmbedding.Id, nullEmbedding.Id);
        // `Clear` buộc lần đọc tiếp theo kiểm tra dữ liệu đã lưu.
        context.ChangeTracker.Clear();
        usage = await AssertFreshValuesAsync(context, usage);
        settings = await AssertFreshValuesAsync(context, settings);
        embedding = await AssertFreshValuesAsync(context, embedding);
        await AssertFreshValuesAsync(context, emptyEmbedding);
        await AssertFreshValuesAsync(context, nullEmbedding);

        // Độ chính xác double cùng giá trị null phải được giữ nguyên sau cập nhật.
        usage.CostUsd = 0.987654321;
        usage.PromptVersion = null;
        usage.ErrorCode = "test-error";
        usage.Status = "error";
        settings.MonthlyBudgetUsd = 456.789012;
        settings.ChatDailyLimitAnon = 0;
        settings.UpdatedAt = ChangedAt;
        embedding.Embedding = [-0.123456789012345, 0.987654321012345];
        // Cập nhật trong DB thật để kiểm tra bộ chuyển đổi khi giá trị thay đổi.
        await context.SaveChangesAsync();
        // `Clear` buộc lần đọc tiếp theo kiểm tra dữ liệu đã lưu.
        context.ChangeTracker.Clear();
        await AssertFreshValuesAsync(context, usage);
        await AssertFreshValuesAsync(context, settings);
        await AssertFreshValuesAsync(context, embedding);

        // Rollback loại bỏ dữ liệu kiểm thử nhưng không đổi ngân sách có sẵn.
        await transaction.RollbackAsync();
        // `Clear` loại bỏ các bản ghi đã bị rollback khỏi bộ nhớ của EF.
        context.ChangeTracker.Clear();
        Assert.False(await context.Set<AiSetting>().AnyAsync(row => row.Id == settings.Id));
        Assert.False(await context.Set<AiUsageLog>().AnyAsync(row => row.Id == usage.Id));
    }

    private static void AssertPersistedIds(params string[] identifiers)
    {
        Assert.All(identifiers, identifier =>
            Assert.True(Guid.TryParse(identifier, out var parsed) && parsed != Guid.Empty,
                "Persisted Prisma text IDs must be nonempty UUID strings."));
        Assert.Equal(identifiers.Length, identifiers.Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task<TEntity> AssertFreshValuesAsync<TEntity>(DbContext context, TEntity expected)
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))!;
        var primaryKey = entityType.FindPrimaryKey()!;
        var keyValues = primaryKey.Properties.Select(property => property.PropertyInfo!.GetValue(expected)).ToArray();
        // `FindAsync` phải đọc bản ghi mới vì đối tượng mong đợi đã bị tách khỏi context.
        var actual = await context.Set<TEntity>().FindAsync(keyValues);
        Assert.NotNull(actual);
        Assert.NotSame(expected, actual);
        foreach (var property in entityType.GetProperties())
        {
            var propertyInfo = property.PropertyInfo
                ?? throw new InvalidOperationException($"Roundtrip requires a CLR property for {property.Name}.");
            var expectedValue = propertyInfo.GetValue(expected);
            var actualValue = propertyInfo.GetValue(actual);
            var fieldName = $"{typeof(TEntity).Name}.{property.Name}";
            switch (expectedValue)
            {
                case DateTime expectedDate:
                    // Ticks UTC (Coordinated Universal Time) chính xác phát hiện mất mili giây hoặc lệch múi giờ.
                    var actualDate = Assert.IsType<DateTime>(actualValue);
                    Assert.True(expectedDate.Ticks == actualDate.Ticks, $"{fieldName}: expected {expectedDate:O}, actual {actualDate:O}");
                    Assert.Equal(DateTimeKind.Utc, actualDate.Kind);
                    break;
                case JsonDocument expectedJson:
                    // `jsonb` có thể đổi thứ tự khóa nên so sánh giá trị JSON thay vì văn bản.
                    var actualJson = Assert.IsType<JsonDocument>(actualValue);
                    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson.RootElement.GetRawText()),
                        JsonNode.Parse(actualJson.RootElement.GetRawText())), $"{fieldName}: JSON value differs.");
                    break;
                case Array expectedArray:
                    var actualArray = Assert.IsAssignableFrom<Array>(actualValue);
                    Assert.Equal(expectedArray.Cast<object>(), actualArray.Cast<object>());
                    break;
                default:
                    Assert.True(Equals(expectedValue, actualValue),
                        $"{fieldName}: expected [{expectedValue ?? "<null>"}], actual [{actualValue ?? "<null>"}]");
                    break;
            }
        }

        return actual;
    }
}
