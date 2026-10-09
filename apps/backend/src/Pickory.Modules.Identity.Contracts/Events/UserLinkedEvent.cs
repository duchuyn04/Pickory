using Pickory.Shared.Events;

namespace Pickory.Modules.Identity.Contracts.Events;

// Phát sau khi khách đăng ký hoặc đăng nhập. Module khác gộp dữ liệu của mã ẩn danh vào tài khoản, ví dụ giỏ hàng.
public sealed record UserLinkedEvent(string AnonymousId, string UserId) : IDomainEvent;
