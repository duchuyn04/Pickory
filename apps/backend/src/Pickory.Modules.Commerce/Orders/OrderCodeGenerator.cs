using System.Security.Cryptography;

namespace Pickory.Modules.Commerce.Orders;

// Sinh mã đơn dạng `PK` cộng 8 ký tự. Mã này là nội dung chuyển khoản và `Ref` của vận đơn, nên khách phải đọc và gõ lại được.
public static class OrderCodeGenerator
{
    public const string Prefix = "PK";

    public const int RandomPartLength = 8;

    // Mã dài 10 ký tự: tiền tố 2 ký tự cộng phần ngẫu nhiên 8 ký tự.
    public const int CodeLength = 10;

    // Bảng chữ gồm `A-Z` bỏ `O` và `I`, cộng `2-9`. Bảng bỏ `0` và `1` vì dễ nhầm với `O` và `I` khi khách gõ tay.
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // Nguồn ngẫu nhiên do bên gọi truyền vào để test sinh lại được cùng một mã.
    // `nextIndex(upperBound)` phải trả số nguyên trong khoảng từ 0 đến `upperBound - 1`.
    public static string Generate(Func<int, int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(nextIndex);

        var randomPart = new char[RandomPartLength];
        for (var position = 0; position < randomPart.Length; position++)
        {
            var index = nextIndex(Alphabet.Length);

            // Chỉ số ngoài bảng chữ làm mã chứa ký tự lạ hoặc làm lỗi mảng. Nguồn ngẫu nhiên hỏng thì dừng ngay.
            if (index < 0 || index >= Alphabet.Length)
            {
                throw new InvalidOperationException(
                    $"Nguồn ngẫu nhiên trả chỉ số {index}, ngoài khoảng từ 0 đến {Alphabet.Length - 1}.");
            }

            randomPart[position] = Alphabet[index];
        }

        return Prefix + new string(randomPart);
    }

    // Mã dùng trong production lấy ngẫu nhiên từ bộ sinh số mật mã, để kẻ tấn công không đoán được mã đơn kế tiếp.
    public static string Generate() => Generate(RandomNumberGenerator.GetInt32);
}
