using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Kiểm luật tên và số điện thoại ở ba route nhận hai trường này. Test gửi request thẳng vào API, không qua giao diện.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ProfileValidationTests : IClassFixture<PickoryWebApplicationFactory>
{
    private const string PhoneInvalidMessage = "Số điện thoại không đúng định dạng.";
    private const string NameTooLongMessage = "Họ và tên tối đa 100 ký tự.";
    private const string NameInvalidMessage = "Họ và tên chứa ký tự không hợp lệ.";

    // Đúng 100 ký tự có dấu. Mỗi ký tự chiếm nhiều byte, nên test chứng minh đếm theo ký tự chứ không theo byte.
    private static readonly string HundredVietnameseCharacters = string.Concat(Enumerable.Repeat("Ệ", 100));

    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public ProfileValidationTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ đếm theo IP dùng chung trong một factory. Mỗi test bắt đầu với bộ đếm trống.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
        // Đăng ký gán role `customer`, nên role này phải có sẵn trong DB.
        IdentityTestData.SeedRolesAsync(database).GetAwaiter().GetResult();
    }

    public static TheoryData<string> InvalidPhones => new() { "abc", "12345", "0123456789", "+8412345678" };

    public static TheoryData<string> ValidPhoneForms => new() { "0912345678", "0912 345 678", "091.234.5678", "+84912345678" };

    public static TheoryData<string, string> InvalidNames => new()
    {
        { new string('a', 101), NameTooLongMessage },
        { "Tên\u0000x", NameInvalidMessage },
        { "Tên\nxuống dòng", NameInvalidMessage },
        { "Tên\u007Fx", NameInvalidMessage },
    };

    // ---------- Đăng ký ----------

    [Theory]
    [MemberData(nameof(InvalidPhones))]
    // Số sai dạng nhận 400 và không tạo tài khoản.
    public async Task Register_InvalidPhone_Returns400AndCreatesNoUser(string phone)
    {
        var email = IdentityTestData.NewEmail("reg-bad-phone");

        var response = await PostRegisterAsync(email, name: "Khách", phone: phone);

        await AssertValidationErrorAsync(response, PhoneInvalidMessage);
        Assert.False(await UserExistsAsync(email));
    }

    [Theory]
    [MemberData(nameof(ValidPhoneForms))]
    // Mọi dạng hợp lệ lưu về `0912345678`.
    public async Task Register_ValidPhoneForms_StoreCanonicalNumber(string phone)
    {
        var email = IdentityTestData.NewEmail("reg-good-phone");

        var response = await PostRegisterAsync(email, name: "Khách", phone: phone);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("0912345678", (await ReadUserByEmailAsync(email)).Phone);
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Register_InvalidName_Returns400AndCreatesNoUser(string name, string expectedMessage)
    {
        var email = IdentityTestData.NewEmail("reg-bad-name");

        var response = await PostRegisterAsync(email, name: name, phone: null);

        await AssertValidationErrorAsync(response, expectedMessage);
        Assert.False(await UserExistsAsync(email));
    }

    [Fact]
    public async Task Register_NameOfExactlyOneHundredVietnameseCharacters_IsAccepted()
    {
        var email = IdentityTestData.NewEmail("reg-name-100");

        var response = await PostRegisterAsync(email, name: HundredVietnameseCharacters, phone: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HundredVietnameseCharacters, (await ReadUserByEmailAsync(email)).Name);
    }

    [Fact]
    // Hành vi cũ: đăng ký không có tên và số điện thoại vẫn thành công.
    public async Task Register_WithoutNameAndPhone_StillSucceeds()
    {
        var email = IdentityTestData.NewEmail("reg-no-profile");

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/register", new { email, password = IdentityTestData.DefaultPassword }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await ReadUserByEmailAsync(email);
        Assert.Null(stored.Name);
        Assert.Null(stored.Phone);
    }

    // ---------- Cập nhật hồ sơ ----------

    [Theory]
    [MemberData(nameof(InvalidPhones))]
    // Số sai dạng nhận 400 và DB giữ nguyên cả tên lẫn số điện thoại.
    public async Task UpdateProfile_InvalidPhone_Returns400AndKeepsDatabase(string phone)
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-bad-phone");

        var response = await PatchMeAsync(sessionToken, new { name = "Tên mới", phone });

        await AssertValidationErrorAsync(response, PhoneInvalidMessage);
        var stored = await ReadUserAsync(user.Id);
        Assert.Equal("Người dùng thử", stored.Name);
        Assert.Equal("0901234567", stored.Phone);
    }

    [Theory]
    [MemberData(nameof(ValidPhoneForms))]
    public async Task UpdateProfile_ValidPhoneForms_StoreCanonicalNumber(string phone)
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-good-phone");

        var response = await PatchMeAsync(sessionToken, new { phone });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0912345678", (await ReadUserAsync(user.Id)).Phone);
        // Response trả số đã chuẩn hóa. Trang tài khoản hiện đúng giá trị đã lưu.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("0912345678", document.RootElement.GetProperty("data").GetProperty("user").GetProperty("phone").GetString());
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task UpdateProfile_InvalidName_Returns400AndKeepsDatabase(string name, string expectedMessage)
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-bad-name");

        var response = await PatchMeAsync(sessionToken, new { name });

        await AssertValidationErrorAsync(response, expectedMessage);
        Assert.Equal("Người dùng thử", (await ReadUserAsync(user.Id)).Name);
    }

    [Fact]
    public async Task UpdateProfile_NameOfExactlyOneHundredVietnameseCharacters_IsAccepted()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-name-100");

        var response = await PatchMeAsync(sessionToken, new { name = HundredVietnameseCharacters });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HundredVietnameseCharacters, (await ReadUserAsync(user.Id)).Name);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789")]
    // Luật chỉ kiểm trường được gửi. Người dùng có số cũ sai dạng vẫn sửa được tên.
    public async Task UpdateProfile_OnlyName_IgnoresStoredPhoneThatIsInvalid(string storedPhone)
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-stale-phone");
        await SetStoredPhoneAsync(user.Id, storedPhone);

        var response = await PatchMeAsync(sessionToken, new { name = "Tên đã sửa" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadUserAsync(user.Id);
        Assert.Equal("Tên đã sửa", stored.Name);
        Assert.Equal(storedPhone, stored.Phone);
    }

    [Fact]
    // Hành vi cũ: `null` và chuỗi trống vẫn xóa giá trị.
    public async Task UpdateProfile_NullAndBlankStillClearValues()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-clear");

        var response = await PatchMeAsync(sessionToken, new { name = "   ", phone = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadUserAsync(user.Id);
        Assert.Null(stored.Name);
        Assert.Null(stored.Phone);
    }

    // ---------- Mời nhân viên ----------

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task InviteStaff_InvalidName_Returns400AndCreatesNoUser(string name, string expectedMessage)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var email = IdentityTestData.NewEmail("invite-bad-name");

        var response = await PostInviteAsync(adminSession, email, name);

        await AssertValidationErrorAsync(response, expectedMessage);
        Assert.False(await UserExistsAsync(email));
    }

    [Fact]
    public async Task InviteStaff_NameOfExactlyOneHundredVietnameseCharacters_IsAccepted()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var email = IdentityTestData.NewEmail("invite-name-100");

        var response = await PostInviteAsync(adminSession, email, HundredVietnameseCharacters);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HundredVietnameseCharacters, (await ReadUserByEmailAsync(email)).Name);
    }

    [Fact]
    // Hành vi cũ: tên vẫn bắt buộc khi mời nhân viên.
    public async Task InviteStaff_BlankName_StillReturns400()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await PostInviteAsync(adminSession, IdentityTestData.NewEmail("invite-blank"), "   ");

        await AssertValidationErrorAsync(response, "Họ và tên không được để trống.");
    }

    // ---------- Hàm dùng chung ----------

    private Task<HttpResponseMessage> PostRegisterAsync(string email, string? name, string? phone) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/register", new { email, password = IdentityTestData.DefaultPassword, name, phone }));

    private Task<HttpResponseMessage> PatchMeAsync(string sessionToken, object body) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Patch, "/api/me", body, $"pk_session={sessionToken}"));

    private Task<HttpResponseMessage> PostInviteAsync(string adminSession, string email, string name) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            "/api/admin/staff/invitations",
            new { email, name, roleCodes = new[] { "sales" } },
            $"pk_session={adminSession}"));

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string expectedMessage)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Contains(expectedMessage, error.GetProperty("message").GetString());
    }

    private async Task<(User User, string SessionToken)> CreateCustomerSessionAsync(string prefix)
    {
        var user = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail(prefix), UserKind.Customer, "customer");
        return (user, await IdentityTestData.CreateSessionAsync(_database, user.Id));
    }

    private async Task SetStoredPhoneAsync(string userId, string phone)
    {
        await using var identity = _database.CreateIdentityContext();
        var user = await identity.Users.SingleAsync(value => value.Id == userId);
        user.Phone = phone;
        await identity.SaveChangesAsync();
    }

    private async Task<User> ReadUserAsync(string userId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.SingleAsync(value => value.Id == userId);
    }

    private async Task<User> ReadUserByEmailAsync(string email)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.SingleAsync(value => value.Email == email);
    }

    private async Task<bool> UserExistsAsync(string email)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.AnyAsync(value => value.Email == email);
    }
}
