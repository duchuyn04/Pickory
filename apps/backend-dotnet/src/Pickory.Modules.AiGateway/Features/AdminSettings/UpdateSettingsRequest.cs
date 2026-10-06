using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.AiGateway.Settings;

namespace Pickory.Modules.AiGateway.Features.AdminSettings;

// Đọc body của `PUT /api/admin/ai/settings` theo luật `strict` của bản Node.
// Binder mặc định nhận số dạng chuỗi và bỏ qua trường lạ, nên lớp này đọc JSON thủ công.
public static class UpdateSettingsRequest
{
    private static readonly string[] KnownFields = ["monthlyBudgetUsd", "chatDailyLimitUser", "chatDailyLimitAnon"];

    public static AiSettings FromJson(JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            throw new ValidationException([new ValidationFailure(string.Empty, "Dữ liệu gửi lên phải là một đối tượng JSON.")]);
        }

        var failures = new List<ValidationFailure>();
        // Trường lạ bị từ chối. Cấu hình không nhận dữ liệu ngoài ý muốn.
        foreach (var property in json.EnumerateObject())
        {
            if (!KnownFields.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, "Trường này không được hỗ trợ."));
            }
        }

        var budget = ReadBudget(json, failures);
        var userLimit = ReadPositiveInteger(json, "chatDailyLimitUser", "Hạn mức chat ngày của người dùng phải là số nguyên dương.", failures);
        var anonLimit = ReadPositiveInteger(json, "chatDailyLimitAnon", "Hạn mức chat ngày của khách vãng lai phải là số nguyên dương.", failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new AiSettings(budget, userLimit, anonLimit);
    }

    private static double ReadBudget(JsonElement json, List<ValidationFailure> failures)
    {
        // Ngân sách phải là số JSON. Chuỗi `"30"` bị từ chối, giống `z.number()`.
        if (!json.TryGetProperty("monthlyBudgetUsd", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            failures.Add(new ValidationFailure("monthlyBudgetUsd", "monthlyBudgetUsd phải là số."));
            return 0;
        }

        // Số quá lớn không đọc được thành `double` hữu hạn. Giá trị đó bị từ chối như ngân sách sai.
        if (!value.TryGetDouble(out var budget) || !double.IsFinite(budget) || budget < 0)
        {
            failures.Add(new ValidationFailure("monthlyBudgetUsd", "Ngân sách tháng không được là số âm."));
        }

        return budget;
    }

    private static int ReadPositiveInteger(JsonElement json, string name, string rangeMessage, List<ValidationFailure> failures)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            failures.Add(new ValidationFailure(name, $"{name} phải là số."));
            return 0;
        }

        // Số lẻ như 25.5 không phải số lượt chat. `TryGetInt32` từ chối số có phần thập phân.
        if (!value.TryGetInt32(out var limit))
        {
            failures.Add(new ValidationFailure(name, $"{name} phải là số nguyên."));
            return 0;
        }

        if (limit < 1)
        {
            failures.Add(new ValidationFailure(name, rangeMessage));
        }

        return limit;
    }
}
