using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Calendar;

namespace Pickory.Modules.Commerce.Demo;

// Dựng đơn mẫu cho báo cáo doanh thu trên DB dev. Mọi đơn mang cờ `demo_order` để lệnh `--reset` xóa đúng chúng.
internal static class DemoOrderSeeder
{
    public const string DemoFlag = "demo_order";
    public const string CodePrefix = "DEMO-";

    private const int MonthCount = 6;
    private const int OrdersPerMonth = 5;
    private const int MaxBrands = 6;
    private const int ShippingFee = 30_000;
    private const int SepayShippingDiscount = 10_000;

    private enum DemoOrderKind
    {
        SepayPaid,
        CodPaid,
        CustomerCancelled,
        ShopCancelled,
        PaymentExpired,
    }

    private sealed record DemoProduct(
        string ProductId,
        string Name,
        string SkuId,
        string SkuCode,
        IReadOnlyDictionary<string, string> OptionValues,
        int UnitPrice);

    private sealed record DemoOrderPlan(
        string Code,
        DemoOrderKind Kind,
        DateOnly Day,
        DemoProduct Product,
        int Quantity);

    private sealed record BuiltOrder(Order Order, PaymentTransaction? Payment);

    // Tạo các đơn còn thiếu. Đơn đã có cùng mã bị bỏ qua, nên chạy lệnh lần hai không tạo thêm đơn trùng.
    public static async Task<DemoOrderSeedResult> SeedAsync(
        CommerceDbContext database,
        ICatalogReader catalogReader,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync(catalogReader, cancellationToken);
        var today = VietnamCalendar.ToVietnamDate(now.UtcDateTime);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var plans = BuildPlans(currentMonth, today, products);

        var existingCodes = await database.Orders.AsNoTracking()
            .Where(order => order.Flags.Contains(DemoFlag))
            .Select(order => order.Code)
            .ToListAsync(cancellationToken);
        var existing = existingCodes.ToHashSet(StringComparer.Ordinal);
        var missing = plans.Where(plan => !existing.Contains(plan.Code)).ToList();
        if (missing.Count == 0)
        {
            return new DemoOrderSeedResult(0, plans.Count);
        }

        var built = missing.Select(plan => BuildOrder(plan, now.UtcDateTime)).ToList();

        // Ghi đơn và giao dịch trong một transaction. Lỗi giữa chừng không để lại đơn mẫu dở dang.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Orders.AddRange(built.Select(item => item.Order));
        await database.SaveChangesAsync(cancellationToken);

        // Giao dịch SePay thêm sau đơn để khóa ngoại luôn có đơn đích.
        var payments = built.Where(item => item.Payment is not null).Select(item => item.Payment!).ToList();
        if (payments.Count > 0)
        {
            database.PaymentTransactions.AddRange(payments);
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DemoOrderSeedResult(missing.Count, plans.Count - missing.Count);
    }

    // Chỉ xóa đơn mang cờ `demo_order`. Đơn thật không có cờ này nên không bị chạm tới.
    public static async Task<int> ResetAsync(CommerceDbContext database, CancellationToken cancellationToken)
    {
        // Đọc danh sách trong transaction để tập đơn bị xóa không đổi giữa chừng.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var orderIds = await database.Orders.AsNoTracking()
            .Where(order => order.Flags.Contains(DemoFlag))
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);
        if (orderIds.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        // Xóa bảng con trước vì khóa ngoại chặn xóa đơn còn dòng phụ thuộc.
        await database.PaymentTransactions
            .Where(item => orderIds.Contains(item.OrderId ?? string.Empty))
            .ExecuteDeleteAsync(cancellationToken);
        await database.OrderStatusHistory
            .Where(item => orderIds.Contains(item.OrderId))
            .ExecuteDeleteAsync(cancellationToken);
        await database.OrderItems
            .Where(item => orderIds.Contains(item.OrderId))
            .ExecuteDeleteAsync(cancellationToken);
        await database.Orders
            .Where(order => orderIds.Contains(order.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return orderIds.Count;
    }

    // Lấy một sản phẩm còn hàng cho mỗi thương hiệu. Nhiều thương hiệu để biểu đồ tròn của báo cáo có nghĩa.
    private static async Task<IReadOnlyList<DemoProduct>> LoadProductsAsync(
        ICatalogReader catalogReader, CancellationToken cancellationToken)
    {
        // Một trang lớn nhất là đủ cho dữ liệu mẫu của `catalog:seed`.
        var summaries = await catalogReader.ListProductsAsync(new ProductFilters(), 48, cancellationToken);
        var products = new List<DemoProduct>();

        foreach (var brandGroup in summaries.GroupBy(item => item.Brand.Id, StringComparer.Ordinal))
        {
            foreach (var summary in brandGroup)
            {
                var detail = await catalogReader.GetProductAsync(summary.Id, cancellationToken);
                // Chỉ lấy SKU còn hàng. Đơn mẫu không được bán một SKU đã hết.
                var sku = detail?.Skus.FirstOrDefault(item => item.InStock);
                if (detail is null || sku is null)
                {
                    continue;
                }

                products.Add(new DemoProduct(
                    detail.Id, detail.Name, sku.Id, sku.Code, sku.OptionValues, sku.Price));
                break;
            }

            if (products.Count == MaxBrands)
            {
                break;
            }
        }

        // Thiếu dữ liệu mẫu thì dừng, không tạo 30 đơn chỉ có một thương hiệu.
        if (products.Count < 2)
        {
            throw new InvalidOperationException(
                "Catalog cần ít nhất hai thương hiệu có sản phẩm còn hàng. Chạy catalog:seed trước.");
        }

        return products;
    }

    // Dựng kế hoạch cho 6 tháng lịch gần nhất. Mỗi tháng có hai đơn đã thanh toán và ba đơn hủy.
    private static List<DemoOrderPlan> BuildPlans(
        DateOnly currentMonth, DateOnly today, IReadOnlyList<DemoProduct> products)
    {
        var plans = new List<DemoOrderPlan>(MonthCount * OrdersPerMonth);

        for (var monthOffset = MonthCount - 1; monthOffset >= 0; monthOffset--)
        {
            var monthStart = currentMonth.AddMonths(-monthOffset);
            for (var kindIndex = 0; kindIndex < OrdersPerMonth; kindIndex++)
            {
                // Rải đơn trong tháng. Tháng hiện tại chưa tới ngày đó thì dồn về hôm nay.
                var day = monthStart.AddDays(4 + kindIndex * 4);
                if (day > today)
                {
                    day = today;
                }

                var kind = (DemoOrderKind)kindIndex;
                var product = products[(monthOffset * OrdersPerMonth + kindIndex) % products.Count];
                var quantity = 1 + ((monthOffset + kindIndex) % 2);
                var code = CodePrefix
                    + monthStart.ToString("yyyyMM", CultureInfo.InvariantCulture)
                    + "-"
                    + (kindIndex + 1).ToString("00", CultureInfo.InvariantCulture);
                plans.Add(new DemoOrderPlan(code, kind, day, product, quantity));
            }
        }

        return plans;
    }

    private static BuiltOrder BuildOrder(DemoOrderPlan plan, DateTime nowUtc)
    {
        var (createdUtc, paidUtc, cancelledUtc) = ResolveTimes(plan, nowUtc);
        var subtotal = plan.Product.UnitPrice * plan.Quantity;
        var shippingDiscount = plan.Kind == DemoOrderKind.SepayPaid ? SepayShippingDiscount : 0;
        var total = subtotal + ShippingFee - shippingDiscount;
        var isExpired = plan.Kind == DemoOrderKind.PaymentExpired;
        var isPaid = plan.Kind is DemoOrderKind.SepayPaid or DemoOrderKind.CodPaid;
        var flags = new List<string> { DemoFlag };
        if (isExpired)
        {
            flags.Add(OrderReportMarkers.PaymentExpiredFlag);
        }

        // Đơn quá hạn dùng SePay để cờ hết hạn khớp với phương thức chưa thanh toán.
        var paymentMethod = plan.Kind is DemoOrderKind.SepayPaid or DemoOrderKind.PaymentExpired
            ? PaymentMethod.Sepay
            : PaymentMethod.Cod;
        var status = plan.Kind switch
        {
            DemoOrderKind.SepayPaid => OrderStatus.Confirmed,
            DemoOrderKind.CodPaid => OrderStatus.Delivered,
            _ => OrderStatus.Cancelled,
        };
        DateTime? paymentDeadline = plan.Kind switch
        {
            DemoOrderKind.SepayPaid => createdUtc.AddHours(24),
            DemoOrderKind.PaymentExpired => createdUtc.AddMinutes(1),
            _ => null,
        };

        var order = new Order
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = plan.Code,
            AnonymousId = "demo-anonymous-" + plan.Code,
            Contact = Json(new
            {
                name = "Khách demo",
                phone = "0900000000",
                email = "demo@pickory.local",
                note = "Đơn mẫu cho báo cáo",
            }),
            ShippingAddress = Json(new
            {
                provinceCode = "1000001",
                provinceName = "Hà Nội",
                wardCode = "1003646",
                wardName = "Phường Ba Đình",
                line = "12 Phố Demo",
            }),
            InvoiceInfo = null,
            PaymentMethod = paymentMethod,
            Status = status,
            PaymentStatus = isPaid ? PaymentStatus.Paid : PaymentStatus.Unpaid,
            Flags = flags,
            Subtotal = subtotal,
            ShippingFee = ShippingFee,
            ShippingDiscount = shippingDiscount,
            Total = total,
            ShippingFeeSource = ShippingFeeSource.Ghn,
            PaymentDeadline = paymentDeadline,
            AccessTokenHash = "demo-access-" + plan.Code,
            IdempotencyKey = "demo-idempotency-" + plan.Code,
            CreatedAt = createdUtc,
        };

        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid().ToString("N"),
            OrderId = order.Id,
            SkuId = plan.Product.SkuId,
            ProductId = plan.Product.ProductId,
            NameSnapshot = plan.Product.Name,
            SkuCodeSnapshot = plan.Product.SkuCode,
            OptionValues = Json(plan.Product.OptionValues),
            UnitPrice = plan.Product.UnitPrice,
            Quantity = plan.Quantity,
        });

        AddHistory(order, plan.Kind, createdUtc, paidUtc, cancelledUtc, nowUtc);

        var payment = plan.Kind == DemoOrderKind.SepayPaid ? BuildSepayPayment(order, paidUtc) : null;
        return new BuiltOrder(order, payment);
    }

    private static void AddHistory(
        Order order,
        DemoOrderKind kind,
        DateTime createdUtc,
        DateTime paidUtc,
        DateTime cancelledUtc,
        DateTime nowUtc)
    {
        switch (kind)
        {
            case DemoOrderKind.SepayPaid:
                AddHistoryEntry(order, OrderStatus.PendingPayment, OrderStatus.Confirmed, paidUtc, nowUtc,
                    "SePay chuyển khoản.");
                break;
            case DemoOrderKind.CodPaid:
                // Gộp ba bước giao hàng vào lúc tạo để mốc nhận tiền luôn sau chúng.
                AddHistoryEntry(order, OrderStatus.PendingConfirmation, OrderStatus.Confirmed, createdUtc, nowUtc);
                AddHistoryEntry(order, OrderStatus.Confirmed, OrderStatus.Shipping, createdUtc, nowUtc);
                AddHistoryEntry(order, OrderStatus.Shipping, OrderStatus.Delivered, createdUtc, nowUtc);
                // Dòng nhận tiền COD là mốc doanh thu. Báo cáo đọc đúng cặp trạng thái và lý do này.
                AddHistoryEntry(order, OrderStatus.Delivered, OrderStatus.Delivered, paidUtc, nowUtc,
                    OrderReportMarkers.CodReceivedReason);
                break;
            case DemoOrderKind.CustomerCancelled:
                AddHistoryEntry(order, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, cancelledUtc, nowUtc,
                    OrderReportMarkers.CustomerCancellationReason);
                break;
            case DemoOrderKind.ShopCancelled:
                AddHistoryEntry(order, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, cancelledUtc, nowUtc,
                    "Shop hết hàng.");
                break;
            case DemoOrderKind.PaymentExpired:
                AddHistoryEntry(order, OrderStatus.PendingPayment, OrderStatus.Cancelled, cancelledUtc, nowUtc,
                    "Quá hạn thanh toán.");
                break;
        }
    }

    private static void AddHistoryEntry(
        Order order, OrderStatus fromStatus, OrderStatus toStatus, DateTime atUtc, DateTime nowUtc, string? reason = null)
    {
        // Mốc lịch sử không được sau hiện tại, kể cả khi lệnh chạy sát nửa đêm.
        var createdAt = atUtc > nowUtc ? nowUtc : atUtc;
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid().ToString("N"),
            OrderId = order.Id,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            CreatedAt = createdAt,
        });
    }

    private static PaymentTransaction BuildSepayPayment(Order order, DateTime paidUtc) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Provider = "sepay",
        ProviderTxId = "demo-" + order.Code,
        Amount = order.Total,
        Content = order.Code,
        Raw = Json(new { demo = true, orderCode = order.Code }),
        OrderId = order.Id,
        MatchStatus = PaymentMatchStatuses.Matched,
        ReceivedAt = paidUtc,
    };

    private static (DateTime CreatedUtc, DateTime PaidUtc, DateTime CancelledUtc) ResolveTimes(
        DemoOrderPlan plan, DateTime nowUtc)
    {
        var dayStartUtc = VietnamCalendar.DayStartUtc(plan.Day);

        // Đơn tạo lúc 09:00 giờ Việt Nam. Nếu chưa tới giờ đó thì lùi về trước hiện tại.
        var createdUtc = dayStartUtc.AddHours(9);
        if (createdUtc > nowUtc)
        {
            createdUtc = nowUtc.AddMinutes(-10);
        }

        if (createdUtc < dayStartUtc)
        {
            createdUtc = dayStartUtc;
        }

        // Thanh toán 30 phút sau khi tạo, nhưng không vượt quá hiện tại.
        var paidUtc = createdUtc.AddMinutes(30);
        if (paidUtc > nowUtc)
        {
            paidUtc = nowUtc.AddMinutes(-5);
        }

        if (paidUtc < createdUtc)
        {
            paidUtc = createdUtc;
        }

        // Hủy sau khi tạo 5 phút, cùng giới hạn.
        var cancelledUtc = createdUtc.AddMinutes(5);
        if (cancelledUtc > nowUtc)
        {
            cancelledUtc = createdUtc;
        }

        return (createdUtc, paidUtc, cancelledUtc);
    }

    private static JsonDocument Json(object value) => JsonSerializer.SerializeToDocument(value);
}

// Số đơn đã tạo và số đơn bỏ qua vì đã có cùng mã.
internal sealed record DemoOrderSeedResult(int Created, int Skipped);
