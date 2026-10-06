using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Events;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Database.Catalog;

// Port từ `events.int.test.ts` và phần `emitAfterCommit` của `events.test.ts` ở bản Node (CAT-03).
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CatalogTransactionTests(IntegrationDatabaseFixture database)
{
    private static string NewSlug(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    // Dispatcher thật với một handler do test cung cấp. Handler chạy như khi API chạy thật.
    private static IEventDispatcher CreateDispatcher(Func<ProductChangedEvent, Task> onProductChanged)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<ProductChangedEvent>>(new DelegateHandler(onProductChanged));
        var provider = services.BuildServiceProvider();
        return new InProcessEventDispatcher(provider, NullLogger<InProcessEventDispatcher>.Instance);
    }

    [Fact]
    // Transaction lỗi thì dữ liệu bị hủy và sự kiện không được phát.
    public async Task RunAsync_RollbackDropsDataAndEvents()
    {
        var calls = 0;
        var dispatcher = CreateDispatcher(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        var slug = NewSlug("brand-rollback");
        await using var catalog = database.CreateCatalogContext();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogTransaction.RunAsync<int>(catalog, dispatcher, async events =>
        {
            catalog.Brands.Add(new Brand { Name = "Thương hiệu lỗi", Slug = slug });
            await catalog.SaveChangesAsync();
            events.Emit(new ProductChangedEvent("prod-rollback", ProductChange.Created));
            throw new InvalidOperationException("Cố ý gây lỗi rollback");
        }, CancellationToken.None));

        Assert.Equal("Cố ý gây lỗi rollback", error.Message);
        await using var verify = database.CreateCatalogContext();
        Assert.False(await verify.Brands.AnyAsync(brand => brand.Slug == slug));
        Assert.Equal(0, calls);
    }

    [Fact]
    // Sự kiện phát đúng một lần, sau khi kết nối khác đã đọc được dữ liệu vừa commit.
    public async Task RunAsync_PublishesOnceAfterCommit()
    {
        var calls = 0;
        var visibleFromOtherConnection = false;
        var slug = NewSlug("brand-commit");
        var dispatcher = CreateDispatcher(async _ =>
        {
            calls++;
            await using var other = database.CreateCatalogContext();
            visibleFromOtherConnection = await other.Brands.AnyAsync(brand => brand.Slug == slug);
        });
        await using var catalog = database.CreateCatalogContext();

        var result = await CatalogTransaction.RunAsync(catalog, dispatcher, async events =>
        {
            catalog.Brands.Add(new Brand { Name = "Thương hiệu thành công", Slug = slug });
            await catalog.SaveChangesAsync();
            events.Emit(new ProductChangedEvent("prod-commit", ProductChange.Created));
            return "ok";
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(1, calls);
        Assert.True(visibleFromOtherConnection);
    }

    [Fact]
    // Bên ngoài tự quản lý transaction: hủy thì không flush, commit thì flush đúng một lần.
    public async Task Collector_WorksWithExternalTransaction()
    {
        var calls = 0;
        var dispatcher = CreateDispatcher(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        var rollbackCollector = new CatalogEventCollector(dispatcher);
        await using (var catalog = database.CreateCatalogContext())
        await using (var transaction = await catalog.Database.BeginTransactionAsync())
        {
            catalog.Brands.Add(new Brand { Name = "Thương hiệu bên ngoài 1", Slug = NewSlug("brand-external-fail") });
            await catalog.SaveChangesAsync();
            rollbackCollector.Emit(new ProductChangedEvent("prod-external-fail", ProductChange.Created));
            await transaction.RollbackAsync();
            rollbackCollector.Discard();
        }

        Assert.Equal(0, calls);

        var commitCollector = new CatalogEventCollector(dispatcher);
        await using (var catalog = database.CreateCatalogContext())
        await using (var transaction = await catalog.Database.BeginTransactionAsync())
        {
            catalog.Brands.Add(new Brand { Name = "Thương hiệu bên ngoài 2", Slug = NewSlug("brand-external-success") });
            await catalog.SaveChangesAsync();
            commitCollector.Emit(new ProductChangedEvent("prod-external-success", ProductChange.Created));
            await transaction.CommitAsync();
        }

        await commitCollector.FlushAsync(CancellationToken.None);
        await commitCollector.FlushAsync(CancellationToken.None);
        Assert.Equal(1, calls);
    }

    private sealed class DelegateHandler(Func<ProductChangedEvent, Task> handle) : IDomainEventHandler<ProductChangedEvent>
    {
        public Task HandleAsync(ProductChangedEvent @event, CancellationToken cancellationToken = default)
        {
            return handle(@event);
        }
    }
}
