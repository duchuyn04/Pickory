namespace Pickory.Modules.Identity.Contracts.Staff;

// Đọc tên hiển thị của nhân viên theo mã người dùng cho các module ghi lịch sử thao tác.
public interface IStaffDirectory
{
    Task<IReadOnlyDictionary<string, string>> GetNamesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken);
}
