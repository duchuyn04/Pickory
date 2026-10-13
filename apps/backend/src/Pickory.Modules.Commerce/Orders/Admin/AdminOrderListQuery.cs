using System.Globalization;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Tham số lọc danh sách đơn của nhân viên. Mọi tham số đều tùy chọn và kết hợp theo AND.
public sealed record AdminOrderListQuery(
    PageQuery Page,
    OrderStatus? Status,
    PaymentStatus? PaymentStatus,
    PaymentMethod? PaymentMethod,
    string? Flag,
    DateOnly? From,
    DateOnly? To,
    SearchTerm? Search)
{
    // Cờ đơn ghi vào `orders.flags`. Bốn cờ đầu do luồng thanh toán ghi (`SepayPaymentService`, `SepayLatePaymentMatcher`,
    // `ExpiredPaymentProcessor`), `invoice_issued` do nhân viên đánh dấu, còn `shipment_cancelled` do `ShipmentStatusChangedHandler` ghi.
    public static readonly IReadOnlyList<string> KnownFlags =
    [
        "underpaid",
        "overpaid",
        "late_payment",
        "payment_expired",
        AdminOrderActionService.InvoiceIssuedFlag,
        ShipmentStatusChangedHandler.ShipmentCancelledFlag,
    ];

    private const string DateFormat = "yyyy-MM-dd";

    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static AdminOrderListQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        var page = PageQuery.Read(query, failures);
        var status = ReadLabel<OrderStatus>(query, "status", failures);
        var paymentStatus = ReadLabel<PaymentStatus>(query, "paymentStatus", failures);
        var paymentMethod = ReadLabel<PaymentMethod>(query, "paymentMethod", failures);
        var flag = ReadFlag(query, failures);
        var from = ReadDate(query, "from", failures);
        var to = ReadDate(query, "to", failures);
        var search = SearchTerm.Read(query, failures);

        // Khoảng ngược không có đơn nào khớp. Báo lỗi để người dùng sửa thay vì nhận danh sách rỗng khó hiểu.
        if (from is not null && to is not null && from > to)
        {
            failures.Add(new ValidationFailure("from", "Ngày bắt đầu không được sau ngày kết thúc."));
        }

        return new AdminOrderListQuery(page, status, paymentStatus, paymentMethod, flag, from, to, search);
    }

    private static TEnum? ReadLabel<TEnum>(IQueryCollection query, string name, List<ValidationFailure> failures)
        where TEnum : struct, Enum
    {
        var raw = QueryParameter.ReadSingle(query, name, failures);
        if (raw is null)
        {
            return null;
        }

        if (!OrderLabels.TryParse<TEnum>(raw, out var value))
        {
            failures.Add(new ValidationFailure(name, $"Giá trị của `{name}` không hợp lệ."));
            return null;
        }

        return value;
    }

    private static string? ReadFlag(IQueryCollection query, List<ValidationFailure> failures)
    {
        var raw = QueryParameter.ReadSingle(query, "flag", failures);
        if (raw is null)
        {
            return null;
        }

        if (!KnownFlags.Contains(raw, StringComparer.Ordinal))
        {
            failures.Add(new ValidationFailure("flag", "Cờ đơn không hợp lệ."));
            return null;
        }

        return raw;
    }

    private static DateOnly? ReadDate(IQueryCollection query, string name, List<ValidationFailure> failures)
    {
        var raw = QueryParameter.ReadSingle(query, name, failures);
        if (raw is null)
        {
            return null;
        }

        // Chỉ nhận đúng `yyyy-MM-dd`. Dạng có giờ hoặc múi giờ bị từ chối để ngày luôn hiểu theo giờ Việt Nam.
        if (!DateOnly.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            failures.Add(new ValidationFailure(name, $"Ngày của `{name}` phải có dạng yyyy-MM-dd."));
            return null;
        }

        return date;
    }
}
