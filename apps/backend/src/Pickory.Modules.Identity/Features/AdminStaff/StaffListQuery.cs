using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminStaff;

// Trạng thái của nhân viên trong bộ lọc danh sách. Suy ra từ `LockedAt` và `PasswordHash`, không có cột riêng.
public enum StaffStatusFilter
{
    Active,
    Invited,
    Locked,
}

// Tham số lọc danh sách nhân viên. Mọi tham số đều tùy chọn và kết hợp theo AND.
public sealed record StaffListQuery(
    PageQuery Page,
    SearchTerm? Search,
    StaffStatusFilter? Status,
    string? RoleCode)
{
    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static StaffListQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        var page = PageQuery.Read(query, failures);
        var search = SearchTerm.Read(query, failures);
        var status = ReadStatus(query, failures);

        // Mã role so khớp đúng chữ với `Role.Code`. Mã không tồn tại cho danh sách rỗng, không phải lỗi.
        var roleCode = QueryParameter.ReadSingle(query, "role", failures);
        return new StaffListQuery(page, search, status, roleCode);
    }

    private static StaffStatusFilter? ReadStatus(IQueryCollection query, List<ValidationFailure> failures)
    {
        var raw = QueryParameter.ReadSingle(query, "status", failures);
        if (raw is null)
        {
            return null;
        }

        // So sánh đúng chữ thường. Giá trị lạ nhận lỗi, vì danh sách không lọc làm nhân viên hiểu sai kết quả.
        switch (raw)
        {
            case StaffStatusLabels.Active:
                return StaffStatusFilter.Active;
            case StaffStatusLabels.Invited:
                return StaffStatusFilter.Invited;
            case StaffStatusLabels.Locked:
                return StaffStatusFilter.Locked;
            default:
                failures.Add(new ValidationFailure("status", "Trạng thái phải là `active`, `invited` hoặc `locked`."));
                return null;
        }
    }
}
