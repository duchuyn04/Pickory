namespace Pickory.Modules.Identity.Contracts.Staff;

// Đọc tên hiển thị của nhân viên theo mã người dùng cho các module ghi lịch sử thao tác.
public interface IStaffDirectory
{
    Task<IReadOnlyDictionary<string, string>> GetNamesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken);

    // Đọc tên trong hồ sơ nhân viên, trả null khi hồ sơ không có tên.
    Task<IReadOnlyDictionary<string, string?>> GetProfileNamesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken);
}
