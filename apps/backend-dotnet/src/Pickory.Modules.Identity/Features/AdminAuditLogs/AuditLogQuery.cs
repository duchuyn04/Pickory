using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminAuditLogs;

// Bộ lọc của trang nhật ký thao tác. Mọi điều kiện đều tùy chọn.
public sealed partial record AuditLogQuery(
    string? ActorId,
    string? Action,
    string? TargetType,
    DateTime? StartDate,
    DateTime? EndDate,
    PageQuery Page)
{
    private const string DateFormatMessage = "Thời gian phải theo định dạng ISO 8601 có đuôi Z.";

    public static AuditLogQuery Read(IQueryCollection query)
    {
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(query, failures);
        var startDate = ReadUtcDate(query, "startDate", failures);
        var endDate = ReadUtcDate(query, "endDate", failures);
        // Báo mọi tham số sai trong một lỗi. Người dùng sửa được tất cả trong một lần gửi.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new AuditLogQuery(
            query["actorId"].FirstOrDefault(),
            query["action"].FirstOrDefault(),
            query["targetType"].FirstOrDefault(),
            startDate,
            endDate,
            page);
    }

    private static DateTime? ReadUtcDate(IQueryCollection query, string name, List<ValidationFailure> failures)
    {
        if (!query.TryGetValue(name, out var rawValues))
        {
            return null;
        }

        // Chỉ nhận chuỗi ISO có đuôi `Z`, giống `z.string().datetime()` của bản Node. Giao diện phải đổi ngày sang UTC trước khi gửi.
        var rawValue = rawValues.ToString();
        if (!IsoUtcPattern().IsMatch(rawValue)
            || !DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
        {
            failures.Add(new ValidationFailure(name, DateFormatMessage));
            return null;
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?Z$")]
    private static partial Regex IsoUtcPattern();
}
