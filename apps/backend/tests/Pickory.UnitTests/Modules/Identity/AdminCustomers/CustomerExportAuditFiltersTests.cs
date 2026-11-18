using Pickory.Modules.Identity.Features.AdminCustomers;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.AdminCustomers;

// Bộ lọc ghi vào nhật ký của route xuất khách. Khóa là tên tham số của query.
public sealed class CustomerExportAuditFiltersTests
{
    private static CustomerListQuery Filter(
        string? search = null,
        CustomerStatusFilter? status = null,
        bool? emailVerified = null) =>
        new(new PageQuery(1, 20), search is null ? null : new SearchTerm(search, "%" + search + "%"), status, emailVerified);

    [Fact]
    // Không có tham số lọc thì nhật ký ghi object rỗng, không ghi `page` hay `limit`.
    public void From_NoFilters_ReturnsEmpty()
    {
        Assert.Empty(CustomerExportAuditFilters.From(Filter()));
    }

    [Fact]
    // Từ khóa ghi đúng chữ người dùng gõ, không ghi mẫu `LIKE` đã escape.
    public void From_Search_RecordsTypedText()
    {
        var filters = CustomerExportAuditFilters.From(Filter(search: "minh thu"));

        Assert.Equal(new Dictionary<string, string> { ["search"] = "minh thu" }, filters);
    }

    [Theory]
    [InlineData(CustomerStatusFilter.Active, "active")]
    [InlineData(CustomerStatusFilter.Locked, "locked")]
    // Giá trị `status` ghi đúng chữ mà người dùng gửi trong query.
    public void From_Status_RecordsQueryValue(CustomerStatusFilter status, string expected)
    {
        var filters = CustomerExportAuditFilters.From(Filter(status: status));

        Assert.Equal(new Dictionary<string, string> { ["status"] = expected }, filters);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    // Giá trị `false` vẫn là bộ lọc có giá trị, nên nhật ký phải ghi nó.
    public void From_EmailVerified_RecordsQueryValue(bool emailVerified, string expected)
    {
        var filters = CustomerExportAuditFilters.From(Filter(emailVerified: emailVerified));

        Assert.Equal(new Dictionary<string, string> { ["emailVerified"] = expected }, filters);
    }

    [Fact]
    public void From_AllFilters_RecordsEveryParameterByQueryName()
    {
        var filters = CustomerExportAuditFilters.From(Filter("an", CustomerStatusFilter.Locked, true));

        Assert.Equal(
            new Dictionary<string, string> { ["search"] = "an", ["status"] = "locked", ["emailVerified"] = "true" },
            filters);
    }
}
