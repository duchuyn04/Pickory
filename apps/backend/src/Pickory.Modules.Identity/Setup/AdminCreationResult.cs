namespace Pickory.Modules.Identity.Setup;

// Lệnh tạo admin chạy lại được nhiều lần. Email nhân viên đã có thì lệnh báo đã tồn tại thay vì báo lỗi.
public enum AdminCreationResult
{
    Created,
    AlreadyExists,
}
