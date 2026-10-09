using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Users;

namespace Pickory.Modules.Identity.Features.UpdateProfile;

// Sửa tên và số điện thoại của chính người dùng đang đăng nhập.
public sealed class UpdateProfileHandler(IdentityDbContext database, TimeProvider timeProvider)
{
    public async Task<UserProfileResponse> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        // `userId` lấy từ actor, không lấy từ body. Người dùng không sửa được hồ sơ của người khác.
        var user = await database.Users.SingleAsync(value => value.Id == userId, cancellationToken);

        // Trường được gửi thì cập nhật. Chuỗi trống hoặc `null` xóa giá trị, giống bản Node.
        if (request.Name.IsProvided)
        {
            user.Name = UserInputNormalizer.TrimToNull(request.Name.Value);
        }

        if (request.Phone.IsProvided)
        {
            // `FromJson` đã kiểm định dạng. Lưu về dạng `0xxxxxxxxx`, còn chuỗi trống thì xóa số.
            user.Phone = ProfileFieldRules.NormalizePhone(request.Phone.Value);
        }

        // Prisma tự cập nhật `updatedAt` mỗi khi sửa user. Bản .NET ghi cùng cột để dữ liệu không lệch.
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);

        // Response chỉ chứa trường an toàn. Hash mật khẩu không bao giờ rời máy chủ.
        var kind = user.Kind == UserKind.Staff ? UserSummaryResponse.StaffKind : UserProfileResponse.CustomerKind;
        return new UserProfileResponse(user.Id, user.Email, user.Name, user.Phone, kind);
    }
}
