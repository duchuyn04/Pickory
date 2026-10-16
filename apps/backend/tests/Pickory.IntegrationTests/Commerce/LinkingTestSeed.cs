using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;

namespace Pickory.IntegrationTests.Commerce;

// Dữ liệu dùng chung cho test COM-21. Đơn và đánh giá ghi thẳng vào DB, để test chọn được email liên hệ và người sở hữu.
internal sealed class LinkingTestSeed(IntegrationDatabaseFixture database)
{
    private readonly List<string> _orderIds = [];
    private readonly List<string> _reviewIds = [];

    public IReadOnlyList<string> OrderIds => _orderIds;

    // Ghi một đơn đã giao. `rawToken` là token trong link xem đơn, và DB chỉ lưu bản băm của token đó.
    public async Task<string> AddOrderAsync(string contactEmail, string? userId, string rawToken)
    {
        var orderId = Guid.NewGuid().ToString();
        var code = "LK" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var order = new Order
        {
            Id = orderId,
            Code = code,
            UserId = userId,
            AnonymousId = "anon-" + code,
            Contact = JsonSerializer.SerializeToDocument(new
            {
                name = "Nguyễn Văn An", phone = "0901234567", email = contactEmail, note = "Giao giờ hành chính",
            }),
            ShippingAddress = JsonSerializer.SerializeToDocument(new
            {
                provinceCode = "1000001", provinceName = "Hồ Chí Minh", wardCode = "1003646", wardName = "Phường Bến Nghé", line = "12 Phố Test",
            }),
            PaymentMethod = PaymentMethod.Cod,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Paid,
            Flags = [],
            Subtotal = 200_000,
            ShippingFee = 30_000,
            Total = 230_000,
            AccessTokenHash = OrderAccessToken.Hash(rawToken),
            IdempotencyKey = "key-" + code,
            CreatedAt = DateTime.UtcNow,
        };

        await using var commerce = database.CreateCommerceContext();
        commerce.Orders.Add(order);
        await commerce.SaveChangesAsync();
        _orderIds.Add(orderId);
        return orderId;
    }

    public async Task<string> AddReviewAsync(string productId, string orderId, string? userId, int rating, string body)
    {
        var review = new Review
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = productId,
            UserId = userId,
            OrderId = orderId,
            Rating = rating,
            Body = body,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await using var commerce = database.CreateCommerceContext();
        commerce.Reviews.Add(review);
        await commerce.SaveChangesAsync();
        _reviewIds.Add(review.Id);
        return review.Id;
    }

    public async Task<Order> ReadOrderAsync(string orderId)
    {
        await using var commerce = database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().SingleAsync(order => order.Id == orderId);
    }

    public async Task<Review> ReadReviewAsync(string reviewId)
    {
        await using var commerce = database.CreateCommerceContext();
        return await commerce.Reviews.AsNoTracking().SingleAsync(review => review.Id == reviewId);
    }

    // Xóa đánh giá trước đơn vì khóa ngoại chặn xóa đơn còn đánh giá.
    public async Task CleanupAsync()
    {
        await using var commerce = database.CreateCommerceContext();
        await commerce.Reviews.Where(review => _reviewIds.Contains(review.Id)).ExecuteDeleteAsync();
        await commerce.OrderStatusHistory.Where(history => _orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
        await commerce.OrderItems.Where(item => _orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
        await commerce.Orders.Where(order => _orderIds.Contains(order.Id)).ExecuteDeleteAsync();
    }
}
