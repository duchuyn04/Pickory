using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Lỗi 400 khi `attr.*` nêu khóa không tồn tại hoặc không `filterable`. `Keys` đưa tên khóa sai vào `details`.
public sealed class UnknownFilterException : AppException
{
    public const string ErrorCode = "UNKNOWN_FILTER";

    public IReadOnlyList<string> Keys { get; }

    public UnknownFilterException(IReadOnlyList<string> keys)
        : base(ErrorCode, 400, $"Bộ lọc thông số không hợp lệ: {string.Join(", ", keys)}.")
    {
        Keys = keys;
    }
}
