namespace Pickory.Modules.Identity.Setup;

// Email, mật khẩu và tên của admin đầu tiên. Giá trị lấy từ tham số dòng lệnh hoặc biến môi trường.
public sealed record AdminOptions(string Email, string Password, string Name);
