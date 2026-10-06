using System.Text.RegularExpressions;
using FluentValidation;

namespace Pickory.Shared.Validation;

// Kiểm tra email giống `z.string().email()` của Zod 4 ở bản Node. Hai backend nhận cùng tập email hợp lệ.
public static partial class EmailAddressRule
{
    public static IRuleBuilderOptions<T, string?> MustBeEmailAddress<T>(this IRuleBuilder<T, string?> ruleBuilder, string message)
    {
        return ruleBuilder
            .Must(value => value is not null && ZodEmailPattern().IsMatch(value))
            .WithMessage(message);
    }

    // Regex sao chép từ Zod 4. Chuỗi không bị cắt khoảng trắng trước khi kiểm tra, giống bản Node.
    [GeneratedRegex(@"^(?!\.)(?!.*\.\.)([A-Za-z0-9_'+\-\.]*)[A-Za-z0-9_+-]@([A-Za-z0-9][A-Za-z0-9\-]*\.)+[A-Za-z]{2,}$")]
    private static partial Regex ZodEmailPattern();
}
