namespace Pickory.Modules.Identity.Features.UpdateProfile;

// Trường không gửi nghĩa là giữ nguyên, còn trường gửi `null` nghĩa là xóa giá trị. Kiểu này giữ được khác biệt đó.
public readonly record struct ProfileFieldUpdate(bool IsProvided, string? Value)
{
    public static ProfileFieldUpdate Missing { get; } = new(IsProvided: false, Value: null);
}
