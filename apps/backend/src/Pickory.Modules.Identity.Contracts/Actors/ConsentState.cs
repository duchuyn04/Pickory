namespace Pickory.Modules.Identity.Contracts.Actors;

// Lựa chọn đồng ý thu thập dữ liệu. Cả hai lựa chọn mặc định tắt cho tới khi người dùng chọn.
public sealed record ConsentState(bool Personalization, bool Marketing)
{
    public static ConsentState AllDisabled { get; } = new(Personalization: false, Marketing: false);
}
