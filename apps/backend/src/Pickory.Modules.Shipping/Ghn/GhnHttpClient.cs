using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Shipping.Ghn;

// Adapter gọi API GHN bằng `HttpClient`. Mọi lỗi quy về `ShippingUnavailableException` hoặc `ShippingRejectedException`.
internal sealed class GhnHttpClient : IGhnClient
{
    // Mỗi lần gọi GHN tối đa 5 giây theo spec, trừ ngày giao có timeout riêng bên dưới. Test truyền giá trị ngắn hơn để không chờ thật.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    // Ngày giao chỉ là thông tin phụ và chạy song song với báo phí. Chờ tối đa 2 giây để phần phụ không làm báo phí chậm quá ngưỡng p95 dưới 2 giây của spec.
    public static readonly TimeSpan DefaultLeadTimeTimeout = TimeSpan.FromSeconds(2);

    // Giới hạn độ dài thông báo của GHN khi nối vào thông báo cho người dùng và log.
    private const int MaxCarrierMessageLength = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly string _shopId;
    private readonly ILogger<GhnHttpClient> _logger;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _leadTimeTimeout;

    public GhnHttpClient(
        HttpClient httpClient,
        string token,
        string shopId,
        ILogger<GhnHttpClient> logger,
        TimeSpan? timeout = null,
        TimeSpan? leadTimeTimeout = null)
    {
        _httpClient = httpClient;
        _token = token;
        _shopId = shopId;
        _logger = logger;
        _timeout = timeout ?? DefaultTimeout;
        _leadTimeTimeout = leadTimeTimeout ?? DefaultLeadTimeTimeout;
    }

    public async Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken)
    {
        // Không truyền `limit`: GHN trả toàn bộ khi bỏ tham số, và hiện chỉ có 34 tỉnh.
        var provinces = await SendAsync<List<GhnProvince>>(
            "GetProvinces",
            () => CreateRequest(HttpMethod.Get, "/shiip/public-api/v3/master-data/province/all", body: null),
            retryOnTransientFailure: false,
            cancellationToken);
        return RequireData("GetProvinces", provinces);
    }

    public async Task<IReadOnlyList<GhnWard>> GetWardsAsync(
        string provinceId, int offset, int limit, CancellationToken cancellationToken)
    {
        // Mã tỉnh đi vào query string. Mã hóa để giá trị lạ không chèn thêm tham số vào request.
        var query = $"province_id={Uri.EscapeDataString(provinceId)}&offset={offset}&limit={limit}";
        var wards = await SendAsync<List<GhnWard>>(
            "GetWards",
            () => CreateRequest(HttpMethod.Get, $"/shiip/public-api/v3/master-data/ward/all-by-province-id?{query}", body: null),
            retryOnTransientFailure: false,
            cancellationToken);

        // Tỉnh không tồn tại thì GHN trả HTTP 200 với `data: null`. Đó là kết quả rỗng, không phải lỗi.
        return wards ?? [];
    }

    public async Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken)
    {
        // Báo phí chỉ đọc dữ liệu nên là hàm duy nhất được thử lại. Thử đúng 1 lần khi lỗi mạng, hết giờ hoặc 5xx.
        var fee = await SendAsync<GhnFee>(
            "CalculateFee",
            () => CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/fee", request),
            retryOnTransientFailure: true,
            cancellationToken);
        return RequireData("CalculateFee", fee);
    }

    public async Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken)
    {
        // Không thử lại: ngày giao chỉ là thông tin phụ, và lần thử thứ hai sẽ kéo dài báo phí. Timeout riêng ngắn hơn báo phí.
        var leadTime = await SendAsync<GhnLeadTime>(
            "GetLeadTime",
            () => CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/leadtime", request),
            retryOnTransientFailure: false,
            cancellationToken,
            _leadTimeTimeout);
        return RequireData("GetLeadTime", leadTime);
    }

    public async Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken)
    {
        // Không thử lại, kể cả khi lỗi mạng hoặc 5xx, vì GHN có thể đã nhận lần đầu. Người dùng bấm tạo lại thì GHN trả đơn cũ theo cùng `client_order_code`.
        var created = await SendAsync<GhnCreatedOrder>(
            "CreateOrder",
            () => CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/create", request),
            retryOnTransientFailure: false,
            cancellationToken);
        var order = RequireData("CreateOrder", created);

        // Thiếu mã vận đơn thì không có gì để lưu. Báo lỗi để bên gọi không ghi nhận một vận đơn rỗng.
        if (string.IsNullOrWhiteSpace(order.OrderCode))
        {
            _logger.LogError("GHN CreateOrder trả phản hồi thiếu mã vận đơn.");
            throw new ShippingUnavailableException();
        }

        return order;
    }

    public async Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken)
    {
        // GHN nhận lô mã đơn và hủy cả lô hoặc không hủy gì. Adapter chỉ gửi một mã mỗi lần.
        var body = new { OrderCodes = new[] { orderCode }, ReasonCode = reasonCode, Reason = reason };
        var results = await SendAsync<List<GhnCancelResult>>(
            "CancelOrder",
            () => CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/switch-status/cancel", body),
            retryOnTransientFailure: false,
            cancellationToken);

        var result = results?.FirstOrDefault(
            candidate => string.Equals(candidate.OrderCode, orderCode, StringComparison.OrdinalIgnoreCase));

        // Không có kết quả cho đúng mã đơn thì không biết đơn đã hủy hay chưa. Không được coi là đã hủy.
        if (result is null)
        {
            _logger.LogError("GHN CancelOrder trả phản hồi thiếu kết quả cho mã vận đơn đã gửi.");
            throw new ShippingUnavailableException();
        }

        // Đơn ở trạng thái không hủy được thì GHN trả HTTP 200 với `result: false`. Đó là từ chối nghiệp vụ.
        if (!result.Result)
        {
            throw CreateRejection("CancelOrder", result.Message, 200);
        }
    }

    public async Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken)
    {
        var detail = await SendAsync<GhnOrderDetail>(
            "GetOrderDetail",
            () => CreateRequest(
                HttpMethod.Get, $"/shiip/public-api/v2/shipping-order/detail?order_code={Uri.EscapeDataString(orderCode)}", body: null),
            retryOnTransientFailure: false,
            cancellationToken);
        return RequireData("GetOrderDetail", detail);
    }

    // Dựng request mới cho mỗi lần gọi, vì `HttpRequestMessage` không gửi lại được lần hai.
    private HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery, object? body)
    {
        var request = new HttpRequestMessage(method, pathAndQuery);

        // Token và ShopId nằm trong header, không bao giờ nằm trong URL. URL còn xuất hiện trong log của HttpClient.
        request.Headers.TryAddWithoutValidation("Token", _token);
        request.Headers.TryAddWithoutValidation("ShopId", _shopId);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        return request;
    }

    // Gửi request, đổi lỗi sang lỗi nghiệp vụ. Chỉ khi `retryOnTransientFailure` bật thì thử lại đúng 1 lần.
    private async Task<T?> SendAsync<T>(
        string operation,
        Func<HttpRequestMessage> createRequest,
        bool retryOnTransientFailure,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        // Mỗi hàm dùng timeout mặc định của adapter, trừ hàm truyền timeout riêng.
        var effectiveTimeout = timeout ?? _timeout;
        var attempt = 1;
        while (true)
        {
            try
            {
                return await SendOnceAsync<T>(operation, createRequest(), effectiveTimeout, cancellationToken);
            }
            catch (GhnAttemptException failure) when (retryOnTransientFailure && attempt == 1 && failure.IsTransient)
            {
                // Lỗi mạng, hết giờ hoặc 5xx có thể qua khỏi ở lần sau. Chỉ báo phí được thử lại vì đó là thao tác chỉ đọc.
                _logger.LogWarning("GHN {Operation} lỗi tạm thời, thử lại 1 lần.", operation);
                attempt++;
            }
            catch (GhnAttemptException failure)
            {
                throw failure.Mapped;
            }
        }
    }

    private async Task<T?> SendOnceAsync<T>(
        string operation, HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Timeout gồm cả lúc đọc thân phản hồi. Token của bên gọi vẫn hủy được ngay.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        HttpStatusCode statusCode;
        string body;
        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutSource.Token);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Bên gọi tự hủy, không phải lỗi của GHN. Ném lại nguyên trạng để bên gọi biết.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Token của bên gọi chưa hủy mà lời gọi bị hủy, nghĩa là đã hết timeout của hàm này.
            _logger.LogWarning("GHN {Operation} quá hạn {TimeoutSeconds} giây.", operation, timeout.TotalSeconds);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: true);
        }
        catch (HttpRequestException exception)
        {
            // Chỉ ghi loại lỗi. Thông điệp lỗi mạng có thể chứa địa chỉ máy chủ, không cần cho việc chẩn đoán.
            _logger.LogWarning("GHN {Operation} lỗi mạng: {Error}.", operation, exception.HttpRequestError);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: true);
        }

        return InterpretResponse<T>(operation, statusCode, body);
    }

    private T? InterpretResponse<T>(string operation, HttpStatusCode statusCode, string body)
    {
        var status = (int)statusCode;

        // 400: GHN từ chối yêu cầu, ví dụ địa chỉ không phục vụ. Gửi lại cùng yêu cầu vẫn bị từ chối, nên không thử lại.
        if (statusCode == HttpStatusCode.BadRequest)
        {
            throw Rejected(operation, ReadCarrierMessage(body), status);
        }

        // 401 và 403: token sai hoặc IP không được phép. Đây là lỗi cấu hình của shop nên ghi mức lỗi.
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError("GHN {Operation} bị từ chối quyền với HTTP {StatusCode}. Kiểm tra token và IP được phép.", operation, status);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: false);
        }

        // 408 và 429: GHN báo quá tải hoặc giới hạn tần suất. Không thử lại ngay để không tăng tải.
        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("GHN {Operation} trả HTTP {StatusCode}.", operation, status);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: false);
        }

        // 5xx: GHN lỗi. Lỗi này có thể tự qua khỏi nên được tính là tạm thời.
        if (status >= 500)
        {
            _logger.LogWarning("GHN {Operation} trả HTTP {StatusCode}.", operation, status);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: true);
        }

        // Mã còn lại ngoài 2xx (ví dụ 404, 3xx) cho thấy sai địa chỉ gốc hoặc đường dẫn, nên ghi mức lỗi.
        if (status is < 200 or >= 300)
        {
            _logger.LogError("GHN {Operation} trả HTTP {StatusCode} không mong đợi.", operation, status);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: false);
        }

        GhnEnvelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<GhnEnvelope<T>>(body, JsonOptions);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        // Thân không đọc được là lỗi phía GHN hoặc proxy trung gian. Không ghi thân vì thân có thể chứa dữ liệu khách hàng.
        if (envelope is null)
        {
            _logger.LogError("GHN {Operation} trả thân phản hồi không đọc được.", operation);
            throw new GhnAttemptException(new ShippingUnavailableException(), isTransient: false);
        }

        // HTTP 200 nhưng `code` khác 200: GHN từ chối trong thân phản hồi.
        if (envelope.Code != 200)
        {
            throw Rejected(operation, envelope.Message, envelope.Code);
        }

        return envelope.Data;
    }

    private GhnAttemptException Rejected(string operation, string? carrierMessage, int carrierCode)
    {
        return new GhnAttemptException(CreateRejection(operation, carrierMessage, carrierCode), isTransient: false);
    }

    private ShippingRejectedException CreateRejection(string operation, string? carrierMessage, int carrierCode)
    {
        var safeMessage = SanitizeCarrierMessage(carrierMessage);
        _logger.LogWarning("GHN {Operation} từ chối với mã {CarrierCode}: {CarrierMessage}", operation, carrierCode, safeMessage);
        return new ShippingRejectedException(safeMessage);
    }

    // Đọc `message` của GHN từ thân lỗi. Thân không đọc được thì bỏ qua, vì HTTP 400 đã đủ để biết bị từ chối.
    private static string? ReadCarrierMessage(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<GhnEnvelope<JsonElement>>(body, JsonOptions)?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Làm sạch thông báo của GHN trước khi nối vào lỗi cho người dùng và log. Thông báo không được mang token ra ngoài.
    private string? SanitizeCarrierMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var cleaned = message.Replace(_token, "***", StringComparison.Ordinal);
        cleaned = new string(cleaned.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return cleaned.Length <= MaxCarrierMessageLength ? cleaned : cleaned[..MaxCarrierMessageLength];
    }

    // Thiếu `data` ở thao tác cần dữ liệu nghĩa là phản hồi hỏng, không phải kết quả rỗng.
    private T RequireData<T>(string operation, T? data)
        where T : class
    {
        if (data is null)
        {
            _logger.LogError("GHN {Operation} trả phản hồi thiếu dữ liệu.", operation);
            throw new ShippingUnavailableException();
        }

        return data;
    }

    // Lỗi nội bộ của một lần gọi. Mang lỗi nghiệp vụ đã quy đổi và cờ cho phép thử lại.
    private sealed class GhnAttemptException(AppException mapped, bool isTransient) : Exception
    {
        public AppException Mapped { get; } = mapped;
        public bool IsTransient { get; } = isTransient;
    }
}
