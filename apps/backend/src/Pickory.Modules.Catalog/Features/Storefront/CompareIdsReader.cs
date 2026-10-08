using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.Catalog.Reading;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Đọc tham số `ids` của `GET /api/products/compare`. Giá trị sai nhận lỗi `VALIDATION_ERROR`.
public static class CompareIdsReader
{
    public const int MaxProducts = 4;

    public static IReadOnlyList<string> Read(string? rawIds)
    {
        var splitIds = (rawIds ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        // Bỏ id trùng trước khi đếm. Người dùng thêm cùng một sản phẩm hai lần vẫn so sánh được.
        var ids = RequestedIdOrder.Normalize(splitIds);

        if (ids.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("ids", "Cần ít nhất một id sản phẩm để so sánh.")]);
        }

        // Trần 4 sản phẩm giữ bảng so sánh đọc được và chặn truy vấn lớn.
        if (ids.Count > MaxProducts)
        {
            throw new ValidationException(
                [new ValidationFailure("ids", $"Chỉ so sánh được tối đa {MaxProducts} sản phẩm.")]);
        }

        return ids;
    }
}
