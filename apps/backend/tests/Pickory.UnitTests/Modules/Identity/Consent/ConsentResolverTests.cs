using Pickory.Modules.Identity.Consent;
using Pickory.Modules.Identity.Contracts.Actors;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Consent;

// Port từ test ID-08 của bản Node cho quy tắc chọn consent có hiệu lực.
public class ConsentResolverTests
{
    private static readonly DateTime Morning = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    // Chưa ai chọn thì mọi lựa chọn tắt. Hệ thống chỉ thu thập dữ liệu khi người dùng đồng ý chủ động.
    public void ResolveLatest_NoRecordsDisablesEverything()
    {
        Assert.Equal(ConsentState.AllDisabled, ConsentResolver.ResolveLatest(guestConsent: null, userConsent: null));
    }

    [Fact]
    // Chỉ một phía từng chọn thì lựa chọn của phía đó có hiệu lực.
    public void ResolveLatest_OnlyOneSideUsesThatSide()
    {
        var guestOnly = ConsentResolver.ResolveLatest(new ConsentSnapshot(true, false, Morning), userConsent: null);
        var userOnly = ConsentResolver.ResolveLatest(guestConsent: null, new ConsentSnapshot(false, true, Morning));

        Assert.Equal(new ConsentState(Personalization: true, Marketing: false), guestOnly);
        Assert.Equal(new ConsentState(Personalization: false, Marketing: true), userOnly);
    }

    [Fact]
    // Hai phía cùng có lựa chọn thì bản mới hơn thắng. Bản mới hơn là ý định gần nhất của người dùng.
    public void ResolveLatest_NewerRecordWins()
    {
        var olderGuest = new ConsentSnapshot(false, false, Morning);
        var newerUser = new ConsentSnapshot(true, true, Noon);
        var newerGuest = new ConsentSnapshot(true, false, Noon.AddHours(2));

        Assert.Equal(new ConsentState(true, true), ConsentResolver.ResolveLatest(olderGuest, newerUser));
        Assert.Equal(new ConsentState(true, false), ConsentResolver.ResolveLatest(newerGuest, newerUser));
    }

    [Fact]
    // Hai bản trùng thời điểm thì phía khách thắng. Đó là lựa chọn vừa đưa ra trên thiết bị đang đăng nhập.
    public void ResolveLatest_TieFavorsGuestSide()
    {
        var guest = new ConsentSnapshot(true, false, Noon);
        var user = new ConsentSnapshot(false, true, Noon);

        Assert.Equal(new ConsentState(true, false), ConsentResolver.ResolveLatest(guest, user));
    }
}
