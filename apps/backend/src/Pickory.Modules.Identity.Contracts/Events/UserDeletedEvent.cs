using Pickory.Shared.Events;

namespace Pickory.Modules.Identity.Contracts.Events;

// Phát sau khi khách tự xóa tài khoản. Module khác xóa dữ liệu riêng của người dùng đó.
public sealed record UserDeletedEvent(string UserId) : IDomainEvent;
