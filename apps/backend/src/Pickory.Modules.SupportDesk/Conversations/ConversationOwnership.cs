using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Conversations;

// Luật chủ hội thoại và luật chặn nhân viên cho ba route của khách.
internal static class ConversationOwnership
{
    private const string StaffForbiddenMessage = "Khung chat hỗ trợ chỉ dành cho khách hàng.";

    // Nhân viên có phiên hợp lệ nhưng không phải khách của hội thoại. Phải kiểm trước mọi bước khác.
    public static void RejectStaff(Actor actor)
    {
        if (actor.Kind == ActorKind.Staff)
        {
            throw new AppException(SupportDeskErrorCodes.Forbidden, 403, StaffForbiddenMessage);
        }
    }

    // Giữ lại hội thoại thuộc về người gọi. Hội thoại của người khác và `id` không tồn tại cho cùng một kết quả rỗng.
    public static IQueryable<SupportConversation> OwnedBy(IQueryable<SupportConversation> conversations, Actor actor)
    {
        if (actor.Kind == ActorKind.Customer)
        {
            // Khách đăng nhập sở hữu theo tài khoản. Đổi trình duyệt hay `pk_anon` không làm mất hội thoại.
            var userId = actor.UserId;
            return conversations.Where(conversation => conversation.CustomerUserId == userId);
        }

        // Khách vãng lai chỉ sở hữu hội thoại chưa gắn tài khoản, vì cookie `pk_anon` không đổi khi chủ đăng xuất.
        // Thiếu điều kiện này, người dùng chung trình duyệt đọc được hội thoại của chủ tài khoản.
        var anonymousId = actor.AnonymousId;
        return conversations.Where(conversation =>
            conversation.CustomerUserId == null && conversation.AnonymousId == anonymousId);
    }
}
