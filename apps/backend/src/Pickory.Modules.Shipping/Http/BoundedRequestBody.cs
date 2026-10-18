using Microsoft.AspNetCore.Http;

namespace Pickory.Modules.Shipping.Http;

// Đọc body của request có giới hạn kích thước. Route webhook và route E2E dùng chung để cùng một luật chặn body lớn.
internal static class BoundedRequestBody
{
    private const int ChunkSizeBytes = 4096;

    // Trả `null` khi body dài hơn `maxBytes`. Bên gọi tự chọn lỗi validation với tên trường của route mình.
    public static async Task<byte[]?> ReadAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkSizeBytes];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);

            // Dừng đọc ngay khi vượt giới hạn, để body lớn không bị đọc hết vào bộ nhớ.
            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }
}
