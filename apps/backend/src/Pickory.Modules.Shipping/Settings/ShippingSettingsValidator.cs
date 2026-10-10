using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Features.AdminSettings;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.Settings;

// Kiểm body của PUT cấu hình: luật từng trường, rồi đối chiếu mã với danh mục địa chỉ đã đồng bộ.
internal sealed class ShippingSettingsValidator(ShippingDbContext database)
{
    // Ném `ValidationException` gom mọi lỗi của body. Trả giá trị đã kiểm kèm tên tỉnh và phường lấy từ danh mục.
    public async Task<ValidatedShippingSettings> ValidateAsync(JsonElement? body, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var input = UpdateShippingSettingsRequest.Read(body, failures);

        // Lỗi cả body (không phải đối tượng JSON) không có mã nào để tra danh mục. Trả đúng một lỗi, không thêm lỗi thiếu mã.
        if (failures.Any(failure => string.IsNullOrEmpty(failure.PropertyName)))
        {
            throw new ValidationException(failures);
        }

        var provinceName = await FindProvinceNameAsync(input, failures, cancellationToken);
        var wardName = await FindWardNameAsync(input, failures, cancellationToken);

        // Gom lỗi đọc body và lỗi danh mục vào một phản hồi, để UI hiện đủ lỗi dưới từng ô.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new ValidatedShippingSettings(input, provinceName!, wardName!);
    }

    private async Task<string?> FindProvinceNameAsync(
        ShippingSettingsInput input, List<ValidationFailure> failures, CancellationToken cancellationToken)
    {
        // Trường đã có lỗi đọc body thì không tra danh mục, vì mã rỗng không đại diện cho tỉnh nào.
        if (HasFailure(failures, UpdateShippingSettingsRequest.ProvinceCodeField))
        {
            return null;
        }

        // Tỉnh/thành đã tắt không còn trong danh mục của GHN, nên không dùng làm nơi lấy hàng.
        var provinceName = await database.AddressProvinces
            .AsNoTracking()
            .Where(province => province.Code == input.ProvinceCode && province.IsActive)
            .Select(province => province.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (provinceName is null)
        {
            failures.Add(new ValidationFailure(
                UpdateShippingSettingsRequest.ProvinceCodeField, "Tỉnh/thành không có trong danh mục hoặc đã ngừng dùng."));
        }

        return provinceName;
    }

    private async Task<string?> FindWardNameAsync(
        ShippingSettingsInput input, List<ValidationFailure> failures, CancellationToken cancellationToken)
    {
        if (HasFailure(failures, UpdateShippingSettingsRequest.WardCodeField))
        {
            return null;
        }

        // Phường/xã đã tắt không dùng được, giống tỉnh/thành đã tắt.
        var ward = await database.AddressWards
            .AsNoTracking()
            .Where(candidate => candidate.Code == input.WardCode && candidate.IsActive)
            .Select(candidate => new { candidate.Name, candidate.ProvinceCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (ward is null)
        {
            failures.Add(new ValidationFailure(
                UpdateShippingSettingsRequest.WardCodeField, "Phường/xã không có trong danh mục hoặc đã ngừng dùng."));
            return null;
        }

        // Phường phải thuộc đúng tỉnh đã chọn. Nếu không, GHN nhận địa chỉ lấy hàng sai vùng.
        if (!string.Equals(ward.ProvinceCode, input.ProvinceCode, StringComparison.Ordinal))
        {
            failures.Add(new ValidationFailure(
                UpdateShippingSettingsRequest.WardCodeField, "Phường/xã không thuộc tỉnh/thành đã chọn."));
            return null;
        }

        return ward.Name;
    }

    private static bool HasFailure(List<ValidationFailure> failures, string field) =>
        failures.Any(failure => string.Equals(failure.PropertyName, field, StringComparison.Ordinal));
}
