namespace Pickory.Modules.Identity.Contracts.Actors;

// Loại người gửi request. Module khác dựa vào loại này để chọn cách kiểm tra quyền.
public enum ActorKind
{
    Guest,
    Customer,
    Staff,
}
