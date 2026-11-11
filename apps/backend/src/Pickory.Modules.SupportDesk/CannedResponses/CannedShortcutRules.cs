namespace Pickory.Modules.SupportDesk.CannedResponses;

// Luật của `shortcut` và `body` mẫu trả lời nhanh. Hàm thuần, nên unit test không cần DB.
internal static class CannedShortcutRules
{
    // Giới hạn đã duyệt ngày 2026-11-04 trong mục "Giới hạn" của hợp đồng: phím tắt dài 2 đến 30 ký tự.
    public const int MinShortcutLength = 2;
    public const int MaxShortcutLength = 30;
    public const int MaxBodyLength = 2000;

    // Chỉ nhận `a-z`, `0-9`, `-`, `_`. Server không đổi chữ hoa thành chữ thường, nên chữ hoa là sai.
    public static bool IsValidShortcut(string? shortcut)
    {
        if (string.IsNullOrEmpty(shortcut) || shortcut.Length < MinShortcutLength || shortcut.Length > MaxShortcutLength)
        {
            return false;
        }

        // Kiểm từng ký tự thay cho regex, vì `$` của regex chấp nhận thêm một ký tự xuống dòng ở cuối.
        foreach (var character in shortcut)
        {
            var isAllowed = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    // Độ dài tính sau khi cắt khoảng trắng hai đầu. Thân chỉ có khoảng trắng coi như rỗng.
    public static bool IsValidBody(string? body)
    {
        var length = body?.Trim().Length ?? 0;
        return length is >= 1 and <= MaxBodyLength;
    }
}
