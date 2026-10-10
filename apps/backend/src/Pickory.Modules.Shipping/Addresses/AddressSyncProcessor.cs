using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.Addresses;

// Bộ đồng bộ tỉnh/thành và phường/xã từ GHN vào DB. Đọc hết GHN trước, rồi mới ghi DB một lần.
internal sealed class AddressSyncProcessor(
    ShippingDbContext database,
    IGhnClient ghnClient,
    TimeProvider timeProvider,
    ILogger<AddressSyncProcessor> logger) : IAddressSyncProcessor
{
    // GHN trả tối đa 200 phường/xã mỗi trang. Trang ít hơn 200 dòng là trang cuối.
    internal const int WardPageSize = 200;

    // Trần số trang phường/xã của một tỉnh/thành: 50 trang là 10.000 dòng, dư nhiều so với dữ liệu thật.
    // Trần chặn vòng đọc không dừng khi GHN bỏ qua `offset` và trả mãi một trang đủ 200 dòng.
    internal const int MaxWardPagesPerProvince = 50;

    // Đơn vị GHN đang dùng có `status` bằng 1. Mọi giá trị khác là đã tắt hoặc đã xóa.
    private const int GhnActiveStatus = 1;

    private sealed record ProvinceWithWards(GhnProvince Province, IReadOnlyList<GhnWard> Wards);

    // GHN vẫn trả đủ 200 dòng ở trang cuối cùng được phép đọc. Dữ liệu của tỉnh/thành này không tin được.
    private sealed class WardPageLimitExceededException(string provinceCode) : Exception
    {
        public string ProvinceCode { get; } = provinceCode;
    }

    public async Task<AddressSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProvinceWithWards> snapshot;
        try
        {
            // Đọc hết dữ liệu GHN trước khi ghi. GHN lỗi giữa chừng thì DB không bị nửa cũ nửa mới.
            snapshot = await ReadSnapshotAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is ShippingUnavailableException or ShippingRejectedException)
        {
            // Chưa ghi gì vào DB nên danh mục cũ còn nguyên. Ghi log mức lỗi để người vận hành biết lần đồng bộ này hỏng.
            logger.LogError(exception, "Đồng bộ danh mục địa chỉ thất bại vì GHN lỗi. Danh mục cũ được giữ nguyên.");
            return AddressSyncResult.Failure(exception.Message);
        }
        catch (WardPageLimitExceededException exception)
        {
            // Dữ liệu của tỉnh/thành này không dừng được nên không ghi gì. Danh mục cũ còn nguyên và log nêu mã tỉnh/thành để người vận hành tra.
            logger.LogError(
                "Đồng bộ danh mục địa chỉ thất bại vì GHN trả quá {MaxPages} trang phường/xã cho tỉnh/thành {ProvinceCode}. Danh mục cũ được giữ nguyên.",
                MaxWardPagesPerProvince, exception.ProvinceCode);
            return AddressSyncResult.Failure(
                $"GHN trả quá {MaxWardPagesPerProvince} trang phường/xã cho tỉnh/thành {exception.ProvinceCode}.");
        }

        // Danh sách tỉnh/thành rỗng mà không báo lỗi là dữ liệu bất thường. Nếu ghi, mọi tỉnh bị tắt và khách không chọn được địa chỉ.
        if (snapshot.Count == 0)
        {
            logger.LogError("Đồng bộ danh mục địa chỉ bị bỏ qua vì GHN trả danh sách tỉnh/thành rỗng. Danh mục cũ được giữ nguyên.");
            return AddressSyncResult.Failure("GHN trả danh sách tỉnh/thành rỗng.");
        }

        await WriteSnapshotAsync(snapshot, cancellationToken);

        var wardCount = snapshot.Sum(entry => entry.Wards.Count);
        return AddressSyncResult.Success(snapshot.Count, wardCount);
    }

    private async Task<IReadOnlyList<ProvinceWithWards>> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        var provinces = await ghnClient.GetProvincesAsync(cancellationToken);

        var snapshot = new List<ProvinceWithWards>(provinces.Count);
        foreach (var province in provinces)
        {
            // Phường/xã của tỉnh/thành đã tắt không hiện cho khách, nên không đọc. Lời gọi này còn có thể lỗi với tỉnh/thành đã xóa.
            if (province.Status != GhnActiveStatus)
            {
                snapshot.Add(new ProvinceWithWards(province, []));
                continue;
            }

            var wards = await ReadAllWardsAsync(province, cancellationToken);
            snapshot.Add(new ProvinceWithWards(province, wards));
        }

        return snapshot;
    }

    private async Task<IReadOnlyList<GhnWard>> ReadAllWardsAsync(GhnProvince province, CancellationToken cancellationToken)
    {
        var wards = new List<GhnWard>();
        for (var pageNumber = 0; pageNumber < MaxWardPagesPerProvince; pageNumber++)
        {
            var page = await ghnClient.GetWardsAsync(province.Code, pageNumber * WardPageSize, WardPageSize, cancellationToken);
            wards.AddRange(page);

            // Trang ngắn hơn kích thước trang là trang cuối. Tỉnh có đúng 200 phường/xã cần thêm một lần gọi để thấy trang rỗng.
            if (page.Count < WardPageSize)
            {
                return wards;
            }
        }

        // Đọc tới trần mà trang cuối vẫn đủ 200 dòng. Coi là dữ liệu lỗi, không giữ nửa chừng.
        throw new WardPageLimitExceededException(province.Code);
    }

    private async Task WriteSnapshotAsync(IReadOnlyList<ProvinceWithWards> snapshot, CancellationToken cancellationToken)
    {
        // Mọi dòng của lần đồng bộ này dùng cùng một `syncedAt`, lấy từ `TimeProvider` để test kiểm được giá trị.
        var syncedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Nạp cả bảng vào bộ nhớ. Danh mục chỉ có vài chục tỉnh/thành và vài nghìn phường/xã.
        var provincesByCode = await database.AddressProvinces.ToDictionaryAsync(province => province.Code, cancellationToken);
        var wardsByCode = await database.AddressWards.ToDictionaryAsync(ward => ward.Code, cancellationToken);
        var provinceCodesFromGhn = new HashSet<string>(StringComparer.Ordinal);
        var wardCodesFromGhn = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in snapshot)
        {
            provinceCodesFromGhn.Add(entry.Province.Code);
            UpsertProvince(provincesByCode, entry.Province, syncedAt);

            foreach (var ghnWard in entry.Wards)
            {
                wardCodesFromGhn.Add(ghnWard.Code);
                // Mã tỉnh lấy từ tỉnh đang đọc, không lấy từ `ParentId`. Phường/xã đổi tỉnh thì dòng cũ chuyển sang tỉnh mới.
                UpsertWard(wardsByCode, ghnWard, entry.Province.Code, syncedAt);
            }
        }

        // Dòng có trong DB mà GHN không trả nữa thì tắt thay vì xóa. Đơn cũ có thể còn trỏ tới mã này.
        foreach (var province in provincesByCode.Values.Where(province => !provinceCodesFromGhn.Contains(province.Code)))
        {
            // Giữ nguyên `SyncedAt` để biết lần cuối GHN còn trả dòng này.
            province.IsActive = false;
        }

        foreach (var ward in wardsByCode.Values.Where(ward => !wardCodesFromGhn.Contains(ward.Code)))
        {
            // Giữ nguyên `SyncedAt` vì lý do như với tỉnh/thành.
            ward.IsActive = false;
        }

        // Một lần lưu là một transaction. Lỗi DB thì không dòng nào đổi.
        await database.SaveChangesAsync(cancellationToken);
    }

    private void UpsertProvince(Dictionary<string, AddressProvince> provincesByCode, GhnProvince ghnProvince, DateTime syncedAt)
    {
        // Khóa là mã của GHN, nên chạy lại cập nhật dòng cũ và không tạo dòng trùng.
        if (!provincesByCode.TryGetValue(ghnProvince.Code, out var province))
        {
            province = new AddressProvince { Code = ghnProvince.Code };
            provincesByCode[province.Code] = province;
            database.AddressProvinces.Add(province);
        }

        province.Name = ghnProvince.Name;
        // Chỉ `status` bằng 1 mới hiện cho khách.
        province.IsActive = ghnProvince.Status == GhnActiveStatus;
        province.SyncedAt = syncedAt;
    }

    private void UpsertWard(Dictionary<string, AddressWard> wardsByCode, GhnWard ghnWard, string provinceCode, DateTime syncedAt)
    {
        if (!wardsByCode.TryGetValue(ghnWard.Code, out var ward))
        {
            ward = new AddressWard { Code = ghnWard.Code };
            wardsByCode[ward.Code] = ward;
            database.AddressWards.Add(ward);
        }

        ward.ProvinceCode = provinceCode;
        ward.Name = ghnWard.Name;
        ward.IsActive = ghnWard.Status == GhnActiveStatus;
        ward.SyncedAt = syncedAt;
    }
}
