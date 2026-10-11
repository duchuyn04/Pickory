namespace Pickory.Modules.Commerce.Invoices;

// Luật mã số thuế của hóa đơn VAT: 10 hoặc 13 chữ số, cho phép dấu gạch ngang ASCII.
public static class TaxCodeRule
{
    private const int EnterpriseTaxCodeLength = 10;
    private const int BranchTaxCodeLength = 13;

    // Hàm không cắt khoảng trắng đầu và cuối. Bên gọi tự cắt trước, để chuỗi lạ không được chấp nhận ngầm.
    public static bool IsValid(string? taxCode)
    {
        if (string.IsNullOrEmpty(taxCode))
        {
            return false;
        }

        var digitCount = 0;
        foreach (var character in taxCode)
        {
            // Dấu gạch chỉ để khách ngăn nhóm số, như `0312345678-001`. Dấu gạch không tính vào độ dài.
            if (character == '-')
            {
                continue;
            }

            // Chỉ nhận chữ số ASCII. `char.IsDigit` nhận cả chữ số của hệ chữ khác, mà phần mềm hóa đơn không đọc được.
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }

            digitCount++;
        }

        return digitCount is EnterpriseTaxCodeLength or BranchTaxCodeLength;
    }
}
