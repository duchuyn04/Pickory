using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Trạng thái khóa của khách trong bộ lọc danh sách. Suy ra từ `LockedAt`, không có cột riêng.
public enum CustomerStatusFilter
{
    Active,
    Locked,
}

// Tham số lọc danh sách khách của nhân viên. Mọi tham số đều tùy chọn và kết hợp theo AND.
public sealed record CustomerListQuery(
    PageQuery Page,
    SearchTerm? Search,
    CustomerStatusFilter? Status,
    bool? EmailVerified)
{
    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static CustomerListQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        var page = PageQuery.Read(query, failures);
        var search = SearchTerm.Read(query, failures);
        var status = ReadStatus(query, failures);
        var emailVerified = ReadEmailVerified(query, failures);
        return new CustomerListQuery(page, search, status, emailVerified);
    }

    private static CustomerStatusFilter? ReadStatus(IQueryCollection query, List<ValidationFailure> failures)
    {
        var raw = ReadSingleValue(query, "status", failures);
        if (raw is null)
        {
            return null;
        }

        // So sánh đúng chữ thường. Giá trị lạ nhận lỗi, vì danh sách không lọc làm nhân viên hiểu sai kết quả.
        return raw switch
        {
            "active" => CustomerStatusFilter.Active,
            "locked" => CustomerStatusFilter.Locked,
            _ => AddFailure<CustomerStatusFilter>(failures, "status", "Trạng thái phải là `active` hoặc `locked`."),
        };
    }

    private static bool? ReadEmailVerified(IQueryCollection query, List<ValidationFailure> failures)
    {
        var raw = ReadSingleValue(query, "emailVerified", failures);
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            "true" => true,
            "false" => false,
            _ => AddFailure<bool>(failures, "emailVerified", "Trạng thái xác minh email phải là `true` hoặc `false`."),
        };
    }

    // Tham số trống được coi như không gửi, để form lọc để trống ô không gây lỗi.
    // Tham số lặp lại là lỗi, vì không rõ giá trị nào là ý của người dùng.
    private static string? ReadSingleValue(IQueryCollection query, string name, List<ValidationFailure> failures)
    {
        if (!query.TryGetValue(name, out var values))
        {
            return null;
        }

        if (values.Count > 1)
        {
            failures.Add(new ValidationFailure(name, $"Tham số `{name}` chỉ được gửi một lần."));
            return null;
        }

        var raw = values.ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static TValue? AddFailure<TValue>(List<ValidationFailure> failures, string name, string message)
        where TValue : struct
    {
        failures.Add(new ValidationFailure(name, message));
        return null;
    }
}
