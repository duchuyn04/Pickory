using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

// Contracts cũng có kiểu `Shipment` cho bên gọi. Test này kiểm entity của module.
using Shipment = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.IntegrationTests.Database.Shipping;

// SHIP-02: schema shipping ghi và đọc lại đúng, và DB tự từ chối dữ liệu trùng hoặc mồ côi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShippingPersistenceTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly DateTime WrittenAt = new(2026, 10, 9, 8, 7, 6, 543, DateTimeKind.Utc);

    private static Shipment NewShipment(string suffix) => new()
    {
        Ref = $"ship02-ref-{suffix}",
        TrackingCode = $"ship02-track-{suffix}",
        Fee = 32000,
        CodAmount = 1500000,
        Parcel = new Parcel(1850, 30, 20, 14),
        Status = ShipmentStatus.DeliveryFailed,
        RawStatus = "delivery_fail",
        CreatedAt = WrittenAt,
        UpdatedAt = WrittenAt,
    };

    [Fact]
    public async Task Shipping_WritesAndFreshlyReadsEveryTableIncludingEnumsAndJsonbParcel()
    {
        // Context từ đăng ký dịch vụ phải dùng đúng DB test và cấu hình module thật.
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ dữ liệu thử để rollback kể cả khi assertion lỗi.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var shipment = NewShipment(suffix);
            shipment.Events.Add(new ShipmentEvent
            {
                Status = ShipmentStatus.DeliveryFailed, RawStatus = "delivery_fail",
                OccurredAt = WrittenAt, Source = ShipmentEventSource.Webhook,
            });
            var province = new AddressProvince { Code = $"p-{suffix}", Name = "Thành phố Hà Nội", SyncedAt = WrittenAt };
            province.Wards.Add(new AddressWard { Code = $"w-{suffix}", Name = "Phường Ba Đình", SyncedAt = WrittenAt });
            var settings = new ShippingSettings
            {
                Id = $"settings-{suffix}", SenderName = "Pickory", SenderPhone = "0900000000",
                ProvinceCode = province.Code, ProvinceName = province.Name, WardCode = "w", WardName = "Phường Ba Đình",
                Line = "1 Phố Test", DefaultServiceTypeId = 53320, UpdatedAt = WrittenAt,
            };
            context.AddRange(shipment, province, settings);
            await context.SaveChangesAsync();
            var shipmentId = shipment.Id;

            // Đọc mới từ PostgreSQL để tránh giá trị được giữ trong bộ theo dõi Entity Framework Core.
            context.ChangeTracker.Clear();
            var persisted = await context.Shipments.Include(value => value.Events).SingleAsync(value => value.Id == shipmentId);
            Assert.Equal(new Parcel(1850, 30, 20, 14), persisted.Parcel);
            Assert.Equal(ShipmentStatus.DeliveryFailed, persisted.Status);
            Assert.Equal("ghn", persisted.Carrier);
            Assert.Equal(32000, persisted.Fee);
            Assert.Equal(1500000, persisted.CodAmount);
            Assert.Equal(WrittenAt, persisted.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, persisted.CreatedAt.Kind);
            var persistedEvent = Assert.Single(persisted.Events);
            Assert.Equal(ShipmentEventSource.Webhook, persistedEvent.Source);
            Assert.Equal(ShipmentStatus.DeliveryFailed, persistedEvent.Status);

            var persistedProvince = await context.AddressProvinces.Include(value => value.Wards)
                .SingleAsync(value => value.Code == province.Code);
            Assert.True(persistedProvince.IsActive);
            Assert.Equal("Phường Ba Đình", Assert.Single(persistedProvince.Wards).Name);
            Assert.True(persistedProvince.Wards.Single().IsActive);

            var persistedSettings = await context.Settings.SingleAsync(value => value.Id == settings.Id);
            Assert.Equal(53320, persistedSettings.DefaultServiceTypeId);

            // Nhãn enum trong DB là snake_case, và `parcel` là jsonb có khóa camelCase như tên cột. SQL thuần đọc được cả hai.
            var rawStatus = await context.Database
                .SqlQuery<string>($"SELECT status::text AS \"Value\" FROM shipments WHERE id = {shipmentId}")
                .SingleAsync();
            Assert.Equal("delivery_failed", rawStatus);
            var rawWeight = await context.Database
                .SqlQuery<string>($"SELECT parcel->>'weightGrams' AS \"Value\" FROM shipments WHERE id = {shipmentId}")
                .SingleAsync();
            Assert.Equal("1850", rawWeight);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Shipments_DuplicateRef_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            var first = NewShipment(suffix);
            var second = NewShipment(suffix);
            // Cùng `ref` nhưng khác mã vận đơn, để chỉ ràng buộc của `ref` có thể từ chối.
            second.TrackingCode = $"ship02-track-other-{suffix}";
            context.AddRange(first, second);
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("shipments_ref_key", error.ConstraintName);
    }

    [Fact]
    public async Task Shipments_DuplicateTrackingCode_IsRejectedByUniqueConstraint()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            var first = NewShipment(suffix);
            var second = NewShipment(suffix);
            // Cùng mã vận đơn nhưng khác `ref`, để chỉ ràng buộc của `trackingCode` có thể từ chối.
            second.Ref = $"ship02-ref-other-{suffix}";
            context.AddRange(first, second);
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("shipments_trackingCode_key", error.ConstraintName);
    }

    [Fact]
    public async Task ShipmentEvents_UnknownShipment_IsRejectedByForeignKey()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
            context.ShipmentEvents.Add(new ShipmentEvent
            {
                ShipmentId = Guid.NewGuid().ToString(), Status = ShipmentStatus.InTransit, RawStatus = "storing",
                OccurredAt = WrittenAt, Source = ShipmentEventSource.Poll,
            }));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal("shipment_events_shipmentId_fkey", error.ConstraintName);
    }

    [Fact]
    public async Task AddressWards_UnknownProvince_IsRejectedByForeignKey()
    {
        var error = await SaveAndCaptureConstraintErrorAsync(context =>
            context.AddressWards.Add(new AddressWard
            {
                Code = $"w-{Guid.NewGuid():N}", ProvinceCode = "khong-ton-tai", Name = "Phường mồ côi", SyncedAt = WrittenAt,
            }));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal("address_wards_provinceCode_fkey", error.ConstraintName);
    }

    [Fact]
    // Quy ước của repo: tên bảng snake_case, tên cột camelCase, tên enum PascalCase, nhãn enum snake_case.
    public async Task ShippingSchema_FollowsRepoNamingConvention()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();

        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT table_name || '.' || column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name IN ('shipments', 'shipment_events', 'address_provinces', 'address_wards', 'shipping_settings')
                """)
            .ToListAsync();
        var enumNames = await context.Database
            .SqlQuery<string>($"""
                SELECT typname AS "Value" FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'public' AND t.typtype = 'e' AND typname LIKE 'Shipment%'
                """)
            .ToListAsync();

        // Cột camelCase thì không có dấu gạch dưới. Cột `trackingCode` có mặt chứng minh đúng tên, không chỉ đúng kiểu.
        Assert.All(columns, column => Assert.DoesNotContain('_', column.Split('.')[1]));
        Assert.Contains("shipments.trackingCode", columns);
        Assert.Contains("shipment_events.shipmentId", columns);
        Assert.Contains("address_wards.provinceCode", columns);
        Assert.Equal(["ShipmentEventSource", "ShipmentStatus"], enumNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    // Địa chỉ chỉ có hai cấp. Không bảng shipping nào được có cột quận hoặc huyện, kể cả `districtCode`, `districtId`, `district_code`.
    public async Task ShippingTables_HaveNoDistrictColumn()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();

        var districtColumns = await context.Database
            .SqlQuery<string>($"""
                SELECT table_name || '.' || column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND column_name ILIKE '%district%'
                  AND table_name IN ('shipments', 'shipment_events', 'address_provinces', 'address_wards', 'shipping_settings')
                """)
            .ToListAsync();

        Assert.Empty(districtColumns);
    }

    [Fact]
    // `shipping_settings` không có cột token hay `ghnShopId`. Hai giá trị này chỉ nằm trong biến môi trường.
    public async Task ShippingSettings_HasNoSecretOrShopIdColumn()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();

        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'shipping_settings'
                """)
            .ToListAsync();

        Assert.DoesNotContain(columns, column => column.Contains("token", StringComparison.OrdinalIgnoreCase)
            || column.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || column.Contains("shopId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("defaultServiceTypeId", columns);
    }

    private async Task<PostgresException> SaveAndCaptureConstraintErrorAsync(Action<ShippingDbContext> arrange)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
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
