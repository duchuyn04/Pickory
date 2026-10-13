using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Addresses;

// Sổ địa chỉ của khách đã đăng nhập. Mỗi thao tác ghi chạy trong một transaction và giữ khóa theo khách.
internal sealed class SavedAddressService(CommerceDbContext database, IAddressDirectory addressDirectory)
{
    public const int MaxAddressesPerUser = 10;
    public const string LimitReachedCode = "ADDRESS_LIMIT_REACHED";
    public const string NotFoundCode = "ADDRESS_NOT_FOUND";

    private const string LimitReachedMessage = "Sổ địa chỉ đã đủ 10 địa chỉ. Hãy xóa bớt một địa chỉ trước khi thêm địa chỉ mới.";
    private const string NotFoundMessage = "Không tìm thấy địa chỉ.";

    public async Task<IReadOnlyList<SavedAddressResponse>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        // Địa chỉ mặc định lên đầu để UI điền sẵn vào checkout. Hai cột sau chốt thứ tự cho các địa chỉ còn lại.
        var rows = await database.SavedAddresses
            .AsNoTracking()
            .Where(address => address.UserId == userId)
            .OrderByDescending(address => address.IsDefault)
            .ThenBy(address => address.Label)
            .ThenBy(address => address.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<SavedAddressResponse> CreateAsync(
        string userId, SavedAddressInput input, CancellationToken cancellationToken)
    {
        // Tra danh mục trước khi mở transaction. Lời gọi sang module khác không nên giữ khóa của khách.
        var addressJson = await BuildAddressJsonAsync(input.Address, cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAddressesAsync(userId, cancellationToken);

        // Đếm sau khi giữ khóa. Hai request đồng thời không cùng thấy 9 địa chỉ và cùng được thêm.
        var count = await database.SavedAddresses.CountAsync(address => address.UserId == userId, cancellationToken);
        if (count >= MaxAddressesPerUser)
        {
            throw new AppException(LimitReachedCode, 422, LimitReachedMessage);
        }

        // Địa chỉ đầu tiên tự là mặc định, vì checkout cần một địa chỉ để điền sẵn.
        var isDefault = input.IsDefault || count == 0;
        if (isDefault)
        {
            await ClearDefaultAsync(userId, exceptId: null, cancellationToken);
        }

        var address = new SavedAddress
        {
            UserId = userId,
            Label = input.Label,
            Name = input.Name,
            Phone = input.Phone,
            Address = addressJson,
            IsDefault = isDefault,
        };
        database.SavedAddresses.Add(address);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(address);
    }

    public async Task<SavedAddressResponse> UpdateAsync(
        string userId, string id, SavedAddressPatch patch, CancellationToken cancellationToken)
    {
        // Mã địa chỉ chỉ cần tra khi client gửi địa chỉ mới. Tra trước khi mở transaction vì cùng lý do với lúc tạo.
        var addressJson = patch.Address is null ? null : await BuildAddressJsonAsync(patch.Address, cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAddressesAsync(userId, cancellationToken);

        // Điều kiện `UserId` nằm trong truy vấn. Địa chỉ của khách khác và `id` không tồn tại cùng trả 404.
        var address = await database.SavedAddresses
            .SingleOrDefaultAsync(row => row.Id == id && row.UserId == userId, cancellationToken)
            ?? throw new AppException(NotFoundCode, 404, NotFoundMessage);

        if (patch.Label is not null)
        {
            address.Label = patch.Label;
        }

        if (patch.Name is not null)
        {
            address.Name = patch.Name;
        }

        if (patch.Phone is not null)
        {
            address.Phone = patch.Phone;
        }

        if (addressJson is not null)
        {
            address.Address = addressJson;
        }

        if (patch.IsDefault is { } isDefault)
        {
            // Chọn mặc định mới thì bỏ cờ của địa chỉ cũ. Bỏ cờ thì không tự chọn địa chỉ khác.
            if (isDefault)
            {
                await ClearDefaultAsync(userId, exceptId: address.Id, cancellationToken);
            }

            address.IsDefault = isDefault;
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(address);
    }

    public async Task DeleteAsync(string userId, string id, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAddressesAsync(userId, cancellationToken);

        // Xóa theo cả `id` và `UserId`. Không dòng nào bị xóa nghĩa là địa chỉ không của khách này hoặc không tồn tại.
        var deleted = await database.SavedAddresses
            .Where(row => row.Id == id && row.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new AppException(NotFoundCode, 404, NotFoundMessage);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Khóa dòng không đủ khi khách chưa có địa chỉ nào, vì không có dòng để khóa.
    // Khóa tư vấn theo khách phủ cả trường hợp đó, và tự nhả khi transaction kết thúc.
    private async Task LockUserAddressesAsync(string userId, CancellationToken cancellationToken)
    {
        var lockName = "saved_addresses:" + userId;
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockName}))", cancellationToken);
    }

    // Bỏ cờ mặc định của mọi địa chỉ khác của khách. Gọi sau khi giữ khóa, nên luôn còn tối đa một địa chỉ mặc định.
    private async Task ClearDefaultAsync(string userId, string? exceptId, CancellationToken cancellationToken)
    {
        await database.SavedAddresses
            .Where(address => address.UserId == userId && address.IsDefault && address.Id != exceptId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(address => address.IsDefault, false), cancellationToken);
    }

    private async Task<JsonDocument> BuildAddressJsonAsync(CheckoutAddressInput address, CancellationToken cancellationToken)
    {
        // Tên tỉnh và phường lấy từ danh mục, không tin client. Mã sai dừng ở đây trước khi ghi gì.
        var names = await addressDirectory.FindAsync(address.ProvinceCode, address.WardCode, cancellationToken);
        if (names is null)
        {
            throw new ValidationException(
                [new ValidationFailure(CheckoutPreviewRequests.AddressField, CheckoutPreviewService.AddressNotFoundMessage)]);
        }

        // Lưu cùng dạng với địa chỉ giao hàng của đơn, để checkout điền sẵn mà không đổi dữ liệu.
        return JsonSerializer.SerializeToDocument(new
        {
            provinceCode = address.ProvinceCode,
            provinceName = names.ProvinceName,
            wardCode = address.WardCode,
            wardName = names.WardName,
            line = address.Line,
        });
    }

    private static SavedAddressResponse ToResponse(SavedAddress address) =>
        new(address.Id, address.Label, address.Name, address.Phone, address.Address.RootElement.Clone(), address.IsDefault);
}
