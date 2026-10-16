using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Reviews.Admin;

// Tham số của hàng chờ kiểm duyệt. `Status` là `visible`, `hidden`, hoặc `null` để lấy cả hai.
internal sealed record AdminReviewListQuery(PageQuery Page, string? Status)
{
    public const string VisibleStatus = "visible";
    public const string HiddenStatus = "hidden";

    private const string StatusMessage = "`status` chỉ nhận `visible` hoặc `hidden`.";

    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static AdminReviewListQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        var page = PageQuery.Read(query, failures);
        var status = QueryParameter.ReadSingle(query, "status", failures);

        // So sánh phân biệt hoa thường, vì giá trị lọc dùng đúng nhãn của tài liệu route.
        if (status is not null && status != VisibleStatus && status != HiddenStatus)
        {
            failures.Add(new ValidationFailure("status", StatusMessage));
            status = null;
        }

        return new AdminReviewListQuery(page, status);
    }
}
