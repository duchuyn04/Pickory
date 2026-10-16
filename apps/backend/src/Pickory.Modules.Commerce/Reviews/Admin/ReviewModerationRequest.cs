using System.Text.Json;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Reviews.Admin;

// Body của `PATCH /api/admin/reviews/{id}`, trong đó `Hidden` là trạng thái muốn đặt và `Reason` là mã trong danh sách cố định.
// Binder mặc định nhận kiểu sai và bỏ qua trường lạ, nên lớp này đọc JSON thủ công.
internal sealed record ReviewModerationRequest(bool Hidden, string Reason)
{
    public const string HiddenField = "hidden";
    public const string ReasonField = "reason";

    // Mã lý do ẩn. Mỗi mã nêu một nhóm vi phạm để thống kê kiểm duyệt.
    public static readonly IReadOnlyList<string> HideReasons = ["spam", "offensive", "personal_info", "off_topic", "misleading"];

    // Mã lý do hiện lại. Audit giữ mã này, vì `HiddenReason` về `null` khi hiện.
    public static readonly IReadOnlyList<string> ShowReasons = ["report_rejected", "hidden_by_mistake"];

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string HiddenMessage = "`hidden` là bắt buộc và chỉ nhận `true` hoặc `false`.";
    private const string ReasonRequiredMessage = "`reason` là bắt buộc.";
    private const string ReasonNotAllowedMessage = "Mã lý do không thuộc danh sách của hành động này.";

    // Thêm mọi lỗi vào `failures`. Nơi gọi phải dừng khi `failures` không rỗng.
    public static ReviewModerationRequest Read(JsonElement? body, List<ValidationFailure> failures)
    {
        var empty = new ReviewModerationRequest(false, string.Empty);

        // Thiếu body hoặc body không phải đối tượng thì không có trường nào để đọc tiếp.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
            return empty;
        }

        // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            if (property.Name != HiddenField && property.Name != ReasonField)
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        // Chỉ nhận kiểu boolean của JSON. Chuỗi `"true"` và số `1` không phải ý rõ ràng.
        if (!json.TryGetProperty(HiddenField, out var hiddenElement)
            || hiddenElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            failures.Add(new ValidationFailure(HiddenField, HiddenMessage));
            return empty;
        }

        var hidden = hiddenElement.GetBoolean();

        // Lý do bắt buộc cho cả hai hành động. Lý do hiện lại chỉ còn trong audit nên không được thiếu.
        if (!json.TryGetProperty(ReasonField, out var reasonElement)
            || reasonElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(reasonElement.GetString()))
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonRequiredMessage));
            return new ReviewModerationRequest(hidden, string.Empty);
        }

        // So khớp đúng chữ, không cắt khoảng trắng và không đổi hoa thường, để mã trong audit luôn thuộc danh sách.
        var reason = reasonElement.GetString()!;
        var allowedReasons = hidden ? HideReasons : ShowReasons;
        if (!allowedReasons.Contains(reason, StringComparer.Ordinal))
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonNotAllowedMessage));
        }

        return new ReviewModerationRequest(hidden, reason);
    }
}
