namespace Pickory.Modules.Catalog.Features.Storefront;

// Duyệt cây danh mục theo bảng `id -> parentId`. Dữ liệu sai có vòng cha con vẫn dừng, không treo request.
public static class CategoryTreeWalker
{
    // Trả chuỗi từ danh mục gốc đến danh mục đã chọn. Thuộc tính của cha nhờ đó đứng trước thuộc tính của con.
    public static IReadOnlyList<string> AncestorChain(string categoryId, IReadOnlyDictionary<string, string?> parentById)
    {
        var chain = new List<string>();
        var current = categoryId;
        // Mỗi danh mục chỉ vào chuỗi một lần. Vòng `ParentId` trong dữ liệu sai nhờ đó không lặp mãi.
        while (current is not null && !chain.Contains(current))
        {
            chain.Add(current);
            // Cha không có trong bảng (dữ liệu mồ côi) thì dừng, để chuỗi chỉ chứa danh mục có thật.
            var parentId = parentById.GetValueOrDefault(current);
            current = parentId is not null && parentById.ContainsKey(parentId) ? parentId : null;
        }

        chain.Reverse();
        return chain;
    }

    // Trả danh mục đã chọn và mọi danh mục con ở mọi cấp. Danh mục cha nhờ đó trả cả sản phẩm của con.
    public static IReadOnlyList<string> SelfAndDescendants(string rootId, IReadOnlyDictionary<string, string?> parentById)
    {
        var childrenByParent = parentById
            .Where(pair => pair.Value is not null)
            .ToLookup(pair => pair.Value!, pair => pair.Key);

        var collected = new List<string> { rootId };
        // Duyệt theo hàng đợi. Mỗi danh mục chỉ vào danh sách một lần, nên cây có vòng vẫn dừng.
        for (var index = 0; index < collected.Count; index++)
        {
            foreach (var childId in childrenByParent[collected[index]])
            {
                if (!collected.Contains(childId))
                {
                    collected.Add(childId);
                }
            }
        }

        return collected;
    }
}
