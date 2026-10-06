using System.Globalization;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Pickory.Shared.Http;

// Tham số `page` và `limit` trên query string. Giá trị mặc định và giới hạn giữ nguyên bản Node.
public sealed record PageQuery(int Page, int Limit)
{
    private const int DefaultLimit = 20;
    // Trần 100 dòng mỗi trang. Một request không kéo về cả bảng dữ liệu.
    private const int MaxLimit = 100;

    public int Skip => (Page - 1) * Limit;

    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static PageQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        var page = ReadInteger(query, "page", defaultValue: 1, minimum: 1, maximum: int.MaxValue, failures, "Số trang phải là số nguyên lớn hơn hoặc bằng 1.");
        var limit = ReadInteger(query, "limit", DefaultLimit, minimum: 1, MaxLimit, failures, "Số dòng mỗi trang phải là số nguyên từ 1 đến 100.");
        return new PageQuery(page, limit);
    }

    private static int ReadInteger(
        IQueryCollection query,
        string name,
        int defaultValue,
        int minimum,
        int maximum,
        List<ValidationFailure> failures,
        string message)
    {
        // Query string luôn là chuỗi. Thiếu tham số thì dùng giá trị mặc định.
        if (!query.TryGetValue(name, out var rawValues))
        {
            return defaultValue;
        }

        // Chỉ nhận số nguyên trong khoảng cho phép. Chuỗi lạ nhận lỗi validate thay vì lỗi 500.
        if (!int.TryParse(rawValues.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
        {
            failures.Add(new ValidationFailure(name, message));
            return defaultValue;
        }

        return value;
    }
}
