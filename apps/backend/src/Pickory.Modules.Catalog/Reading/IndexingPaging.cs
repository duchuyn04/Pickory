namespace Pickory.Modules.Catalog.Reading;

// Cắt trang cho `ListForIndexingAsync`. Cursor là `Id` cuối của trang trước, nên trang kế bắt đầu sau `Id` đó.
public static class IndexingPaging
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    public static void EnsurePageSizeInRange(int pageSize)
    {
        // Trần giữ mỗi lần gọi không nạp quá nhiều sản phẩm cùng ảnh và SKU vào bộ nhớ.
        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize), pageSize, $"Kích thước trang phải từ 1 đến {MaxPageSize}.");
        }
    }

    // `fetchedRows` được lấy dư một dòng so với `pageSize`. Dòng dư cho biết còn trang sau mà không cần đếm tổng.
    public static (IReadOnlyList<T> Items, string? Next) Split<T>(
        IReadOnlyList<T> fetchedRows, int pageSize, Func<T, string> idOf)
    {
        if (fetchedRows.Count <= pageSize)
        {
            return (fetchedRows, null);
        }

        var items = fetchedRows.Take(pageSize).ToList();
        // Cursor là `Id` của dòng cuối trong trang. Dòng dư được lấy lại ở trang kế, nên không bị lặp hay sót.
        return (items, idOf(items[^1]));
    }
}
