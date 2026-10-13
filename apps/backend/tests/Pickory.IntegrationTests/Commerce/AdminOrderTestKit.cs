using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.IntegrationTests.Commerce;

// Dữ liệu đơn hàng cho test route back-office. Test ghi thẳng vào DB để điều khiển được ngày tạo, trạng thái và cờ.
internal sealed class AdminOrderTestKit(IntegrationDatabaseFixture database)
{
    // Xóa mọi đơn trước và sau mỗi test. Danh sách back-office đếm cả bảng, nên đơn của test khác làm lệch tổng.
    public async Task ResetAsync()
    {
        await using var commerce = database.CreateCommerceContext();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(commerce);
        await commerce.PaymentTransactions.ExecuteDeleteAsync();
        await commerce.OrderStatusHistory.ExecuteDeleteAsync();
        await commerce.OrderItems.ExecuteDeleteAsync();
        await commerce.Orders.ExecuteDeleteAsync();
    }

    public async Task<Order> AddOrderAsync(
        string code,
        DateTime createdAtUtc,
        OrderStatus status = OrderStatus.PendingConfirmation,
        PaymentStatus paymentStatus = PaymentStatus.Unpaid,
        PaymentMethod paymentMethod = PaymentMethod.Cod,
        IReadOnlyList<string>? flags = null,
        int lineCount = 1,
        string? id = null,
        string recipientName = "Nguyễn Văn An",
        string recipientPhone = "0901234567",
        bool withInvoice = false,
        string recipientEmail = "an@example.com",
        bool omitRecipientEmail = false)
    {
        var orderId = id ?? Guid.NewGuid().ToString();
        var order = new Order
        {
            Id = orderId,
            Code = code,
            AnonymousId = "anon-" + code,
            // Đơn cũ hoặc đơn nhập tay có thể thiếu email, nên kit cho bỏ hẳn khóa `email` khỏi `contact`.
            Contact = omitRecipientEmail
                ? Json(new { name = recipientName, phone = recipientPhone, note = "Giao giờ hành chính" })
                : Json(new { name = recipientName, phone = recipientPhone, email = recipientEmail, note = "Giao giờ hành chính" }),
            ShippingAddress = Json(new
            {
                provinceCode = "1000001",
                provinceName = "Hà Nội",
                wardCode = "1003646",
                wardName = "Phường Ba Đình",
                line = "12 Phố Test",
            }),
            InvoiceInfo = withInvoice
                ? Json(new { companyName = "Công ty Thử", taxCode = "0312345678", address = "1 Đường Thử", email = "ketoan@example.com" })
                : null,
            PaymentMethod = paymentMethod,
            Status = status,
            PaymentStatus = paymentStatus,
            Flags = flags?.ToList() ?? [],
            Subtotal = 200_000 * lineCount,
            ShippingFee = 30_000,
            ShippingDiscount = 10_000,
            Total = 200_000 * lineCount + 30_000 - 10_000,
            AccessTokenHash = "hash-" + code,
            IdempotencyKey = "key-" + code,
            CreatedAt = createdAtUtc,
        };

        for (var index = 0; index < lineCount; index++)
        {
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                SkuId = $"sku-{code}-{index}",
                ProductId = $"product-{code}-{index}",
                NameSnapshot = $"Sản phẩm {index}",
                SkuCodeSnapshot = $"SKU-{code}-{index}",
                OptionValues = Json(new { Màu = "Đỏ" }),
                UnitPrice = 100_000,
                Quantity = 2,
            });
        }

        await using var commerce = database.CreateCommerceContext();
        commerce.Orders.Add(order);
        await commerce.SaveChangesAsync();
        return order;
    }

    public async Task AddStatusHistoryAsync(
        string orderId, OrderStatus from, OrderStatus to, DateTime createdAtUtc, string? actorId = null, string? reason = null)
    {
        await using var commerce = database.CreateCommerceContext();
        commerce.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            ActorId = actorId,
            Reason = reason,
            CreatedAt = createdAtUtc,
        });
        await commerce.SaveChangesAsync();
    }

    private static JsonDocument Json(object value) => JsonSerializer.SerializeToDocument(value);
}
