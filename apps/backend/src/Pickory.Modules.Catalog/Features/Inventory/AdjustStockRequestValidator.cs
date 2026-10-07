using FluentValidation;

namespace Pickory.Modules.Catalog.Features.Inventory;

// Kiểm body điều chỉnh tồn kho trước khi mở transaction.
public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public const int MaxReasonLength = 500;

    public AdjustStockRequestValidator()
    {
        // Điều chỉnh 0 cái không đổi tồn kho, nên không có lý do để ghi movement.
        RuleFor(request => request.Delta)
            .NotEqual(0).WithMessage("Số lượng điều chỉnh phải khác 0.");

        // Lý do là bắt buộc để kiểm toán biết vì sao tồn kho đổi. Độ dài đo sau khi bỏ khoảng trắng vì handler lưu bản đã cắt.
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Lý do điều chỉnh là bắt buộc.")
            .Must(reason => reason is null || reason.Trim().Length <= MaxReasonLength)
            .WithMessage($"Lý do không được dài quá {MaxReasonLength} ký tự.");

        // Điều chỉnh tay chỉ dùng `manual` hoặc `return`. Loại của đơn hàng do `IStockService` ghi.
        RuleFor(request => request.Type)
            .Must(type => type is AdjustStockTypes.Manual or AdjustStockTypes.Return)
            .WithMessage("Loại điều chỉnh phải là manual hoặc return.")
            .When(request => request.Type is not null);

        // Trả hàng là nhập kho lại, nên số lượng phải dương.
        RuleFor(request => request.Delta)
            .GreaterThan(0).WithMessage("Trả hàng phải có số lượng lớn hơn 0.")
            .When(request => request.Type == AdjustStockTypes.Return);
    }
}
