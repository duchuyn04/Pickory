using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Commerce.Orders.Admin;

// File `.xlsx` đã dựng xong trong bộ nhớ, cùng tên file đã ghi vào nhật ký thao tác.
public sealed record OrderExportFile(byte[] Content, string FileName);

// Số đơn nạp dòng hàng mỗi lần, để danh sách id đơn trong tham số không quá dài.
// Test đổi giá trị này để chạy nhánh nhiều lô với vài đơn.
internal sealed record OrderExportLineBatching(int OrderBatchSize = 2_000)
{
    // Lô không dương làm `Chunk` ném lỗi khó hiểu. Chặn sớm ở nơi tạo giá trị.
    public int OrderBatchSize { get; } = OrderBatchSize >= 1
        ? OrderBatchSize
        : throw new ArgumentOutOfRangeException(nameof(OrderBatchSize), "Lô đơn phải có ít nhất một đơn.");
}

// Xuất đơn hàng của nhân viên ra Excel. Dùng chung bộ lọc với danh sách, nhưng đọc mọi đơn khớp thay vì một trang.
internal sealed class AdminOrderExportService(
    CommerceDbContext database,
    ExportOptions exportOptions,
    OrderExportLineBatching lineBatching,
    IAuditLogWriter auditLogWriter,
    TimeProvider clock)
{
    public const string ExportAuditAction = "commerce.orders.export";
    public const string ExportAuditTargetType = "export";
    public const string FileNamePrefix = "don-hang";

    public async Task<OrderExportFile> ExportAsync(
        AdminOrderListQuery filter,
        string actorId,
        CancellationToken cancellationToken)
    {
        // Đọc `MaxRows + 1` đơn. Đơn thứ `MaxRows + 1` chứng minh danh sách vượt giới hạn mà không cần câu `COUNT`.
        var sources = await ReadOrdersAsync(filter, exportOptions.MaxRows + 1, cancellationToken);

        // Kiểm giới hạn trước khi đọc dòng hàng. Danh sách quá lớn không tốn thêm truy vấn, không tạo file và không ghi nhật ký.
        ExportLimits.EnsureWithinLimit(sources.Count, exportOptions.MaxRows);

        var lines = await ReadLinesAsync(sources, cancellationToken);

        // Chụp thời điểm một lần. Tên file và nhật ký dùng cùng một tên.
        var fileName = ExportFileName.Create(FileNamePrefix, clock.GetUtcNow());

        // Dựng file trước khi mở transaction. Dựng file lỗi thì không có nhật ký nào được ghi.
        var content = XlsxWorkbookWriter.Write(
        [
            OrderExportSheets.BuildOrdersSheet(sources.Select(source => source.ToRow(lines.QuantityByOrderCode))),
            OrderExportSheets.BuildLinesSheet(lines.Lines),
        ]);

        // Nhật ký và commit nằm sau khi file đã dựng xong. Ghi nhật ký lỗi thì ném lỗi và route không trả file.
        await WriteAuditLogAsync(filter, actorId, fileName, sources.Count, cancellationToken);
        return new OrderExportFile(content, fileName);
    }

    private async Task<List<OrderExportSource>> ReadOrdersAsync(
        AdminOrderListQuery filter,
        int takeCount,
        CancellationToken cancellationToken)
    {
        var filtered = AdminOrderFilterQuery.Apply(database.Orders.AsNoTracking(), filter);

        // Thứ tự giống danh sách. Dòng hàng của sheet 2 đi theo thứ tự đơn này.
        return await AdminOrderFilterQuery.NewestFirst(filtered)
            .Take(takeCount)
            // Chỉ chọn cột cần ghi ra file. Trường JSON lấy từng khóa trong SQL, nên không nạp cả tài liệu JSON của 50.000 đơn.
            .Select(order => new OrderExportSource
            {
                Id = order.Id,
                Code = order.Code,
                CreatedAt = order.CreatedAt,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                PaymentMethod = order.PaymentMethod,
                CustomerName = order.Contact.RootElement.GetProperty("name").GetString(),
                Phone = order.Contact.RootElement.GetProperty("phone").GetString(),
                Email = order.Contact.RootElement.GetProperty("email").GetString(),
                AddressLine = order.ShippingAddress.RootElement.GetProperty("line").GetString(),
                WardName = order.ShippingAddress.RootElement.GetProperty("wardName").GetString(),
                ProvinceName = order.ShippingAddress.RootElement.GetProperty("provinceName").GetString(),
                Subtotal = order.Subtotal,
                ShippingFee = order.ShippingFee,
                ShippingDiscount = order.ShippingDiscount,
                Total = order.Total,
                // Đơn không có thông tin hóa đơn thì cột `invoice_info` là NULL, và SQL trả NULL cho cả bốn trường.
                InvoiceCompanyName = order.InvoiceInfo!.RootElement.GetProperty("companyName").GetString(),
                InvoiceTaxCode = order.InvoiceInfo!.RootElement.GetProperty("taxCode").GetString(),
                InvoiceAddress = order.InvoiceInfo!.RootElement.GetProperty("address").GetString(),
                InvoiceEmail = order.InvoiceInfo!.RootElement.GetProperty("email").GetString(),
                Flags = order.Flags,
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<OrderExportLines> ReadLinesAsync(
        IReadOnlyList<OrderExportSource> sources,
        CancellationToken cancellationToken)
    {
        var lines = new List<OrderExportLine>();
        var quantityByOrderCode = new Dictionary<string, long>(sources.Count, StringComparer.Ordinal);

        // Nạp dòng hàng theo lô id đơn bằng một truy vấn mỗi lô. Không truy vấn từng đơn và không `Include` trên toàn bộ đơn.
        foreach (var batch in sources.Chunk(lineBatching.OrderBatchSize))
        {
            var linesByOrderId = await ReadLinesOfBatchAsync(batch, cancellationToken);

            // Ghép dòng hàng theo thứ tự đơn của sheet 1. Đơn không có dòng hàng vẫn có tổng số lượng bằng không.
            foreach (var source in batch)
            {
                var quantity = 0L;
                if (linesByOrderId.TryGetValue(source.Id, out var orderLines))
                {
                    lines.AddRange(orderLines);
                    quantity = orderLines.Sum(line => (long)line.Quantity);
                }

                quantityByOrderCode[source.Code] = quantity;
            }
        }

        return new OrderExportLines(lines, quantityByOrderCode);
    }

    // Đọc dòng hàng của một lô đơn, gom theo id đơn. Thứ tự dòng trong mỗi đơn là thứ tự `Id`.
    private async Task<Dictionary<string, List<OrderExportLine>>> ReadLinesOfBatchAsync(
        OrderExportSource[] batch,
        CancellationToken cancellationToken)
    {
        var orderIds = batch.Select(source => source.Id).ToArray();
        var linesByOrderId = new Dictionary<string, List<OrderExportLine>>(batch.Length, StringComparer.Ordinal);
        var batchSourcesById = batch.ToDictionary(source => source.Id, StringComparer.Ordinal);

        // Đọc từng dòng theo luồng để giải phóng `JsonDocument` của phân loại ngay sau khi ghép chữ.
        await foreach (var item in database.OrderItems.AsNoTracking()
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            // Thứ tự ổn định theo `Id` trong mỗi đơn. Hai lần xuất cùng dữ liệu cho cùng thứ tự dòng.
            .OrderBy(candidate => candidate.OrderId)
            .ThenBy(candidate => candidate.Id)
            // Không chọn cột ảnh và các cột không ghi ra file.
            .Select(candidate => new
            {
                candidate.OrderId,
                candidate.SkuCodeSnapshot,
                candidate.NameSnapshot,
                candidate.OptionValues,
                candidate.UnitPrice,
                candidate.Quantity,
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            var order = batchSourcesById[item.OrderId];
            string? variant;
            try
            {
                variant = OrderExportSheets.JoinVariant(item.OptionValues);
            }
            finally
            {
                // `JsonDocument` giữ bộ đệm mượn từ pool. Trả lại ngay để 150.000 dòng hàng không giữ bộ nhớ.
                item.OptionValues.Dispose();
            }

            if (!linesByOrderId.TryGetValue(item.OrderId, out var orderLines))
            {
                orderLines = [];
                linesByOrderId[item.OrderId] = orderLines;
            }

            orderLines.Add(new OrderExportLine(
                order.Code, order.CreatedAt, item.SkuCodeSnapshot, item.NameSnapshot, variant, item.Quantity, item.UnitPrice));
        }

        return linesByOrderId;
    }

    private async Task WriteAuditLogAsync(
        AdminOrderListQuery filter,
        string actorId,
        string fileName,
        int rowCount,
        CancellationToken cancellationToken)
    {
        // Transaction giữ nhật ký và commit cùng một đơn vị. `IAuditLogWriter` bắt buộc nhận transaction của module gọi.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Bảng `audit_logs` chỉ có `before` và `after`. `after` mang bộ lọc và số đơn, tương ứng `details` của contract.
        var entry = new AuditLogEntry(
            actorId,
            ExportAuditAction,
            ExportAuditTargetType,
            fileName,
            null,
            new { filters = OrderExportAuditFilters.From(filter), rowCount });
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        // Commit xong mới trả file. Commit lỗi thì ném lỗi và nhân viên không nhận file không có dấu vết.
        await transaction.CommitAsync(cancellationToken);
    }

    // Kết quả đọc đơn trước khi có dòng hàng. Số sản phẩm của đơn cần tổng số lượng từ dòng hàng, nên chưa đủ để dựng hàng sheet 1.
    private sealed class OrderExportSource
    {
        public string Id { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public OrderStatus Status { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public PaymentMethod PaymentMethod { get; init; }
        public string? CustomerName { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? AddressLine { get; init; }
        public string? WardName { get; init; }
        public string? ProvinceName { get; init; }
        public int Subtotal { get; init; }
        public int ShippingFee { get; init; }
        public int ShippingDiscount { get; init; }
        public int Total { get; init; }
        public string? InvoiceCompanyName { get; init; }
        public string? InvoiceTaxCode { get; init; }
        public string? InvoiceAddress { get; init; }
        public string? InvoiceEmail { get; init; }
        public List<string> Flags { get; init; } = [];

        public OrderExportRow ToRow(IReadOnlyDictionary<string, long> quantityByOrderCode) =>
            new(
                Code,
                CreatedAt,
                OrderExportLabels.ForOrderStatus(Status),
                OrderExportLabels.ForPaymentStatus(PaymentStatus),
                OrderExportLabels.ForPaymentMethod(PaymentMethod),
                CustomerName,
                Phone,
                Email,
                OrderExportSheets.JoinShippingAddress(AddressLine, WardName, ProvinceName),
                quantityByOrderCode[Code],
                Subtotal,
                ShippingFee,
                ShippingDiscount,
                Total,
                InvoiceCompanyName,
                InvoiceTaxCode,
                InvoiceAddress,
                InvoiceEmail,
                OrderExportLabels.HasInvoiceIssued(Flags),
                OrderExportLabels.BuildOrderNote(Flags));
    }

    private sealed record OrderExportLines(List<OrderExportLine> Lines, Dictionary<string, long> QuantityByOrderCode);
}
