using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Features.AdminUsage;

// Khoảng thời gian của báo cáo usage. Thiếu mốc nào thì dùng mặc định 30 ngày gần nhất.
public sealed partial record UsageQuery(DateTime FromUtc, DateTime ToUtc)
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);
    // Giới hạn một quý. Query nhiều năm không được tải toàn bộ lịch sử vào bộ nhớ API.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(92);

    public static UsageQuery Read(IQueryCollection query, DateTime nowUtc)
    {
        var failures = new List<ValidationFailure>();
        var from = ReadUtcDate(query, "from", failures);
        var to = ReadUtcDate(query, "to", failures);
        // Gửi cả hai mốc mà mốc đầu không trước mốc cuối là dữ liệu sai, giống `refine` của bản Node.
        if (failures.Count == 0 && from is not null && to is not null && from >= to)
        {
            failures.Add(new ValidationFailure("from", "Khoảng thời gian không hợp lệ: from phải trước to."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var fromUtc = from ?? nowUtc - DefaultRange;
        var toUtc = to ?? nowUtc;
        // Kiểm tra lại sau khi điền mặc định. Chỉ gửi `from` ở tương lai cũng tạo khoảng ngược.
        if (fromUtc >= toUtc)
        {
            throw new AppException("INVALID_DATE_RANGE", 400, "Khoảng thời gian không hợp lệ: from phải trước to.");
        }

        if (toUtc - fromUtc > MaxRange)
        {
            throw new AppException("INVALID_DATE_RANGE", 400, "Báo cáo AI chỉ hỗ trợ khoảng thời gian tối đa 92 ngày.");
        }

        return new UsageQuery(fromUtc, toUtc);
    }

    private static DateTime? ReadUtcDate(IQueryCollection query, string name, List<ValidationFailure> failures)
    {
        if (!query.TryGetValue(name, out var rawValues))
        {
            return null;
        }

        // Chỉ nhận chuỗi ISO 8601 có đuôi `Z`, giống `z.string().datetime()` của bản Node.
        var rawValue = rawValues.ToString();
        if (!IsoUtcPattern().IsMatch(rawValue)
            || !DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
        {
            failures.Add(new ValidationFailure(name, $"Tham số {name} phải là chuỗi ngày giờ ISO 8601 UTC hợp lệ."));
            return null;
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?Z$")]
    private static partial Regex IsoUtcPattern();
}
