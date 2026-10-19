using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Parcels;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Settings;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Validation;

// Cả Contracts và Entities có kiểu `Shipment`. Dịch vụ trả kiểu của Contracts và lưu entity.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;
using ShipmentEntity = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.Modules.Shipping;

// Cổng công khai của module. Mỗi request dùng một bản riêng vì `ShippingDbContext` có vòng đời theo request.
internal sealed class ShippingService : IShippingService
{
    // Trạng thái gốc GHN trả cho đơn vừa tạo, theo bảng ánh xạ trong spec.
    private const string NewShipmentRawStatus = "ready_to_pick";

    // Lý do gửi kèm mã `GHN-CANCEL-OTHER`. Không chứa dữ liệu khách hàng.
    private const string CancelReasonText = "Cửa hàng hủy vận đơn";

    // Giá trị `payment_type_id` của GHN: 1 là shop trả phí vận chuyển, 2 là người nhận trả.
    private const int ShopPaysShippingFee = 1;

    // Tên người nhận và tên hàng của request xem trước. Không có dữ liệu khách hàng nên không lộ gì ra GHN.
    private const string PreviewRecipientName = "Kiem tra dia chi";

    // Mã đơn của request xem trước. GHN không tạo đơn từ API xem trước, nên mã này không đi vào DB hay vào danh sách đơn của GHN.
    private const string PreviewClientOrderCode = "preview";

    // GHN bắt buộc `content` khi không gửi `items`. Giới hạn 2000 ký tự là của GHN.
    private const int MaxContentLength = 2000;

    private readonly ShippingSettingsStore _settingsStore;
    private readonly ShippingDbContext _database;
    private readonly IGhnClient _ghnClient;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ShipmentStatusUpdater _statusUpdater;
    private readonly QuoteCacheStore _quoteCache;
    private readonly FallbackFeeStore _fallbackFeeStore;
    private readonly ILogger<ShippingService> _logger;

    public ShippingService(
        ShippingSettingsStore settingsStore,
        ShippingDbContext database,
        IGhnClient ghnClient,
        IEventDispatcher eventDispatcher,
        TimeProvider timeProvider,
        ShipmentStatusUpdater statusUpdater,
        QuoteCacheStore quoteCache,
        FallbackFeeStore fallbackFeeStore,
        ILogger<ShippingService> logger)
    {
        _settingsStore = settingsStore;
        _database = database;
        _ghnClient = ghnClient;
        _eventDispatcher = eventDispatcher;
        _timeProvider = timeProvider;
        _statusUpdater = statusUpdater;
        _quoteCache = quoteCache;
        _fallbackFeeStore = fallbackFeeStore;
        _logger = logger;
    }

    public async Task<Quote> QuoteAsync(
        ShippingAddress to, Parcel parcel, int codAmount, int insuredValue, CancellationToken cancellationToken)
    {
        // Tham số null hoặc số âm là lỗi lập trình của bên gọi. Báo ngay, không trả lỗi nghiệp vụ.
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(parcel);
        ArgumentOutOfRangeException.ThrowIfNegative(codAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(insuredValue);

        // GHN không nhận tiền thu hộ trên mức giới hạn. Từ chối trước để không tốn một lời gọi chắc chắn bị từ chối.
        if (codAmount > QuoteRules.MaxCodAmount)
        {
            throw new ShippingRejectedException("Số tiền thu hộ vượt giới hạn 50.000.000 đ của đơn vị vận chuyển.");
        }

        // Chưa có địa chỉ lấy hàng thì GHN không tính được phí. Ghi mức lỗi vì admin phải cấu hình.
        var settings = await _settingsStore.GetAsync(cancellationToken);
        if (settings is null)
        {
            _logger.LogError("Báo phí bị chặn vì admin chưa cấu hình địa chỉ lấy hàng của shipping.");
            throw new ShippingNotConfiguredException();
        }

        // Kiểm lại với danh mục hiện tại trước khi gọi GHN, vì phường có thể bị tắt sau khi khách chọn.
        await RequireDeliverableWardAsync(to, cancellationToken);

        // GHN chỉ nhận mã số ở `to_province_id_v2` và `to_ward_id_v2`. Mã không đổi được sang số thì không gọi GHN.
        if (!GhnAddressIds.TryParse(to.ProvinceCode, to.WardCode, out var provinceId, out var wardId))
        {
            _logger.LogError("Báo phí bị chặn vì mã địa chỉ của danh mục không phải số: tỉnh {ProvinceCode}, phường {WardCode}.", to.ProvinceCode, to.WardCode);
            throw new ShippingRejectedException("Địa chỉ giao hàng có mã không hợp lệ.");
        }

        // Admin chưa chọn gói thì dùng gói hàng nhẹ. Báo phí và ngày giao phải dùng cùng một gói.
        var serviceTypeId = QuoteRules.ResolveServiceTypeId(settings.DefaultServiceTypeId);
        var feeRequest = new GhnFeeRequest(
            parcel.WeightGrams,
            parcel.LengthCm,
            parcel.WidthCm,
            parcel.HeightCm,
            serviceTypeId,
            // Giá trị hàng vượt trần thì khai đúng trần. GHN từ chối giá trị cao hơn.
            QuoteRules.ClampInsuredValue(insuredValue),
            codAmount,
            provinceId,
            wardId);

        var leadTimeRequest = new GhnLeadTimeRequest(
            provinceId, wardId, serviceTypeId, parcel.WeightGrams, parcel.LengthCm, parcel.WidthCm, parcel.HeightCm);

        // Hủy riêng được ngày giao khi báo phí lỗi. Token của bên gọi vẫn hủy được cả hai lời gọi.
        using var leadTimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Chạy song song với báo phí để tổng thời gian không cộng thêm thời gian của ngày giao.
        var expectedDeliveryDateTask = FindExpectedDeliveryDateAsync(leadTimeRequest, leadTimeCancellation.Token);

        // Khóa của phí đã lưu dùng đúng các giá trị gửi GHN, nên cùng khóa thì GHN trả cùng phí.
        var cacheKey = new QuoteCacheKey(
            to.WardCode, settings.WardCode, serviceTypeId, FallbackFeeCalculator.ChargeableWeightGrams(parcel), feeRequest.InsuranceValue, codAmount);

        GhnFee fee;
        try
        {
            // Lỗi mạng và 5xx đã được adapter thử lại đúng 1 lần. Service không thêm vòng thử lại nào.
            fee = await _ghnClient.CalculateFeeAsync(feeRequest, cancellationToken);
        }
        catch (ShippingUnavailableException)
        {
            // Dừng ngày giao trước khi tìm phí thay thế, để lời gọi treo không giữ kết nối trong lúc đó.
            await StopLeadTimeLookupAsync(leadTimeCancellation, expectedDeliveryDateTask);

            // GHN không phản hồi. Thử phí đã lưu rồi phí dự phòng trước khi báo khách là không có phí.
            var substitute = await FindSubstituteQuoteAsync(cacheKey, serviceTypeId, settings, to, parcel, cancellationToken);
            if (substitute is not null)
            {
                return substitute;
            }

            // Không có phí thay thế nào dùng được thì giữ nguyên lỗi `SHIPPING_UNAVAILABLE` như trước SHIP-14.
            throw;
        }
        catch
        {
            // Dừng ngày giao rồi ném lại lỗi gốc, để lời gọi treo không chạy tiếp sau khi báo phí đã thất bại.
            await StopLeadTimeLookupAsync(leadTimeCancellation, expectedDeliveryDateTask);

            // GHN từ chối hoặc lỗi khác thì không dùng phí thay thế. GHN đã trả lời, và phí cũ có thể sai với yêu cầu này.
            throw;
        }

        // Lưu phí vừa nhận để dùng khi GHN không phản hồi lần sau. Bước này chạy khi lời gọi ngày giao còn đang chờ.
        await TrySaveQuoteAsync(cacheKey, fee.Total, cancellationToken);

        var expectedDeliveryDate = await expectedDeliveryDateTask;
        return new Quote(fee.Total, serviceTypeId, expectedDeliveryDate, QuoteSource.Ghn);
    }

    // Dừng lời gọi ngày giao khi báo phí đã lỗi, vì ngày giao không còn ý nghĩa.
    private static async Task StopLeadTimeLookupAsync(CancellationTokenSource leadTimeCancellation, Task leadTimeLookup)
    {
        // Hủy lời gọi còn treo để không giữ kết nối tới hết timeout.
        await leadTimeCancellation.CancelAsync();

        // Chờ lời gọi dừng hẳn. Lời gọi ngày giao không ném lỗi, nên không che lỗi của báo phí.
        await leadTimeLookup;
    }

    // Lưu phí GHN đã tính. Lỗi lưu chỉ ghi cảnh báo, vì khách đã có phí đúng từ GHN và không được mất báo phí vì cache.
    private async Task TrySaveQuoteAsync(QuoteCacheKey key, int fee, CancellationToken cancellationToken)
    {
        try
        {
            await _quoteCache.SaveAsync(key, fee, cancellationToken);
        }
        // Khách hủy request thì để lỗi hủy đi tiếp. Mọi lỗi khác của DB chỉ làm mất một lần lưu.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Không lưu được phí GHN vừa tính. Báo phí vẫn trả phí của GHN.");
        }
    }

    // Tìm phí thay thế khi GHN không phản hồi: phí đã lưu còn hạn trước, rồi bảng phí dự phòng. Trả `null` khi không có cái nào dùng được.
    private async Task<Quote?> FindSubstituteQuoteAsync(
        QuoteCacheKey cacheKey,
        int serviceTypeId,
        ShippingSettings settings,
        ShippingAddress to,
        Parcel parcel,
        CancellationToken cancellationToken)
    {
        try
        {
            // Phí đã lưu đi trước vì đó là số GHN từng tính cho đúng điều kiện này, sát thực tế hơn bảng của admin.
            var cachedFee = await _quoteCache.FindFreshFeeAsync(cacheKey, cancellationToken);
            if (cachedFee is not null)
            {
                var cachedRoute = FallbackFeeRouteRules.Determine(settings.ProvinceCode, to.ProvinceCode);
                LogSubstituteQuote(QuoteSource.Cached, cachedRoute, cacheKey.ChargeableWeightGrams);
                // Không có ngày giao dự kiến, vì GHN không trả lời lần này.
                return new Quote(cachedFee.Value, serviceTypeId, null, QuoteSource.Cached);
            }

            var fallbackConfig = await _fallbackFeeStore.GetAsync(cancellationToken);
            var fallbackQuote = FallbackFeeQuoter.TryQuote(fallbackConfig, settings.ProvinceCode, to.ProvinceCode, parcel);
            if (fallbackQuote is null)
            {
                return null;
            }

            LogSubstituteQuote(QuoteSource.Fallback, fallbackQuote.Route, fallbackQuote.ChargeableWeightGrams);
            return new Quote(fallbackQuote.Fee, serviceTypeId, null, QuoteSource.Fallback);
        }
        // DB lỗi lúc tìm phí thay thế không được che lỗi gốc. Trả `null` để bên gọi ném lại `SHIPPING_UNAVAILABLE`.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Không đọc được phí đã lưu hoặc phí dự phòng khi GHN không phản hồi.");
            return null;
        }
    }

    // Log chỉ ghi nguồn phí, tuyến và khối lượng tính cước. Không ghi địa chỉ, tên, số điện thoại hay mã phường.
    private void LogSubstituteQuote(QuoteSource source, FallbackFeeRoute route, long chargeableWeightGrams)
    {
        _logger.LogWarning(
            "GHN không phản hồi khi báo phí, dùng phí {Source}: tuyến {Route}, khối lượng tính cước {ChargeableWeightGrams} g.",
            source, route, chargeableWeightGrams);
    }

    public async Task<Shipment> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);

        // GHN không nhận tiền thu hộ trên mức giới hạn. Từ chối trước để không tốn một lời gọi chắc chắn bị từ chối.
        if (request.CodAmount > QuoteRules.MaxCodAmount)
        {
            throw new ShippingRejectedException("Số tiền thu hộ vượt giới hạn 50.000.000 đ của đơn vị vận chuyển.");
        }

        // Số điện thoại sai thì shipper không gọi được cho khách. Chuẩn hóa về `0xxxxxxxxx` là dạng GHN nhận.
        var normalizedPhone = ContactFieldRules.NormalizePhone(request.To.Phone);
        if (normalizedPhone is null)
        {
            throw new ShippingRejectedException(ContactFieldRules.PhoneInvalidMessage);
        }

        // `Ref` đã có vận đơn còn hiệu lực thì trả vận đơn đó. Bấm tạo lại không được tạo đơn thứ hai hay phát sự kiện lần hai.
        var existing = await FindShipmentByRefAsync(request.Ref, cancellationToken);
        if (existing is not null)
        {
            return ToContract(existing);
        }

        // Chưa có địa chỉ lấy hàng thì GHN không tạo được đơn. Ghi mức lỗi vì admin phải cấu hình.
        var settings = await _settingsStore.GetAsync(cancellationToken);
        if (settings is null)
        {
            _logger.LogError("Tạo vận đơn bị chặn vì admin chưa cấu hình địa chỉ lấy hàng của shipping.");
            throw new ShippingNotConfiguredException();
        }

        // Kiểm lại với danh mục hiện tại và lấy tên chuẩn của danh mục để gửi GHN.
        var ward = await RequireDeliverableWardAsync(request.To.Address, cancellationToken);

        // Đếm số vận đơn đã hủy của `Ref` để chọn hậu tố. Mỗi lần tạo sau khi hủy có mã riêng, nên đơn mới không lẫn với đơn đã hủy.
        var cancelledCount = await CountCancelledShipmentsAsync(request.Ref, cancellationToken);
        var clientOrderCode = GhnOrderCode.For(request.Ref, cancelledCount);

        var createOrderRequest = BuildCreateOrderRequest(request, normalizedPhone, ward, settings, clientOrderCode);

        // Chỉ gọi GHN một lần, không thử lại. GHN có thể đã nhận lần đầu, và gọi lại cùng `client_order_code` trả đơn cũ khi đơn chưa hủy.
        var created = await _ghnClient.CreateOrderAsync(createOrderRequest, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var shipment = new ShipmentEntity
        {
            Ref = request.Ref,
            TrackingCode = created.OrderCode,
            Fee = created.TotalFee,
            CodAmount = request.CodAmount,
            Parcel = request.Parcel,
            Status = ShipmentStatus.PendingPickup,
            RawStatus = NewShipmentRawStatus,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime,
        };
        shipment.Events.Add(new ShipmentEvent
        {
            Status = shipment.Status,
            RawStatus = shipment.RawStatus,
            OccurredAt = now.UtcDateTime,
            // Nhân viên bấm tạo, nên nguồn là `Manual` thay vì webhook hoặc job đồng bộ.
            Source = ShipmentEventSource.Manual,
        });

        // Vận đơn và dòng lịch sử cùng một transaction. GHN đã tạo đơn nên các bước lưu dùng `CancellationToken.None`.
        var events = new DomainEventCollector(_eventDispatcher);
        try
        {
            await using var transaction = await _database.Database.BeginTransactionAsync(CancellationToken.None);
            _database.Shipments.Add(shipment);
            await _database.SaveChangesAsync(CancellationToken.None);

            // `PreviousStatus` là null vì đây là lần ghi trạng thái đầu tiên của vận đơn.
            events.Emit(new ShipmentStatusChangedEvent(
                shipment.Id, shipment.Ref, shipment.Status, PreviousStatus: null, now, ShipmentStatusChangeSource.Shop));
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (DbUpdateException exception) when (IsShipmentUniqueViolation(exception))
        {
            // Một request khác cùng `Ref` đã chèn trước, và GHN trả cùng `order_code` cho cùng `client_order_code`.
            // Bỏ bản chưa lưu rồi trả vận đơn đã có, không phát sự kiện lần hai.
            _database.ChangeTracker.Clear();
            var winner = await FindShipmentByRefAsync(request.Ref, CancellationToken.None);
            if (winner is null)
            {
                // Vi phạm unique nhưng không có vận đơn cùng `Ref`: mã vận đơn GHN trùng với vận đơn của `Ref` khác. Không giấu lỗi này.
                throw;
            }

            return ToContract(winner);
        }

        // Phát sau commit. Bên nhận đọc DB bằng kết nối khác vẫn thấy vận đơn mới.
        await events.FlushAsync(CancellationToken.None);
        return ToContract(shipment);
    }

    public async Task<AddressCheckResult> CheckDeliveryAddressAsync(
        ShippingAddress to, Parcel parcel, CancellationToken cancellationToken)
    {
        // Tham số null là lỗi lập trình của bên gọi. Báo ngay, không trả kết quả kiểm.
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(parcel);

        // Chưa có địa chỉ lấy hàng thì request xem trước thiếu thông tin. Không kiểm được, và không chặn khách đặt đơn.
        var settings = await _settingsStore.GetAsync(cancellationToken);
        if (settings is null)
        {
            _logger.LogWarning("Kiểm địa chỉ nhận bị bỏ qua vì admin chưa cấu hình địa chỉ lấy hàng của shipping.");
            return AddressCheckResult.Unverifiable;
        }

        // Dùng chung luật danh mục với báo phí và tạo vận đơn. Phường đã tắt thì khỏi tốn một lời gọi GHN.
        var ward = await FindDeliverableWardAsync(to, cancellationToken);
        if (ward is null)
        {
            return AddressCheckResult.Rejected(AddressRejectionReason.NotInCatalog);
        }

        // Dựng request bằng đúng hàm của tạo vận đơn. Địa chỉ nhận và địa chỉ lấy hàng gửi GHN giống hệt lúc tạo đơn thật.
        var previewRequest = BuildCreateOrderRequest(
            BuildPreviewShipmentRequest(to, parcel, settings),
            settings.SenderPhone,
            ward,
            settings,
            PreviewClientOrderCode);

        try
        {
            // Chỉ gọi API xem trước. Hàm này không gọi `CreateOrderAsync` và không ghi DB, nên kiểm xong không để lại gì.
            await _ghnClient.PreviewOrderAsync(previewRequest, cancellationToken);
            return AddressCheckResult.Valid;
        }
        catch (ShippingRejectedException rejection) when (rejection.Kind == ShippingRejectionKind.ToAddressMismatch)
        {
            // Chỉ lỗi `To address conflict` được coi là lỗi địa chỉ của khách. GHN chưa từng gắn nhãn này cho địa chỉ lấy hàng (ADR 0003).
            return AddressCheckResult.Rejected(AddressRejectionReason.StreetNotInWard);
        }
        catch (ShippingRejectedException rejection)
        {
            // Lỗi còn lại thuộc cấu hình lấy hàng hoặc yêu cầu của shop, khách không sửa được. Cho đặt đơn và ghi log để admin biết.
            _logger.LogWarning("Kiểm địa chỉ nhận không kết luận được. GHN từ chối vì lý do {Kind}.", rejection.Kind);
            return AddressCheckResult.Unverifiable;
        }
        catch (ShippingUnavailableException)
        {
            // GHN lỗi mạng, hết thời gian chờ hoặc 5xx. Kiểm địa chỉ là bước phụ nên không được chặn khách đặt đơn.
            _logger.LogWarning("Kiểm địa chỉ nhận không kết luận được vì GHN không phản hồi.");
            return AddressCheckResult.Unverifiable;
        }
    }

    // Vi phạm ràng buộc unique của `shipments` (`ref` hoặc `trackingCode`). Hai request cùng `Ref` có thể chạm một trong hai.
    private static bool IsShipmentUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "shipments_ref_key" or "shipments_trackingCode_key",
        };

    public async Task CancelShipmentAsync(string shipmentId, CancellationToken cancellationToken)
    {
        // Mã vận đơn rỗng là lỗi lập trình của bên gọi, không phải vận đơn không tồn tại.
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentId);

        // Đọc không theo dõi vì lớp áp trạng thái ghi bằng câu lệnh có điều kiện, không sửa entity này.
        var shipment = await _database.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            throw new ShipmentNotFoundException();
        }

        // Vận đơn đã hủy thì không có gì để làm. Hủy lần hai không được gọi GHN hay phát sự kiện.
        if (shipment.Status == ShipmentStatus.Cancelled)
        {
            return;
        }

        // Hàng đã giao hoặc đã hoàn thì không hủy được nữa. Từ chối trước để không gọi GHN vô ích.
        if (shipment.Status.IsTerminal())
        {
            throw new ShippingRejectedException("Vận đơn đã kết thúc nên không thể hủy.");
        }

        // Không thử lại, và GHN từ chối thì `ShippingRejectedException` đi thẳng lên bên gọi, DB không đổi.
        // Dùng mã `GHN-CANCEL-OTHER` vì hàm hủy không nhận lý do cụ thể.
        await _ghnClient.CancelOrderAsync(
            shipment.TrackingCode, GhnCancelReason.Other, CancelReasonText, cancellationToken);

        try
        {
            // Ghi bằng lớp dùng chung với webhook và job. Nếu webhook đã ghi trước thì kết quả là `LostRace`, nên không đổi `ref` hay phát sự kiện lần hai.
            await _statusUpdater.ApplyAsync(shipment, ShipmentStatusMapper.CancelledRawStatus, ShipmentEventSource.Manual);
        }
        catch (Exception exception)
        {
            // GHN đã hủy nhưng DB chưa ghi nhận. Ghi mức lỗi kèm mã để SHIP-08 đồng bộ lại trạng thái từ GHN.
            _logger.LogError(
                exception,
                "GHN đã hủy vận đơn nhưng lưu DB lỗi. Mã vận đơn nội bộ {ShipmentId}, mã vận đơn GHN {TrackingCode}.",
                shipment.Id,
                shipment.TrackingCode);
            throw;
        }
    }

    public async Task<Shipment?> GetShipmentAsync(string shipmentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentId);

        // Chỉ đọc nên không cần theo dõi entity.
        var shipment = await _database.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == shipmentId, cancellationToken);
        return shipment is null ? null : ToContract(shipment);
    }

    public async Task<Shipment?> GetShipmentByRefAsync(string reference, CancellationToken cancellationToken)
    {
        // Mã đơn rỗng là lỗi lập trình của bên gọi, giống `GetShipmentAsync` với mã vận đơn rỗng.
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // Tra theo `ref` giống luồng tạo vận đơn. Vận đơn đã hủy có `ref` đã đổi nên không khớp.
        var shipment = await FindShipmentByRefAsync(reference, cancellationToken);
        return shipment is null ? null : ToContract(shipment);
    }

    public Parcel BuildParcel(IReadOnlyList<ParcelItem> items) => ParcelBuilder.Build(items);

    // Lỗi lập trình của bên gọi. Thiếu dữ liệu bắt buộc thì không có gì để gửi GHN.
    private static void ValidateCreateRequest(CreateShipmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Ref, nameof(request.Ref));
        ArgumentNullException.ThrowIfNull(request.To);
        ArgumentNullException.ThrowIfNull(request.To.Address);
        ArgumentNullException.ThrowIfNull(request.Parcel);
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentOutOfRangeException.ThrowIfNegative(request.CodAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(request.InsuredValue);

        // Vận đơn không có hàng thì phiếu GHN không có mô tả hàng hóa.
        if (request.Items.Count == 0)
        {
            throw new ArgumentException("Vận đơn phải có ít nhất một dòng hàng.", nameof(request));
        }

        foreach (var line in request.Items)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.Name) || line.Quantity < 1)
            {
                throw new ArgumentException("Mỗi dòng hàng phải có tên và số lượng từ 1 trở lên.", nameof(request));
            }
        }
    }

    // Để `internal` cho contract test sandbox gọi đúng hàm này, nên test không lệch với request thật của trang đơn.
    internal static GhnCreateOrderRequest BuildCreateOrderRequest(
        CreateShipmentRequest request,
        string normalizedPhone,
        AddressWard ward,
        ShippingSettings settings,
        string clientOrderCode)
    {
        var parcel = request.Parcel;
        var serviceTypeId = QuoteRules.ResolveServiceTypeId(settings.DefaultServiceTypeId);
        var content = BuildContent(request.Items);
        return new GhnCreateOrderRequest(
            request.To.Name,
            normalizedPhone,
            request.To.Address.Line,
            // GHN so chuỗi `name` của danh mục mới. Tên trong tham số có thể cũ hoặc sai nên không dùng.
            ward.Province!.Name,
            ward.Name,
            clientOrderCode,
            parcel.WeightGrams,
            parcel.LengthCm,
            parcel.WidthCm,
            parcel.HeightCm,
            serviceTypeId,
            // Khách đã trả phí ship cho shop ở bước thanh toán, nên shop trả GHN.
            ShopPaysShippingFee,
            request.CodAmount,
            // Giá trị hàng vượt trần thì khai đúng trần. GHN từ chối giá trị cao hơn.
            QuoteRules.ClampInsuredValue(request.InsuredValue),
            content)
        {
            Note = request.Note,
            // Gói hàng nặng bắt buộc `items`, nếu thiếu thì GHN từ chối mọi đơn. Module chỉ có một kiện mỗi vận đơn, nên gửi một dòng với số đo của `Parcel`.
            Items = serviceTypeId == QuoteRules.HeavyGoodsServiceTypeId
                ? [new GhnOrderItem(content, 1, parcel.WeightGrams, parcel.LengthCm, parcel.WidthCm, parcel.HeightCm)]
                : null,
            // Gửi địa chỉ lấy hàng admin đã cấu hình để cấu hình đó có hiệu lực. Thiếu cờ `IsNewFromAddress` thì GHN bỏ qua tên tỉnh và tên phường rồi giữ phường của shop (ADR 0003, SHIP-11).
            FromName = settings.SenderName,
            FromPhone = settings.SenderPhone,
            FromAddress = settings.Line,
            FromProvinceName = settings.ProvinceName,
            FromWardName = settings.WardName,
            IsNewFromAddress = true,
        };
    }

    private static string BuildContent(IReadOnlyList<ShipmentLine> items)
    {
        var content = string.Join(", ", items.Select(line => $"{line.Name.Trim()} x{line.Quantity}"));
        return content.Length <= MaxContentLength ? content : content[..MaxContentLength];
    }

    // Dùng chung cho báo phí và tạo vận đơn, để hai luồng không lệch nhau về luật địa chỉ nhận.
    // Trả phường kèm tỉnh để nơi gọi lấy tên chuẩn của danh mục.
    private async Task<AddressWard> RequireDeliverableWardAsync(ShippingAddress address, CancellationToken cancellationToken)
    {
        var ward = await FindDeliverableWardAsync(address, cancellationToken);
        if (ward is null)
        {
            throw new ShippingRejectedException("Địa chỉ giao hàng không còn trong danh mục.");
        }

        return ward;
    }

    // Luật danh mục của địa chỉ nhận, tách riêng để hàm kiểm địa chỉ trả kết quả thay vì ném lỗi. Trả `null` khi phường không còn dùng được.
    private Task<AddressWard?> FindDeliverableWardAsync(ShippingAddress address, CancellationToken cancellationToken)
    {
        // Phường có thể bị tắt hoặc đổi tỉnh sau khi khách chọn, nên kiểm lại với danh mục hiện tại.
        return _database.AddressWards
            .AsNoTracking()
            .Include(candidate => candidate.Province)
            .FirstOrDefaultAsync(
                candidate => candidate.Code == address.WardCode
                    && candidate.ProvinceCode == address.ProvinceCode
                    && candidate.IsActive,
                cancellationToken);
    }

    // Người nhận giả cho request xem trước. GHN cần tên và số điện thoại để nhận request, và khách chưa nhập thông tin này lúc kiểm địa chỉ.
    private static CreateShipmentRequest BuildPreviewShipmentRequest(
        ShippingAddress to, Parcel parcel, ShippingSettings settings)
    {
        var placeholderRecipient = new ShippingRecipient(PreviewRecipientName, settings.SenderPhone, to);
        return new CreateShipmentRequest(
            PreviewClientOrderCode,
            placeholderRecipient,
            parcel,
            [new ShipmentLine(PreviewRecipientName, 1)],
            CodAmount: 0,
            InsuredValue: 0,
            Note: null);
    }

    // Vận đơn đã hủy có `ref` đã đổi sang `{ref}#cancelled#{n}`, nên tra theo `ref` gốc chỉ thấy vận đơn còn hiệu lực.
    private Task<ShipmentEntity?> FindShipmentByRefAsync(string reference, CancellationToken cancellationToken)
    {
        return _database.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(shipment => shipment.Ref == reference, cancellationToken);
    }

    // Đếm vận đơn có `ref` bắt đầu bằng `{ref}#cancelled#` cho ra số lần hủy của `ref` gốc.
    private Task<int> CountCancelledShipmentsAsync(string reference, CancellationToken cancellationToken) =>
        CancelledShipmentRef.CountAsync(_database, reference, cancellationToken);

    private static Shipment ToContract(ShipmentEntity shipment) =>
        new(shipment.Id, shipment.Ref, shipment.TrackingCode, shipment.Fee, shipment.Status);

    // Hàm này không ném lỗi: ngày giao là thông tin phụ, nên mọi lỗi của nó thành `null` và không làm hỏng báo phí.
    private async Task<DateOnly?> FindExpectedDeliveryDateAsync(
        GhnLeadTimeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var leadTime = await _ghnClient.GetLeadTimeAsync(request, cancellationToken);

            // GHN không trả thời điểm giao hợp lệ thì không có ngày để báo. Báo phí vẫn đúng.
            if (leadTime.Leadtime is not > 0)
            {
                _logger.LogWarning("GHN không trả thời điểm giao dự kiến hợp lệ. Báo phí không kèm ngày giao.");
                return null;
            }

            return GhnLeadTimeDate.ToVietnamDate(leadTime.Leadtime.Value);
        }
        catch (OperationCanceledException)
        {
            // Bên gọi hủy hoặc báo phí đã lỗi. Kết quả này không còn được dùng, nên không cần ghi log.
            return null;
        }
        catch (AppException failure)
        {
            // Lỗi mạng, hết giờ hoặc GHN từ chối đã được adapter quy về lỗi nghiệp vụ. Giữ phí, bỏ ngày giao.
            _logger.LogWarning("Lấy ngày giao dự kiến thất bại với mã {ErrorCode}. Báo phí không kèm ngày giao.", failure.Code);
            return null;
        }
        catch (Exception unexpected)
        {
            // Lỗi ngoài dự kiến là lỗi của code. Ghi mức lỗi để có người sửa, nhưng không để nó làm mất phí đã tính được.
            _logger.LogError(unexpected, "Lấy ngày giao dự kiến gặp lỗi ngoài dự kiến. Báo phí không kèm ngày giao.");
            return null;
        }
    }
}
