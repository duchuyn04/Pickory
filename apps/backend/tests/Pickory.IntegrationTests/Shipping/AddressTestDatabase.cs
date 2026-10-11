using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.IntegrationTests.Shipping;

// Dọn và dựng sẵn danh mục địa chỉ cho test. Các class dùng chung DB test chạy lần lượt trong một collection, nên xóa cả bảng là an toàn.
internal static class AddressTestDatabase
{
    public static async Task ClearAsync(ShippingDbContext context)
    {
        // Xóa phường trước vì khóa ngoại chặn xóa tỉnh còn phường.
        await context.AddressWards.ExecuteDeleteAsync();
        await context.AddressProvinces.ExecuteDeleteAsync();
    }

    public static AddressProvince Province(string code, string name, bool isActive, DateTime syncedAt) =>
        new() { Code = code, Name = name, IsActive = isActive, SyncedAt = syncedAt };

    public static AddressWard Ward(string code, string provinceCode, string name, bool isActive, DateTime syncedAt) =>
        new() { Code = code, ProvinceCode = provinceCode, Name = name, IsActive = isActive, SyncedAt = syncedAt };
}
