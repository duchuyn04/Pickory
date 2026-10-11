using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Shared.Http;

namespace Pickory.Modules.Shipping.Addresses;

// Route công khai của danh mục địa chỉ. Khách vãng lai gọi được, không cần đăng nhập hay quyền back-office.
internal static class AddressEndpoints
{
    private const string ProvinceCodeParameter = "provinceCode";

    // So sánh theo tiếng Việt, để "Đà Nẵng" đứng sau "Cần Thơ" và trước "Hà Nội". Phép so `Ordinal` đặt chữ `Đ` sau `Z`.
    private static readonly StringComparer VietnameseComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: false);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/addresses/provinces", GetProvincesAsync);
        endpoints.MapGet("/api/addresses/wards", GetWardsAsync);
    }

    private static async Task<IResult> GetProvincesAsync(ShippingDbContext database, CancellationToken cancellationToken)
    {
        // Chỉ đọc nên không cần theo dõi entity. Tỉnh/thành đã tắt không hiện cho khách.
        var provinces = await database.AddressProvinces
            .AsNoTracking()
            .Where(province => province.IsActive)
            .Select(province => new AddressItem(province.Code, province.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ApiResponse<IReadOnlyList<AddressItem>>(SortByVietnameseName(provinces)));
    }

    private static async Task<IResult> GetWardsAsync(HttpContext context, ShippingDbContext database, CancellationToken cancellationToken)
    {
        var provinceCode = context.Request.Query[ProvinceCodeParameter].ToString().Trim();

        // Thiếu mã tỉnh/thành thì từ chối. Trả phường/xã của cả nước không giúp khách chọn địa chỉ.
        if (provinceCode.Length == 0)
        {
            throw new ValidationException(
                [new ValidationFailure(ProvinceCodeParameter, "Cần có mã tỉnh/thành để lấy danh sách phường/xã.")]);
        }

        // Phường/xã của tỉnh/thành đã tắt cũng bị ẩn. Mã không có thì kết quả là danh sách rỗng, không phải lỗi.
        var wards = await database.AddressWards
            .AsNoTracking()
            .Where(ward => ward.ProvinceCode == provinceCode && ward.IsActive && ward.Province!.IsActive)
            .Select(ward => new AddressItem(ward.Code, ward.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ApiResponse<IReadOnlyList<AddressItem>>(SortByVietnameseName(wards)));
    }

    // Sắp trong bộ nhớ vì collation mặc định của Postgres không theo thứ tự chữ cái tiếng Việt. Danh sách chỉ vài trăm dòng.
    private static IReadOnlyList<AddressItem> SortByVietnameseName(List<AddressItem> items)
    {
        return items.OrderBy(item => item.Name, VietnameseComparer).ToList();
    }
}

// Một dòng trả cho khách. Không có `isActive` và `syncedAt` vì đó là chi tiết đồng bộ nội bộ.
internal sealed record AddressItem(string Code, string Name);
