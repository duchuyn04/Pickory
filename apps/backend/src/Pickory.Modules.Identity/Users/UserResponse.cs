namespace Pickory.Modules.Identity.Users;

// Bản Node bọc hồ sơ trong `{ user }` cho route đăng nhập và route sửa hồ sơ.
public sealed record UserResponse<TUser>(TUser User);
