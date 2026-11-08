using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Customers;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Identity;

// Kiểm tra cổng đọc thông tin liên hệ khách hàng `ICustomerDirectory` qua `DbContext` thật.
[Collection(IdentityCollection.Name)]
public sealed class CustomerDirectoryTests(IdentityWebApplicationFactory factory, IdentityDatabaseFixture database)
    : IClassFixture<IdentityWebApplicationFactory>
{
    private async Task<T> WithDirectoryAsync<T>(Func<ICustomerDirectory, Task<T>> action)
    {
        // Mở phạm vi dịch vụ độc lập cho mỗi lần gọi để kiểm tra cấu hình DI của module.
        using var scope = factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<ICustomerDirectory>();
        return await action(directory);
    }

    [Fact]
    // Khách hàng đã có tên và email đầy đủ phải trả về đúng đối tượng liên hệ.
    public async Task GetContact_RegisteredCustomer_ReturnsNameAndEmail()
    {
        var email = IdentityTestData.NewEmail("customer-contact");
        var user = await IdentityTestData.CreateUserAsync(database, email, UserKind.Customer, DefaultRoles.CustomerCode);
        user.Name = "Nguyễn Minh Anh";
        await using (var identity = database.CreateIdentityContext())
        {
            identity.Users.Update(user);
            await identity.SaveChangesAsync();
        }

        // Gọi qua interface đăng ký trong DI container thật của ứng dụng.
        var contact = await WithDirectoryAsync(dir => dir.GetContactAsync(user.Id, CancellationToken.None));

        // Khách hàng tồn tại phải có đủ đối tượng liên hệ với tên và email chính xác.
        Assert.NotNull(contact);
        Assert.Equal("Nguyễn Minh Anh", contact.Name);
        Assert.Equal(email, contact.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    // Khách hàng không có tên hoặc tên rỗng phải trả về `Name` bằng `null` và giữ đúng email.
    public async Task GetContact_CustomerWithBlankName_ReturnsNullName(string? blankName)
    {
        var email = IdentityTestData.NewEmail("customer-blank");
        var user = await IdentityTestData.CreateUserAsync(database, email, UserKind.Customer, DefaultRoles.CustomerCode);
        user.Name = blankName;
        await using (var identity = database.CreateIdentityContext())
        {
            identity.Users.Update(user);
            await identity.SaveChangesAsync();
        }

        // Đọc thông tin của tài khoản có tên chưa hoàn thiện trong hồ sơ.
        var contact = await WithDirectoryAsync(dir => dir.GetContactAsync(user.Id, CancellationToken.None));

        // Tên rỗng phải được quy về `null` để lớp hiển thị không vẽ chuỗi trắng.
        Assert.NotNull(contact);
        Assert.Null(contact.Name);
        Assert.Equal(email, contact.Email);
    }

    [Fact]
    // Mã người dùng không tồn tại trong hệ thống phải trả về kết quả `null`.
    public async Task GetContact_UnknownUser_ReturnsNull()
    {
        var nonExistentId = Guid.NewGuid().ToString();

        // Tra cứu mã định danh ngẫu nhiên không có trong cơ sở dữ liệu.
        var contact = await WithDirectoryAsync(dir => dir.GetContactAsync(nonExistentId, CancellationToken.None));

        // Người dùng không tồn tại phải trả về `null` để báo hiệu không tìm thấy.
        Assert.Null(contact);
    }

    [Fact]
    // Tài khoản nhân viên không được coi là khách hàng và phải trả về `null`.
    public async Task GetContact_StaffUser_ReturnsNull()
    {
        var email = IdentityTestData.NewEmail("staff-contact");
        var staff = await IdentityTestData.CreateUserAsync(database, email, UserKind.Staff, DefaultRoles.SupportCode);

        // Tra cứu mã người dùng của tài khoản nhân viên hỗ trợ.
        var contact = await WithDirectoryAsync(dir => dir.GetContactAsync(staff.Id, CancellationToken.None));

        // Phân loại tài khoản `staff` phải bị chặn không trả về thông tin khách hàng.
        Assert.Null(contact);
    }
}
