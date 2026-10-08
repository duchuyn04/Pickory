namespace Pickory.Modules.Catalog.Reading;

// Giữ thứ tự id mà người gọi yêu cầu. Truy vấn DB không bảo đảm thứ tự này.
public static class RequestedIdOrder
{
    // Bỏ id rỗng và id trùng, giữ lần xuất hiện đầu. Id rỗng không thể khớp bản ghi nào.
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>();
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
            {
                normalized.Add(id);
            }
        }

        return normalized;
    }

    // Sắp kết quả theo `orderedIds`. Id không có bản ghi tương ứng bị bỏ khỏi kết quả.
    public static IReadOnlyList<T> Arrange<T>(
        IReadOnlyList<string> orderedIds, IEnumerable<T> items, Func<T, string> idOf)
    {
        var itemById = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            itemById[idOf(item)] = item;
        }

        var arranged = new List<T>(orderedIds.Count);
        foreach (var id in orderedIds)
        {
            if (itemById.TryGetValue(id, out var item))
            {
                arranged.Add(item);
            }
        }

        return arranged;
    }
}
