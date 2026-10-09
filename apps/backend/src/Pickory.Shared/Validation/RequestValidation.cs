using FluentValidation;
using FluentValidation.Results;

namespace Pickory.Shared.Validation;

// Một chỗ chạy validator cho body của route. Lỗi đi qua middleware lỗi và có dạng `VALIDATION_ERROR` như bản Node.
public static class RequestValidation
{
    public const string RequiredMessage = "Trường này là bắt buộc.";

    public static async Task<TRequest> ValidateAsync<TRequest>(
        IValidator<TRequest> validator,
        TRequest? request,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        // Body rỗng hoặc `null` không có trường nào để kiểm tra. Lỗi gắn với đường dẫn `body`.
        if (request is null)
        {
            throw new ValidationException([new ValidationFailure(string.Empty, "Dữ liệu gửi lên phải là một đối tượng JSON.")]);
        }

        await validator.ValidateAndThrowAsync(request, cancellationToken);
        return request;
    }
}
