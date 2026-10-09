using Pickory.Modules.Identity.Contracts.Actors;

namespace Pickory.Modules.Identity.Consent;

// Chọn lựa chọn consent có hiệu lực giữa phía khách vãng lai và phía tài khoản.
public static class ConsentResolver
{
    public static ConsentState ResolveLatest(ConsentSnapshot? guestConsent, ConsentSnapshot? userConsent)
    {
        // Chưa ai chọn thì mọi lựa chọn tắt. Hệ thống chỉ thu thập dữ liệu khi người dùng đồng ý chủ động.
        if (guestConsent is null && userConsent is null)
        {
            return ConsentState.AllDisabled;
        }

        // Chỉ một phía từng chọn thì lấy phía đó.
        if (guestConsent is null)
        {
            return ToState(userConsent!);
        }

        if (userConsent is null)
        {
            return ToState(guestConsent);
        }

        // Bản mới hơn là ý định gần nhất của người dùng. Hai bản trùng thời điểm thì phía khách thắng.
        return guestConsent.CreatedAt >= userConsent.CreatedAt ? ToState(guestConsent) : ToState(userConsent);
    }

    private static ConsentState ToState(ConsentSnapshot snapshot)
    {
        return new ConsentState(snapshot.Personalization, snapshot.Marketing);
    }
}
