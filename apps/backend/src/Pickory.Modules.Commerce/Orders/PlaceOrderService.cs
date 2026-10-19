using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Shared.Configuration;
using Pickory.Shared.Email;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Json;

namespace Pickory.Modules.Commerce.Orders;

// Service đặt đơn tính lại tiền, giữ hàng, tạo đơn có snapshot và xóa dòng đã đặt khỏi giỏ trong một transaction.
public sealed class PlaceOrderService(
    CommerceDbContext database,
    CheckoutPreviewService checkout,
    CartService carts,
    IShippingService shipping,
    IStockService stock,
    IOrderCodeSource codeSource,
    IEventDispatcher dispatcher,
    IEmailSender emailSender,
    AppConfig config,
    ILogger<PlaceOrderService> logger,
    TimeProvider clock)
{
    // Tên ràng buộc unique của cột `orders.idempotency_key`, khai báo ở `OrderConfiguration`.
    private const string IdempotencyKeyConstraint = "orders_idempotencyKey_key";
    private const string UniqueViolationSqlState = "23505";

    // Quyết định 7 của plan: trùng mã thì sinh lại, tối đa 5 lần.
    private const int MaxCodeAttempts = 5;

    private const string TotalChangedMessage = "Giá hoặc giỏ hàng đã thay đổi. Vui lòng xem lại đơn hàng trước khi đặt.";
    private const string InsufficientStockMessage = "Một số sản phẩm không đủ hàng. Vui lòng kiểm tra lại giỏ hàng.";
    private const string KeyConflictMessage = "Khóa đặt đơn này đã được dùng cho một đơn khác.";
    private const string OrderPlacedReason = "Khách đặt đơn.";
    private const string AddressNotDeliverableCode = "ADDRESS_NOT_DELIVERABLE";
    private const string StreetNotInWardMessage =
        "Đơn vị vận chuyển không nhận địa chỉ này. Kiểm tra tên đường có thuộc phường/xã đã chọn không.";

    public async Task<PlaceOrderResult> PlaceAsync(
        CartOwner owner, string idempotencyKey, PlaceOrderInput input, CancellationToken cancellationToken)
    {
        // Tra khóa trước khi tính tiền. Gửi lại cùng khóa không gọi GHN và không giữ hàng lần nữa.
        var existing = await FindOrderByKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, owner);
        }

        // Tính tiền ngoài transaction. Lời gọi GHN có thể chờ mạng, và khóa DB không được giữ trong lúc chờ.
        var calculation = await checkout.CalculateAsync(owner, input.Address, input.PaymentMethod, cancellationToken);

        // Không tin số tiền của client. Tổng tính lại khác tổng khách đã xem thì khách phải xem lại preview.
        if (calculation.Pricing.Total != input.ExpectedTotal)
        {
            throw TotalChanged();
        }

        // Kiểm địa chỉ nhận trước transaction. Lời gọi GHN có thể chờ mạng, nên không được giữ khóa giỏ hay khóa kho.
        await EnsureAddressDeliverableAsync(idempotencyKey, calculation, cancellationToken);

        var events = new DomainEventCollector(dispatcher);
        CreatedOrder created;
        try
        {
            created = await CreateOrderAsync(owner, idempotencyKey, input, calculation, events, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyViolation(exception))
        {
            // Request khác cùng khóa đã commit trước. Transaction của request này đã hủy, nên bỏ sự kiện và dữ liệu đang theo dõi.
            events.Discard();
            database.ChangeTracker.Clear();

            // Đọc lại đơn thắng cuộc. Cùng chủ thì trả đơn đó, khác chủ thì trả 409.
            var winner = await FindOrderByKeyAsync(idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Khóa đặt đơn trùng nhưng không đọc lại được đơn.");
            return Replay(winner, owner);
        }
        catch
        {
            // Lỗi ở bất kỳ bước nào của transaction. Sự kiện đã gom không được phát, vì đơn và tồn kho đã hoàn lại.
            events.Discard();
            throw;
        }

        // Phát sau commit. Handler của module khác không nhận sự kiện về dữ liệu chưa lưu.
        await events.FlushAsync(cancellationToken);

        // Chỉ request tạo đơn mới có dữ liệu thư. Gửi lại cùng khóa và request thua cuộc đồng thời không gửi thư.
        if (created.Confirmation is not null)
        {
            await SendConfirmationAsync(created.Confirmation);
        }

        return created.Result;
    }

    // Chặn đơn khi shipping từ chối địa chỉ nhận. Lý do là tên đường không thuộc phường/xã, hoặc phường/xã không còn trong danh mục.
    private async Task EnsureAddressDeliverableAsync(
        string idempotencyKey, CheckoutCalculation calculation, CancellationToken cancellationToken)
    {
        var check = await shipping.CheckDeliveryAddressAsync(calculation.Destination, calculation.Parcel, cancellationToken);

        // Không kiểm được thì vẫn cho đặt đơn, vì lỗi của GHN không phải lỗi của khách. Log chỉ có khóa đặt đơn, không có dữ liệu khách.
        if (check.Outcome == AddressCheckOutcome.Unverifiable)
        {
            logger.LogWarning(
                "Không kiểm được địa chỉ nhận với đơn vị vận chuyển, đơn vẫn được đặt. Khóa đặt đơn: {IdempotencyKey}.",
                idempotencyKey);
            return;
        }

        if (check.Outcome == AddressCheckOutcome.Rejected)
        {
            throw AddressNotDeliverable(check.Reason);
        }
    }

    // Gửi thư sau commit và sau khi phát sự kiện. Mọi lỗi ở bước này chỉ ghi log, vì đơn đã tạo xong.
    private async Task SendConfirmationAsync(OrderConfirmationInput confirmation)
    {
        try
        {
            var message = OrderConfirmationEmail.Build(confirmation, config.WebOrigin);

            // Không truyền token hủy của request. Khách ngắt kết nối sau commit không được làm mất thư.
            await emailSender.SendAsync(message, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Log chỉ có mã đơn và exception. Token, link và nội dung thư không được vào log.
            logger.LogError(exception, "Gửi email xác nhận đơn {OrderCode} thất bại.", confirmation.OrderCode);
        }
    }

    private async Task<CreatedOrder> CreateOrderAsync(
        CartOwner owner,
        string idempotencyKey,
        PlaceOrderInput input,
        CheckoutCalculation calculation,
        DomainEventCollector events,
        CancellationToken cancellationToken)
    {
        // Mọi lỗi ném trong khối này hủy transaction khi dispose. Đơn, dòng hàng và tồn kho đã giữ cùng hoàn lại.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa giỏ trước mọi việc khác. Hai request cùng giỏ chạy lần lượt, nên request sau thấy giỏ mà request trước đã commit.
        var cart = await carts.LockCartAsync(owner, cancellationToken);

        // Tra lại khóa sau khi giữ khóa giỏ. Request cùng khóa chạy trước đã commit và xóa dòng giỏ, nên request này trả đơn đó.
        var concurrentOrder = await FindOrderByKeyAsync(idempotencyKey, cancellationToken);
        if (concurrentOrder is not null)
        {
            return new CreatedOrder(Replay(concurrentOrder, owner), Confirmation: null);
        }

        // Giỏ có thể đã đổi từ lúc tính tiền, kể cả khi tổng tiền vẫn bằng nhau. Giỏ lệch thì dừng, vì giá đã tính không còn đúng.
        var cartLines = cart?.Items.Select(item => new CartLineInput(item.SkuId, item.Quantity)).ToList() ?? [];
        var pricedLines = calculation.Lines.Select(line => new CartLineInput(line.SkuId, line.Quantity)).ToList();
        if (!OrderCartMatcher.Matches(cartLines, pricedLines))
        {
            throw TotalChanged();
        }

        // Sinh `orderId` trước khi giữ hàng, vì sổ tồn kho dùng `orderId` làm khóa.
        var orderId = Guid.NewGuid().ToString();

        // Truyền transaction của commerce cho catalog. Giữ hàng và tạo đơn cùng commit hoặc cùng rollback.
        var stockItems = calculation.Lines.Select(line => new StockItem(line.SkuId, line.Quantity)).ToList();
        var reservation = await stock.ReserveAsync(
            orderId, stockItems, new StockCallerTransaction(transaction.GetDbTransaction(), events), cancellationToken);
        if (!reservation.Ok)
        {
            throw InsufficientStock(reservation.Shortages);
        }

        var code = await GenerateUniqueCodeAsync(cancellationToken);

        // Cắt về microsecond, vì PostgreSQL lưu timestamp tới microsecond. Giá trị trong response bằng giá trị đã lưu.
        var now = TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
        var accessToken = OrderAccessToken.Create();
        var order = BuildOrder(orderId, code, idempotencyKey, owner, input, calculation, accessToken.Hash, now);
        database.Orders.Add(order);

        // Chỉ xóa các dòng đã đặt. Dòng khác của giỏ, nếu có, vẫn ở lại cho lần đặt sau.
        var orderedSkuIds = calculation.Lines.Select(line => line.SkuId).ToHashSet(StringComparer.Ordinal);
        database.CartItems.RemoveRange(cart!.Items.Where(item => orderedSkuIds.Contains(item.SkuId)));

        // Đổi thời điểm cập nhật để job dọn giỏ cũ biết giỏ còn hoạt động.
        cart.UpdatedAt = now;

        await database.SaveChangesAsync(cancellationToken);

        // Gom sự kiện đặt đơn cùng sự kiện tồn kho. Collector chỉ phát sau khi commit.
        events.Emit(new OrderPlacedEvent(
            order.Id, order.Code, order.UserId, order.AnonymousId, order.Total, order.PaymentMethod));

        await transaction.CommitAsync(cancellationToken);

        // Dữ liệu thư lấy từ đơn đã commit. Token gốc chỉ tồn tại ở đây và trong response của lần tạo đơn.
        var confirmation = new OrderConfirmationInput(
            order.Code,
            input.Contact.Email,
            order.Items.Select(item => new OrderConfirmationLine(item.NameSnapshot, item.Quantity, item.UnitPrice)).ToList(),
            order.Subtotal,
            order.ShippingFee,
            order.ShippingDiscount,
            order.Total,
            order.PaymentMethod,
            accessToken.Token);
        return new CreatedOrder(new PlaceOrderResult(ToResponse(order, accessToken.Token), Created: true), confirmation);
    }

    // Kết quả tạo đơn. `Confirmation` chỉ có khi request này tạo đơn mới và cần gửi thư.
    private sealed record CreatedOrder(PlaceOrderResult Result, OrderConfirmationInput? Confirmation);

    private static Order BuildOrder(
        string orderId,
        string code,
        string idempotencyKey,
        CartOwner owner,
        PlaceOrderInput input,
        CheckoutCalculation calculation,
        string accessTokenHash,
        DateTime now)
    {
        var initialState = OrderInitialState.For(input.PaymentMethod, now);
        var address = calculation.Address;

        // Địa chỉ, liên hệ và hóa đơn là bản chụp tại lúc đặt. Sổ địa chỉ hay hồ sơ đổi sau này không đổi đơn.
        var order = new Order
        {
            Id = orderId,
            Code = code,
            UserId = owner.UserId,
            AnonymousId = owner.AnonymousId,
            Contact = ToJsonDocument(new
            {
                name = input.Contact.Name,
                phone = input.Contact.Phone,
                email = input.Contact.Email,
                note = input.Note,
            }),
            ShippingAddress = ToJsonDocument(new
            {
                provinceCode = address.ProvinceCode,
                provinceName = address.ProvinceName,
                wardCode = address.WardCode,
                wardName = address.WardName,
                line = address.Line,
            }),
            InvoiceInfo = input.Invoice is null
                ? null
                : ToJsonDocument(new
                {
                    companyName = input.Invoice.CompanyName,
                    taxCode = input.Invoice.TaxCode,
                    address = input.Invoice.Address,
                    email = input.Invoice.Email,
                }),
            PaymentMethod = ToPaymentMethod(input.PaymentMethod),
            Status = initialState.Status,
            PaymentStatus = initialState.PaymentStatus,
            PaymentDeadline = initialState.PaymentDeadline,
            Subtotal = calculation.Pricing.Subtotal,
            ShippingFee = calculation.Pricing.ShippingFee,
            ShippingDiscount = calculation.Pricing.ShippingDiscount,
            Total = calculation.Pricing.Total,
            // Ghi nguồn phí để nhân viên biết đơn dùng phí GHN, phí đã lưu hay phí dự phòng. Khách không thấy giá trị này.
            ShippingFeeSource = calculation.FeeSource,
            AccessTokenHash = accessTokenHash,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
        };

        // Tên, mã SKU, tùy chọn, giá và ảnh chụp từ dữ liệu catalog lúc tính tiền, nên đơn không đổi khi catalog đổi.
        foreach (var line in calculation.Lines)
        {
            var sku = calculation.Snapshots[line.SkuId];
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                SkuId = line.SkuId,
                ProductId = sku.ProductId,
                NameSnapshot = sku.ProductName,
                SkuCodeSnapshot = sku.Code,
                OptionValues = ToJsonDocument(sku.OptionValues),
                ImagePublicId = sku.ImagePublicId,
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
            });
        }

        // Dòng lịch sử đầu tiên ghi trạng thái lúc tạo. Chưa có trạng thái trước, nên `FromStatus` bằng `ToStatus`.
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = orderId,
            FromStatus = initialState.Status,
            ToStatus = initialState.Status,
            ActorId = null,
            Reason = OrderPlacedReason,
            CreatedAt = now,
        });

        return order;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            var code = codeSource.Next();

            // Kiểm trước khi ghi. Lỗi unique của PostgreSQL làm hỏng cả transaction, nên không để nó là cách phát hiện trùng.
            var taken = await database.Orders.AsNoTracking().AnyAsync(order => order.Code == code, cancellationToken);
            if (!taken)
            {
                return code;
            }
        }

        // Năm mã liền nhau đều trùng cho thấy nguồn ngẫu nhiên hỏng. Dừng thay vì thử mãi.
        throw new InvalidOperationException($"Không sinh được mã đơn không trùng sau {MaxCodeAttempts} lần thử.");
    }

    private Task<Order?> FindOrderByKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        database.Orders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey, cancellationToken);

    private static PlaceOrderResult Replay(Order existing, CartOwner owner)
    {
        // Khóa của chủ khác không được trả đơn. Phản hồi cũng không mang mã đơn, để khóa đoán được không lộ đơn của người khác.
        if (!IsSameOwner(existing, owner))
        {
            throw new AppException("IDEMPOTENCY_KEY_CONFLICT", 409, KeyConflictMessage);
        }

        // Server không còn token gốc, nên lần gửi lại trả `null` cho token.
        return new PlaceOrderResult(ToResponse(existing, accessToken: null), Created: false);
    }

    private static bool IsSameOwner(Order order, CartOwner owner)
    {
        // Khách đã đăng nhập so theo `UserId`, nên đổi trình duyệt vẫn là cùng chủ.
        if (owner.UserId is not null)
        {
            return order.UserId == owner.UserId;
        }

        // Khách vãng lai so theo `AnonymousId`. Đơn của tài khoản không thuộc về khách vãng lai dù cùng `AnonymousId`.
        return order.UserId is null && order.AnonymousId == owner.AnonymousId;
    }

    private static PlaceOrderResponse ToResponse(Order order, string? accessToken) =>
        new(
            order.Code,
            OrderLabels.ToLabel(order.Status),
            OrderLabels.ToLabel(order.PaymentStatus),
            OrderLabels.ToLabel(order.PaymentMethod),
            order.Subtotal,
            order.ShippingFee,
            order.ShippingDiscount,
            order.Total,
            order.PaymentDeadline,
            accessToken);

    private static bool IsIdempotencyKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: UniqueViolationSqlState,
            ConstraintName: IdempotencyKeyConstraint,
        };

    private static PaymentMethod ToPaymentMethod(string paymentMethod) =>
        paymentMethod switch
        {
            CheckoutPaymentMethods.Cod => PaymentMethod.Cod,
            CheckoutPaymentMethods.Sepay => PaymentMethod.Sepay,
            _ => throw new ArgumentOutOfRangeException(nameof(paymentMethod), paymentMethod, "Phương thức thanh toán không được hỗ trợ."),
        };

    private static JsonDocument ToJsonDocument<T>(T value) =>
        JsonSerializer.SerializeToDocument(value, PickoryJsonDefaults.Options);

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);

    private static AppException TotalChanged() => new("ORDER_TOTAL_CHANGED", 409, TotalChangedMessage);

    private static AppException AddressNotDeliverable(AddressRejectionReason? reason)
    {
        // Gắn lỗi vào đúng ô để form đưa focus về đó. Phường/xã không còn trong danh mục thì khách phải chọn lại phường/xã.
        var (field, message) = reason == AddressRejectionReason.NotInCatalog
            ? (CheckoutPreviewRequests.WardCodeField, CheckoutPreviewService.AddressNotFoundMessage)
            : (CheckoutPreviewRequests.LineField, StreetNotInWardMessage);
        return new AppException(AddressNotDeliverableCode, 422, message, [new ErrorFieldDetail(field, message)]);
    }

    private static AppException InsufficientStock(IReadOnlyList<StockShortage> shortages)
    {
        // Mỗi SKU thiếu một dòng chi tiết. Khóa là `SkuId`, để giỏ hàng đánh dấu đúng dòng.
        var details = shortages
            .Select(shortage => new ErrorFieldDetail(shortage.SkuId, ShortageMessage(shortage)))
            .ToList();
        return new AppException("INSUFFICIENT_STOCK", 409, InsufficientStockMessage, details);
    }

    private static string ShortageMessage(StockShortage shortage) =>
        shortage.Available <= 0
            ? "Sản phẩm này đã hết hàng."
            : $"Sản phẩm này chỉ còn {shortage.Available}, trong khi giỏ hàng có {shortage.Requested}.";
}
