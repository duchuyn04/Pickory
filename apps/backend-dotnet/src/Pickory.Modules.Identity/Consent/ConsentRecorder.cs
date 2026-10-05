using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Consent;

// Thêm bản ghi consent mới. Hàm luôn thêm dòng mới, không sửa dòng cũ.
public static class ConsentRecorder
{
    // Đổi giá trị này mỗi khi chính sách quyền riêng tư đổi. Mỗi lựa chọn ghi rõ theo bản chính sách nào.
    public const string CurrentPolicyVersion = "2026-10-01";

    public static ConsentRecord Add(
        IdentityDbContext database,
        string anonymousId,
        string? userId,
        bool personalization,
        bool marketing,
        string policyVersion = CurrentPolicyVersion)
    {
        // Dòng cũ được giữ làm lịch sử. Lịch sử chứng minh người dùng đã chọn gì và vào lúc nào.
        var record = new ConsentRecord
        {
            AnonymousId = anonymousId,
            UserId = userId,
            Personalization = personalization,
            Marketing = marketing,
            PolicyVersion = policyVersion,
        };
        database.ConsentRecords.Add(record);
        return record;
    }
}
