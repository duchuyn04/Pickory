using FluentValidation;

namespace Pickory.Modules.Identity.Users;

// Nối `ProfileFieldRules` vào validator của FluentValidation. Đăng ký và mời nhân viên dùng chung hai hàm này.
public static class ProfileFieldValidationExtensions
{
    public static IRuleBuilderOptionsConditions<TRequest, string?> AddProfileNameRule<TRequest>(
        this IRuleBuilder<TRequest, string?> ruleBuilder)
    {
        return ruleBuilder.Custom((name, context) =>
        {
            // Hàm luật trả thông báo thay vì ném lỗi. Validator gom lỗi của mọi trường vào một phản hồi.
            var error = ProfileFieldRules.GetNameError(name);
            if (error is not null)
            {
                context.AddFailure(error);
            }
        });
    }

    public static IRuleBuilderOptionsConditions<TRequest, string?> AddProfilePhoneRule<TRequest>(
        this IRuleBuilder<TRequest, string?> ruleBuilder)
    {
        return ruleBuilder.Custom((phone, context) =>
        {
            var error = ProfileFieldRules.GetPhoneError(phone);
            if (error is not null)
            {
                context.AddFailure(error);
            }
        });
    }
}
