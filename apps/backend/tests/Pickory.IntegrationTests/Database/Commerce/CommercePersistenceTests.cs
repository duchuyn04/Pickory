using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database.Commerce;

// COM-03: schema commerce ghi và đọc lại đúng, và DB tự từ chối dữ liệu trùng hoặc mồ côi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommercePersistenceTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly DateTime WrittenAt = new(2026, 10, 11, 8, 7, 6, 543, DateTimeKind.Utc);

    private static Order NewOrder(string suffix) => new()
    {
        Code = $"PK{suffix[..8].ToUpperInvariant()}",
        AnonymousId = $"anon-{suffix}",
        Contact = JsonDocument.Parse("""{"name":"Nguyễn Văn A","phone":"0900000000","email":"a@example.test"}"""),
        ShippingAddress = JsonDocument.Parse("""{"provinceCode":"79","provinceName":"Thành phố Hồ Chí Minh"}"""),
        PaymentMethod = PaymentMethod.Sepay,
        Status = OrderStatus.PendingPayment,
        PaymentStatus = PaymentStatus.Unpaid,
        Subtotal = 1_500_000,
        ShippingFee = 32_000,
        ShippingDiscount = 32_000,
        Total = 1_500_000,
        AccessTokenHash = $"hash-{suffix}",
        IdempotencyKey = $"key-{suffix}",
        CreatedAt = WrittenAt,
    };

    private static OrderItem NewOrderItem(string orderId) => new()
    {
        OrderId = orderId,
        SkuId = "sku-1",
        ProductId = "product-1",
        NameSnapshot = "Laptop thử",
        SkuCodeSnapshot = "LAP-001",
        OptionValues = JsonDocument.Parse("""{"color":"Đen"}"""),
        UnitPrice = 750_000,
        Quantity = 2,
    };

    [Fact]
    public async Task Commerce_WritesAndFreshlyReadsEveryTableIncludingEnumsJsonbAndArrays()
    {
        // Context từ đăng ký dịch vụ phải dùng đúng DB test và cấu hình module thật.
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ dữ liệu thử để rollback kể cả khi assertion lỗi.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var order = NewOrder(suffix);
            order.Flags = ["underpaid", "invoice_requested"];
            order.PaymentDeadline = WrittenAt.AddMinutes(30);
            order.InvoiceInfo = JsonDocument.Parse("""{"companyName":"Công ty thử","taxCode":"0312345678"}""");
            order.Items.Add(NewOrderItem(string.Empty));
            order.StatusHistory.Add(new OrderStatusHistory
            {
                FromStatus = OrderStatus.PendingPayment, ToStatus = OrderStatus.PendingConfirmation,
                Reason = "Tiền về", CreatedAt = WrittenAt,
            });
            var cart = new Cart { UserId = $"user-{suffix}", UpdatedAt = WrittenAt };
            cart.Items.Add(new CartItem { SkuId = "sku-1", Quantity = 3 });
            var address = new SavedAddress
            {
                UserId = $"user-{suffix}", Label = "Nhà", Name = "Nguyễn Văn A", Phone = "0900000000",
                Address = JsonDocument.Parse("""{"provinceCode":"79"}"""), IsDefault = true,
            };
            var settings = new CommerceSettings
            {
                Id = $"settings-{suffix}", FreeShippingThreshold = 0, CodMaxAmount = 20_000_000,
                MaxQuantityPerSku = 5, MaxCartLines = 20, SepayAccountNumber = "0123456789", SepayBankCode = "MB",
                UpdatedAt = WrittenAt,
            };
            context.AddRange(order, cart, address, settings);
            await context.SaveChangesAsync();

            var transactionRow = new PaymentTransaction
            {
                Provider = "sepay", ProviderTxId = $"tx-{suffix}", Amount = 1_500_000, Content = order.Code,
                Raw = JsonDocument.Parse("""{"id":1}"""), OrderId = order.Id, MatchStatus = "matched",
            };
            var review = new Review
            {
                ProductId = "product-1", UserId = $"user-{suffix}", OrderId = order.Id, Rating = 5,
                Body = "Tốt", UpdatedAt = WrittenAt,
            };
            context.AddRange(transactionRow, review);
            await context.SaveChangesAsync();
            var orderId = order.Id;

            // Đọc mới từ PostgreSQL để tránh giá trị được giữ trong bộ theo dõi Entity Framework Core.
            context.ChangeTracker.Clear();
            var persisted = await context.Orders
                .Include(value => value.Items).Include(value => value.StatusHistory)
                .SingleAsync(value => value.Id == orderId);
            Assert.Equal(OrderStatus.PendingPayment, persisted.Status);
            Assert.Equal(PaymentStatus.Unpaid, persisted.PaymentStatus);
            Assert.Equal(PaymentMethod.Sepay, persisted.PaymentMethod);
            Assert.Equal(["underpaid", "invoice_requested"], persisted.Flags);
            Assert.Equal(1_500_000, persisted.Subtotal);
            Assert.Equal(WrittenAt, persisted.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, persisted.CreatedAt.Kind);
            Assert.Equal(WrittenAt.AddMinutes(30), persisted.PaymentDeadline);
            Assert.Equal("Nguyễn Văn A", persisted.Contact.RootElement.GetProperty("name").GetString());
            Assert.Equal("0312345678", persisted.InvoiceInfo!.RootElement.GetProperty("taxCode").GetString());
            var item = Assert.Single(persisted.Items);
            Assert.Equal("Đen", item.OptionValues.RootElement.GetProperty("color").GetString());
            Assert.Null(item.ImagePublicId);
            var history = Assert.Single(persisted.StatusHistory);
            Assert.Equal(OrderStatus.PendingConfirmation, history.ToStatus);

            var persistedCart = await context.Carts.Include(value => value.Items).SingleAsync(value => value.Id == cart.Id);
            Assert.Equal(3, Assert.Single(persistedCart.Items).Quantity);
            Assert.Null(persistedCart.AnonymousId);

            Assert.True((await context.SavedAddresses.SingleAsync(value => value.Id == address.Id)).IsDefault);
            Assert.Equal("matched", (await context.PaymentTransactions.SingleAsync(value => value.Id == transactionRow.Id)).MatchStatus);
            var persistedReview = await context.Reviews.SingleAsync(value => value.Id == review.Id);
            Assert.Equal(0, persistedReview.ReportCount);
            Assert.Null(persistedReview.HiddenAt);

            // Ngưỡng miễn phí bằng 0 là giá trị hợp lệ và phải được ghi nguyên, không bị thay bằng mặc định nào.
            var persistedSettings = await context.Settings.SingleAsync(value => value.Id == settings.Id);
            Assert.Equal(0, persistedSettings.FreeShippingThreshold);

            // Nhãn enum trong DB là snake_case. SQL thuần đọc được nhãn đó.
            var rawStatus = await context.Database
                .SqlQuery<string>($"SELECT status::text AS \"Value\" FROM orders WHERE id = {orderId}")
                .SingleAsync();
            Assert.Equal("pending_payment", rawStatus);
            var rawMethod = await context.Database
                .SqlQuery<string>($"SELECT \"paymentMethod\"::text AS \"Value\" FROM orders WHERE id = {orderId}")
                .SingleAsync();
            Assert.Equal("sepay", rawMethod);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    // Mọi cột tiền phải là `integer`, không phải `bigint` hay `numeric`, theo quyết định của plan.
    public async Task MoneyColumns_AreInteger()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();

        var types = await context.Database
            .SqlQuery<string>($"""
                SELECT table_name || '.' || column_name || ':' || data_type AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND (table_name, column_name) IN (
                    ('orders', 'subtotal'), ('orders', 'shippingFee'), ('orders', 'shippingDiscount'), ('orders', 'total'),
                    ('order_items', 'unitPrice'), ('payment_transactions', 'amount'),
                    ('commerce_settings', 'freeShippingThreshold'), ('commerce_settings', 'codMaxAmount'))
                """)
            .ToListAsync();

        Assert.Equal(8, types.Count);
        Assert.All(types, entry => Assert.EndsWith(":integer", entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Orders_DuplicateCode_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            var first = NewOrder(suffix);
            var second = NewOrder(suffix);
            // Cùng mã đơn nhưng khác khóa idempotency, để chỉ ràng buộc của mã đơn có thể từ chối.
            second.IdempotencyKey = $"other-{suffix}";
            context.AddRange(first, second);
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("orders_code_key", error.ConstraintName);
    }

    [Fact]
    public async Task Orders_DuplicateIdempotencyKey_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            var first = NewOrder(suffix);
            var second = NewOrder(suffix);
            // Cùng khóa idempotency nhưng khác mã đơn, để chỉ ràng buộc của khóa có thể từ chối.
            second.Code = $"PK{Guid.NewGuid():N}"[..10].ToUpperInvariant();
            context.AddRange(first, second);
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("orders_idempotencyKey_key", error.ConstraintName);
    }

    [Fact]
    public async Task PaymentTransactions_DuplicateProviderTxId_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var providerTxId = $"tx-{Guid.NewGuid():N}";
            for (var copy = 0; copy < 2; copy++)
            {
                context.PaymentTransactions.Add(new PaymentTransaction
                {
                    Provider = "sepay", ProviderTxId = providerTxId, Amount = 100_000, Content = "PKAAAAAAAA",
                    Raw = JsonDocument.Parse("{}"), MatchStatus = "unmatched",
                });
            }
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("payment_transactions_providerTxId_key", error.ConstraintName);
    }

    [Fact]
    public async Task Reviews_SecondReviewOfSameUserAndProduct_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var order = NewOrder(Guid.NewGuid().ToString("N"));
            context.Orders.Add(order);
            var userId = $"user-{Guid.NewGuid():N}";
            for (var copy = 0; copy < 2; copy++)
            {
                context.Reviews.Add(new Review
                {
                    ProductId = "product-1", UserId = userId, Order = order, Rating = 4, UpdatedAt = WrittenAt,
                });
            }
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("reviews_productId_userId_key", error.ConstraintName);
    }

    [Fact]
    // Đánh giá đã ẩn danh (`UserId` rỗng) không bị ràng buộc duy nhất chặn, vì PostgreSQL coi mỗi `NULL` là một giá trị riêng.
    public async Task Reviews_ManyAnonymizedReviewsOfSameProduct_AreAllowed()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var order = NewOrder(Guid.NewGuid().ToString("N"));
            context.Orders.Add(order);
            for (var copy = 0; copy < 2; copy++)
            {
                context.Reviews.Add(new Review { ProductId = "product-1", UserId = null, Order = order, Rating = 3, UpdatedAt = WrittenAt });
            }

            Assert.Equal(3, await context.SaveChangesAsync());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Carts_TwoCartsOfSameUser_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var userId = $"user-{Guid.NewGuid():N}";
            context.Carts.Add(new Cart { UserId = userId, UpdatedAt = WrittenAt });
            context.Carts.Add(new Cart { UserId = userId, UpdatedAt = WrittenAt });
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("carts_userId_key", error.ConstraintName);
    }

    [Fact]
    public async Task OrderItems_UnknownOrder_IsRejectedByForeignKey()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
            context.OrderItems.Add(NewOrderItem(Guid.NewGuid().ToString())));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal("order_items_orderId_fkey", error.ConstraintName);
    }

    [Fact]
    // Đơn không bao giờ bị xóa. DB từ chối xóa đơn còn dòng hàng, thay vì xóa dây chuyền cả chứng từ.
    public async Task Orders_DeleteWithItems_IsRejectedByForeignKey()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var order = NewOrder(Guid.NewGuid().ToString("N"));
            order.Items.Add(NewOrderItem(string.Empty));
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            var orderId = order.Id;

            var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database
                .ExecuteSqlAsync($"DELETE FROM orders WHERE id = {orderId}"));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal("order_items_orderId_fkey", exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    // Xóa giỏ thì dòng của giỏ biến mất theo. `UserDeletedEvent` chỉ cần xóa giỏ.
    public async Task Carts_Delete_RemovesItsItems()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var cart = new Cart { AnonymousId = $"anon-{Guid.NewGuid():N}", UpdatedAt = WrittenAt };
            cart.Items.Add(new CartItem { SkuId = "sku-1", Quantity = 1 });
            context.Carts.Add(cart);
            await context.SaveChangesAsync();
            var cartId = cart.Id;

            await context.Database.ExecuteSqlAsync($"DELETE FROM carts WHERE id = {cartId}");

            var remaining = await context.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cart_items WHERE \"cartId\" = {cartId}")
                .SingleAsync();
            Assert.Equal(0, remaining);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    // Cùng SKU không xuất hiện hai lần trong một giỏ. Khóa chính ghép chặn dòng thứ hai.
    public async Task CartItems_DuplicateSkuInSameCart_IsRejectedByPrimaryKey()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var cart = new Cart { AnonymousId = $"anon-{Guid.NewGuid():N}", UpdatedAt = WrittenAt };
            context.Carts.Add(cart);
            await context.SaveChangesAsync();
            var cartId = cart.Id;
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO cart_items (\"cartId\", \"skuId\", quantity) VALUES ({cartId}, 'sku-1', 1)");

            // SQL thuần bỏ qua bộ theo dõi của Entity Framework Core, nên lỗi đến từ khóa chính của DB.
            var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
                $"INSERT INTO cart_items (\"cartId\", \"skuId\", quantity) VALUES ({cartId}, 'sku-1', 2)"));

            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            Assert.Equal("cart_items_pkey", exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    // Quy ước của repo: tên bảng snake_case, tên cột camelCase, tên enum PascalCase, nhãn enum snake_case.
    public async Task CommerceSchema_FollowsRepoNamingConvention()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();

        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT table_name || '.' || column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name IN ('carts', 'cart_items', 'orders', 'order_items', 'order_status_history',
                                     'payment_transactions', 'saved_addresses', 'reviews', 'commerce_settings')
                """)
            .ToListAsync();
        var enumLabels = await context.Database
            .SqlQuery<string>($"""
                SELECT t.typname || ':' || e.enumlabel AS "Value"
                FROM pg_type t JOIN pg_enum e ON e.enumtypid = t.oid JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'public' AND t.typname IN ('OrderStatus', 'PaymentStatus', 'PaymentMethod')
                """)
            .ToListAsync();

        // Cột camelCase thì không có dấu gạch dưới. Cột `idempotencyKey` có mặt chứng minh đúng tên, không chỉ đúng kiểu.
        Assert.All(columns, column => Assert.DoesNotContain('_', column.Split('.')[1]));
        Assert.Contains("orders.idempotencyKey", columns);
        Assert.Contains("payment_transactions.providerTxId", columns);
        Assert.Contains("order_status_history.fromStatus", columns);
        Assert.Contains("OrderStatus:pending_confirmation", enumLabels);
        Assert.Contains("PaymentStatus:refund_pending", enumLabels);
        Assert.Contains("PaymentMethod:cod", enumLabels);
        Assert.Equal(13, enumLabels.Count);
    }

    [Fact]
    // Bốn cột số của cấu hình không có giá trị mặc định ở DB. Giá trị mặc định do mã nguồn cung cấp, nên dòng thiếu cột bị DB từ chối.
    public async Task CommerceSettings_NumericColumns_HaveNoDatabaseDefault()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();

        var columnsWithDefault = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'commerce_settings'
                  AND column_name IN ('freeShippingThreshold', 'codMaxAmount', 'maxQuantityPerSku', 'maxCartLines')
                  AND column_default IS NOT NULL
                """)
            .ToListAsync();

        Assert.Empty(columnsWithDefault);
    }

    [Fact]
    // `commerce_settings` không có cột khóa API. Khóa SePay chỉ nằm trong biến môi trường.
    public async Task CommerceSettings_HasNoApiKeyOrSecretColumn()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();

        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'commerce_settings'
                """)
            .ToListAsync();

        Assert.DoesNotContain(columns, column => column.Contains("key", StringComparison.OrdinalIgnoreCase)
            || column.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || column.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("sepayAccountNumber", columns);
        Assert.Contains("maxQuantityPerSku", columns);
    }

    private async Task<PostgresException> SaveAndCaptureConstraintErrorAsync(Action<CommerceDbContext> arrange)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch ngoài giữ mọi thay đổi để rollback. Lỗi ràng buộc làm giao dịch hỏng nên không dùng tiếp sau đó.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            arrange(context);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            return Assert.IsType<PostgresException>(exception.InnerException);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
