using Pickory.Shared.Events;

namespace Pickory.Modules.Identity.Contracts.Events;

// Phát sau khi email của tài khoản khách đổi từ chưa xác thực sang đã xác thực. `Email` là email đã lưu trên tài khoản.
public sealed record EmailVerifiedEvent(string UserId, string Email) : IDomainEvent;
