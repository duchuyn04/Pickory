using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Features.Products;

// Kiểm ảnh mới với kho ảnh và đồng bộ danh sách ảnh của sản phẩm. Ảnh bị bỏ được xếp lịch xóa.
public sealed class ProductImageSync(CatalogDbContext database, IImageStorage imageStorage, TimeProvider timeProvider)
{
    // Chạy trước transaction ghi sản phẩm. Ảnh sai bị xếp lịch xóa ngay, vì ảnh đã nằm trên Cloudinary dù request thất bại.
    public async Task VerifyNewImagesAsync(
        string? productId, IReadOnlyList<ProductImageRequest>? images, CancellationToken cancellationToken)
    {
        if (images is null || images.Count == 0)
        {
            return;
        }

        // Ảnh đã gắn từ trước đã được kiểm lúc gắn, nên không hỏi lại kho ảnh.
        var attachedPublicIds = productId is null
            ? []
            : await database.ProductImages.AsNoTracking()
                .Where(image => image.ProductId == productId)
                .Select(image => image.PublicId)
                .ToListAsync(cancellationToken);

        foreach (var image in images.Where(image => !attachedPublicIds.Contains(image.PublicId)))
        {
            await VerifyStoredImageAsync(image.PublicId, cancellationToken);
        }
    }

    private async Task VerifyStoredImageAsync(string publicId, CancellationToken cancellationToken)
    {
        var stored = await imageStorage.FindAsync(publicId, cancellationToken);
        // Ảnh không có trên kho ảnh thì không có gì để xóa, chỉ báo lỗi.
        if (stored is null)
        {
            throw new AppException("IMAGE_NOT_FOUND", 400, "Không tìm thấy ảnh đã tải lên. Hãy tải ảnh lên lại.");
        }

        // Cloudinary đã chặn định dạng qua chữ ký. API kiểm lại vì người gọi API có thể tải ảnh bằng cách khác.
        if (!ImageUploadRules.IsAllowedFormat(stored.Format))
        {
            await ScheduleDeletionAsync(publicId, cancellationToken);
            throw new AppException("IMAGE_FORMAT_INVALID", 400, "Ảnh phải có định dạng JPG, PNG hoặc WebP.");
        }

        // Cloudinary không giới hạn kích thước qua chữ ký, nên API là nơi duy nhất chặn ảnh trên 5 MB.
        if (stored.Bytes > ImageUploadRules.MaxBytes)
        {
            await ScheduleDeletionAsync(publicId, cancellationToken);
            throw new AppException("IMAGE_TOO_LARGE", 400, "Ảnh vượt quá 5 MB.");
        }
    }

    // Ghi job xóa ngay, ngoài transaction sản phẩm, để job vẫn còn khi request trả lỗi.
    private async Task ScheduleDeletionAsync(string publicId, CancellationToken cancellationToken)
    {
        database.ImageDeletionJobs.Add(NewDeletionJob(publicId, timeProvider.GetUtcNow().UtcDateTime));
        await database.SaveChangesAsync(cancellationToken);
    }

    // Hàm chạy trong transaction ghi sản phẩm. Ảnh bị bỏ và job xóa của ảnh cùng thành công hoặc cùng hoàn nguyên.
    public void Apply(Product product, IReadOnlyList<ProductImageRequest> images, DateTime now)
    {
        // Ảnh riêng của biến thể chỉ được trỏ tới SKU của chính sản phẩm này.
        var productSkuIds = product.Skus.Select(sku => sku.Id).ToHashSet(StringComparer.Ordinal);
        if (images.Any(image => image.SkuId is not null && !productSkuIds.Contains(image.SkuId)))
        {
            throw new AppException("IMAGE_SKU_INVALID", 400, "Ảnh chỉ được gắn với biến thể của chính sản phẩm này.");
        }

        var targetPublicIds = images.Select(image => image.PublicId).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in product.Images.Where(image => !targetPublicIds.Contains(image.PublicId)).ToList())
        {
            product.Images.Remove(removed);
            database.ProductImages.Remove(removed);
            database.ImageDeletionJobs.Add(NewDeletionJob(removed.PublicId, now));
        }

        var existingByPublicId = product.Images.ToDictionary(image => image.PublicId, StringComparer.Ordinal);
        for (var position = 0; position < images.Count; position++)
        {
            var request = images[position];
            // Thứ tự trong danh sách gửi lên là thứ tự hiển thị trong gallery.
            if (existingByPublicId.TryGetValue(request.PublicId, out var existing))
            {
                existing.Alt = request.Alt.Trim();
                existing.SkuId = request.SkuId;
                existing.Position = position;
                continue;
            }

            var added = new ProductImage
            {
                Id = Guid.NewGuid().ToString("N"),
                ProductId = product.Id,
                SkuId = request.SkuId,
                PublicId = request.PublicId,
                Alt = request.Alt.Trim(),
                Position = position,
                CreatedAt = now,
            };
            product.Images.Add(added);
            // Đánh dấu thêm rõ ràng vì ảnh mới đã có định danh, tránh EF Core coi là bản ghi cần cập nhật.
            database.ProductImages.Add(added);
        }
    }

    private static ImageDeletionJob NewDeletionJob(string publicId, DateTime now) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        PublicId = publicId,
        Status = ImageDeletionProcessor.PendingStatus,
        Attempts = 0,
        MaxAttempts = 8,
        // Job đến hạn ngay, để worker xóa ảnh ở vòng chạy kế tiếp.
        NextRetryAt = now,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
