using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Presence;

// Chuyển trạng thái trực giữa enum trong code và chuỗi trên đường truyền. Chuỗi trùng nhãn enum của DB.
internal static class StaffPresenceWire
{
    public const string Available = "available";
    public const string Away = "away";

    public static bool TryParse(string? text, out StaffPresenceStatus status)
    {
        // So sánh đúng từng ký tự. Giá trị như `Available` hay ` away` bị từ chối để client gửi đúng một dạng.
        switch (text)
        {
            case Available:
                status = StaffPresenceStatus.Available;
                return true;
            case Away:
                status = StaffPresenceStatus.Away;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static string ToWire(StaffPresenceStatus status)
    {
        // Enum có thêm giá trị mới mà quên cập nhật hàm này thì phải lỗi rõ ràng, không trả chuỗi sai.
        return status switch
        {
            StaffPresenceStatus.Available => Available,
            StaffPresenceStatus.Away => Away,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Trạng thái trực chưa có tên trên đường truyền."),
        };
    }
}
