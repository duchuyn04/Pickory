using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Contracts.Reading;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Reviews;

// Kết quả tính toán tóm tắt điểm đánh giá và phân bố sao.
internal sealed record RatingCalculation(
    double? Average,
    int Count,
    IReadOnlyDictionary<int, int> Distribution);

// Một dòng đánh giá hiển thị công khai dùng chung giữa route web và cổng đọc reader.
internal sealed record VisibleReviewRow(
    string Id,
    int Rating,
    string? Body,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Kết quả phân trang danh sách đánh giá công khai kèm con trỏ tiếp theo.
internal sealed record VisibleReviewQueryResult(
    IReadOnlyList<VisibleReviewRow> Items,
    string? NextCursor);

// Lớp dùng chung giữa ReviewService và CommerceReader để truy vấn và tính toán đánh giá hiển thị.
internal static class VisibleReviewReader
{
    // Truy vấn các bản ghi đánh giá chưa bị ẩn bởi quản trị viên.
    public static IQueryable<Review> QueryVisibleReviews(CommerceDbContext database) =>
        database.Reviews.AsNoTracking().Where(review => review.HiddenAt == null);

    // Tính điểm trung bình và phân bố sao từ danh sách số lượng của từng mức sao.
    public static RatingCalculation CalculateSummary(IEnumerable<(int Rating, int Count)> counts)
    {
        var countMap = counts.ToDictionary(item => item.Rating, item => item.Count);
        var distribution = new Dictionary<int, int>(ReviewRequests.MaxRating - ReviewRequests.MinRating + 1);

        // Luôn điền đủ năm mức sao từ 1 đến 5 để người dùng xem biểu đồ không bị thiếu khóa.
        for (var star = ReviewRequests.MinRating; star <= ReviewRequests.MaxRating; star++)
        {
            distribution[star] = countMap.GetValueOrDefault(star, 0);
        }

        var total = distribution.Values.Sum();
        if (total == 0)
        {
            return new RatingCalculation(null, 0, distribution);
        }

        var weightedSum = distribution.Sum(entry => entry.Key * entry.Value);
        // Làm tròn một chữ số thập phân theo kiểu thông thường (0,5 làm tròn lên), không theo kiểu làm tròn về số chẵn.
        var average = Math.Round((double)weightedSum / total, 1, MidpointRounding.AwayFromZero);
        return new RatingCalculation(average, total, distribution);
    }

    // Đọc tóm tắt đánh giá cho một sản phẩm duy nhất phục vụ route chi tiết sản phẩm.
    public static async Task<ProductReviewSummaryResponse> ReadProductSummaryAsync(
        CommerceDbContext database, string productId, CancellationToken cancellationToken)
    {
        // Tái sử dụng hàm đọc tóm tắt danh sách để bảo đảm cùng một logic gom nhóm và làm tròn.
        var summaries = await ReadRatingSummariesAsync(database, [productId], cancellationToken);
        var summary = summaries[productId];

        // Chuyển khóa số sang dạng chuỗi theo đúng định dạng response của route công khai.
        var stringDistribution = summary.Distribution.ToDictionary(
            entry => entry.Key.ToString(CultureInfo.InvariantCulture),
            entry => entry.Value);

        return new ProductReviewSummaryResponse(summary.Average, summary.Count, stringDistribution);
    }

    // Đọc tóm tắt đánh giá cho nhiều sản phẩm trong một truy vấn duy nhất.
    public static async Task<IReadOnlyDictionary<string, RatingSummary>> ReadRatingSummariesAsync(
        CommerceDbContext database, IReadOnlyList<string> productIds, CancellationToken cancellationToken)
    {
        // Lọc bỏ mã trống và loại trừ các phần tử trùng lặp trước khi truy vấn database.
        var distinctIds = productIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinctIds.Count == 0)
        {
            return new Dictionary<string, RatingSummary>();
        }

        // Gom nhóm theo mã sản phẩm và mức sao trong cùng một câu lệnh truy vấn.
        var counts = await QueryVisibleReviews(database)
            .Where(review => distinctIds.Contains(review.ProductId))
            .GroupBy(review => new { review.ProductId, review.Rating })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.Rating,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        var groupedByProduct = counts
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new Dictionary<string, RatingSummary>(distinctIds.Count, StringComparer.Ordinal);

        // Duyệt từng mã sản phẩm yêu cầu để bảo đảm luôn có mục tóm tắt tương ứng.
        foreach (var productId in distinctIds)
        {
            if (groupedByProduct.TryGetValue(productId, out var productCounts))
            {
                var calculation = CalculateSummary(productCounts.Select(item => (item.Rating, item.Count)));
                result[productId] = new RatingSummary(calculation.Average, calculation.Count, calculation.Distribution);
            }
            else
            {
                // Sản phẩm chưa có đánh giá nào vẫn có tóm tắt với số lượng bằng 0.
                var emptyCalc = CalculateSummary([]);
                result[productId] = new RatingSummary(emptyCalc.Average, emptyCalc.Count, emptyCalc.Distribution);
            }
        }

        return result;
    }

    // Đọc danh sách đánh giá hiển thị kèm con trỏ phân trang dùng chung.
    public static async Task<VisibleReviewQueryResult> ReadVisibleReviewsAsync(
        CommerceDbContext database,
        string productId,
        int limit,
        DateTime? cursorCreatedAt,
        string? cursorId,
        CancellationToken cancellationToken)
    {
        // Chỉ đánh giá đang hiện được liệt kê. Tóm tắt và trang dùng cùng điều kiện nên số liệu khớp nhau.
        var visible = QueryVisibleReviews(database)
            .Where(review => review.ProductId == productId);

        // Cursor trỏ tới dòng cuối của trang trước. Dòng cùng thời điểm được chia nốt theo Id để không lặp hay mất dòng.
        if (cursorCreatedAt.HasValue && !string.IsNullOrEmpty(cursorId))
        {
            visible = visible.Where(review =>
                review.CreatedAt < cursorCreatedAt.Value
                || (review.CreatedAt == cursorCreatedAt.Value && review.Id.CompareTo(cursorId) > 0));
        }

        // Lấy thêm một dòng để biết còn trang sau mà không cần đếm cả bảng.
        var rows = await visible
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit + 1)
            .Select(review => new
            {
                review.Id,
                review.Rating,
                review.Body,
                review.UserId,
                review.CreatedAt,
                review.UpdatedAt,
                // Tên người nhận nằm trong Contact của đơn, vì identity không có hàm đọc tên cho module khác.
                Contact = review.Order!.Contact,
            })
            .ToListAsync(cancellationToken);

        var pageRows = rows.Take(limit).ToList();
        string? nextCursor = null;

        // Sinh con trỏ trang kế tiếp từ thời điểm tạo và mã của dòng cuối cùng.
        if (rows.Count > limit)
        {
            var last = pageRows[^1];
            nextCursor = ReviewCursor.Encode(last.CreatedAt, last.Id);
        }

        // Rút gọn tên tác giả và chuẩn hóa thời gian UTC cho từng bản ghi.
        var items = pageRows
            .Select(row => new VisibleReviewRow(
                row.Id,
                row.Rating,
                row.Body,
                ReviewAuthorName.ForReview(row.UserId, row.Contact),
                DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)))
            .ToList();

        return new VisibleReviewQueryResult(items, nextCursor);
    }
}
