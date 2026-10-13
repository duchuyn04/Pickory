using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Pickory.Shared.Http;

// Đọc tham số query chỉ nhận một giá trị. Danh sách back-office dùng cho các bộ lọc như `status` và `role`.
public static class QueryParameter
{
    // Lỗi được thêm vào `failures` và route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    // Trả `null` khi thiếu tham số hoặc giá trị trống, để bên gọi bỏ bộ lọc.
    public static string? ReadSingle(IQueryCollection query, string name, List<ValidationFailure> failures)
    {
        if (!query.TryGetValue(name, out var values))
        {
            return null;
        }

        // Tham số lặp lại là lỗi, vì không rõ giá trị nào là ý của người dùng.
        if (values.Count > 1)
        {
            failures.Add(new ValidationFailure(name, $"Tham số `{name}` chỉ được gửi một lần."));
            return null;
        }

        // Giá trị trống được coi như không gửi, để form lọc để trống ô không gây lỗi.
        var raw = values.ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}
