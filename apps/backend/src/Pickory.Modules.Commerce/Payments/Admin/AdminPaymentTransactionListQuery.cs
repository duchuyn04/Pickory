using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Tham số lọc danh sách giao dịch SePay của nhân viên. Mọi tham số đều tùy chọn và kết hợp theo AND.
public sealed record AdminPaymentTransactionListQuery(PageQuery Page, string? MatchStatus, string? OrderFlag)
{
    // Ba trạng thái khớp mà `SepayPaymentService` và route đối soát tay ghi vào `payment_transactions.matchStatus`.
    public static readonly IReadOnlyList<string> KnownMatchStatuses = ["unmatched", "matched", "ignored"];

    // Màn đối soát chỉ cần hai cờ do lệch số tiền chuyển khoản. Các cờ khác của đơn không thuộc màn này.
    public static readonly IReadOnlyList<string> KnownOrderFlags = ["underpaid", "overpaid"];

    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static AdminPaymentTransactionListQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        var page = PageQuery.Read(query, failures);
        var matchStatus = ReadKnownValue(query, "matchStatus", KnownMatchStatuses, failures);
        var orderFlag = ReadKnownValue(query, "orderFlag", KnownOrderFlags, failures);
        return new AdminPaymentTransactionListQuery(page, matchStatus, orderFlag);
    }

    private static string? ReadKnownValue(
        IQueryCollection query, string name, IReadOnlyList<string> allowedValues, List<ValidationFailure> failures)
    {
        var raw = QueryParameter.ReadSingle(query, name, failures);
        if (raw is null)
        {
            return null;
        }

        // So sánh phân biệt hoa thường, vì giá trị lọc dùng đúng nhãn snake_case của response.
        if (!allowedValues.Contains(raw, StringComparer.Ordinal))
        {
            failures.Add(new ValidationFailure(name, $"Giá trị của `{name}` không hợp lệ."));
            return null;
        }

        return raw;
    }
}
