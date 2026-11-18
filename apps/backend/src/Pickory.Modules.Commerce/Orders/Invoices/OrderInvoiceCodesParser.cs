using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Pickory.Modules.Commerce.Orders.Invoices;

// Tách và kiểm tra danh sách mã đơn từ tham số `codes` trước khi truy vấn DB.
internal static class OrderInvoiceCodesParser
{
    public const string CodesParameter = "codes";
    public const int MaxCodes = 50;
    public const int MaxRawLength = 4096;

    public static IReadOnlyList<string> Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        // Chuyển tham số thiếu trên URL sang cùng bước kiểm tra rỗng của `codes`.
        if (!query.TryGetValue(CodesParameter, out var values))
        {
            return Parse(null, failures);
        }

        // Tham số lặp lại gây mơ hồ thứ tự và tập đơn cần in.
        if (values.Count > 1)
        {
            failures.Add(new ValidationFailure(CodesParameter, $"Tham số `{CodesParameter}` chỉ được gửi một lần."));
            // Dừng sớm khi tham số lặp để không chạy tiếp bước tách và kiểm tra mã.
            return [];
        }

        return Parse(values.ToString(), failures);
    }

    public static IReadOnlyList<string> Parse(string? raw, List<ValidationFailure> failures)
    {
        // Chặn tham số rỗng trước khi tách chuỗi để trả đúng thông báo chọn đơn.
        if (string.IsNullOrWhiteSpace(raw))
        {
            failures.Add(new ValidationFailure(CodesParameter, "Chọn ít nhất 1 đơn để in."));
            return [];
        }

        // Giới hạn độ dài thô ngăn chuỗi quá lớn làm tốn bộ nhớ khi tách dấu phẩy.
        if (raw.Length > MaxRawLength)
        {
            failures.Add(new ValidationFailure(CodesParameter, "Danh sách mã đơn vượt quá độ dài cho phép."));
            return [];
        }

        // Bỏ khoảng trắng hai đầu, bỏ phần tử rỗng và giữ lần xuất hiện đầu của mỗi mã.
        var uniqueCodes = new List<string>();
        // So sánh phân biệt hoa thường vì mã đơn hợp lệ luôn dùng chữ in hoa.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(segment))
            {
                uniqueCodes.Add(segment);
            }
        }

        // Chuỗi chỉ gồm dấu phẩy và khoảng trắng sau khi tách sẽ không còn mã nào.
        if (uniqueCodes.Count == 0)
        {
            failures.Add(new ValidationFailure(CodesParameter, "Chọn ít nhất 1 đơn để in."));
            return [];
        }

        // Đếm giới hạn sau khi bỏ trùng để mã lặp không chiếm suất của đơn khác.
        if (uniqueCodes.Count > MaxCodes)
        {
            failures.Add(new ValidationFailure(CodesParameter, $"Mỗi lần in tối đa {MaxCodes} đơn."));
            return [];
        }

        // Kiểm tra định dạng mọi mã trước khi chạm DB để chặn mã sai và nêu đủ lỗi.
        var initialFailureCount = failures.Count;
        foreach (var code in uniqueCodes)
        {
            if (!IsValidOrderCode(code))
            {
                failures.Add(new ValidationFailure(CodesParameter, $"Mã đơn không hợp lệ: {code}."));
            }
        }

        // Trả danh sách rỗng nếu phát hiện thêm lỗi để bên gọi không dùng dữ liệu hỏng.
        return failures.Count == initialFailureCount ? uniqueCodes : [];
    }

    private static bool IsValidOrderCode(string code)
    {
        // Độ dài và tiền tố phải khớp quy ước sinh mã đơn, phân biệt chữ hoa và thường.
        if (code.Length != OrderCodeGenerator.CodeLength
            || !code.StartsWith(OrderCodeGenerator.Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Mọi ký tự phần ngẫu nhiên phải nằm trong bảng chữ cái của `OrderCodeGenerator`.
        for (var index = OrderCodeGenerator.Prefix.Length; index < code.Length; index++)
        {
            if (!OrderCodeGenerator.Alphabet.Contains(code[index]))
            {
                return false;
            }
        }

        return true;
    }
}
