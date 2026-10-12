using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.Addresses;

// Tra danh mục địa chỉ cho module khác. Vòng đời scoped vì lớp giữ `ShippingDbContext` của request.
internal sealed class AddressDirectory(ShippingDbContext database) : IAddressDirectory
{
    public async Task<AddressNames?> FindAsync(string provinceCode, string wardCode, CancellationToken cancellationToken)
    {
        // Điều kiện `ProvinceCode` loại phường không thuộc tỉnh. Hai cờ `IsActive` loại đơn vị đã tắt sau lần đồng bộ.
        return await database.AddressWards
            .AsNoTracking()
            .Where(ward => ward.Code == wardCode
                && ward.ProvinceCode == provinceCode
                && ward.IsActive
                && ward.Province!.IsActive)
            .Select(ward => new AddressNames(ward.Province!.Name, ward.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
