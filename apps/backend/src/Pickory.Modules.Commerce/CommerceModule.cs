using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Modules.Commerce.Addresses;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Configuration;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Orders.Admin;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Payments.Admin;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Commerce.Settings;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Shared.Events;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Commerce;

// Điểm đăng ký duy nhất của module Commerce. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class CommerceModule
{
    public static IServiceCollection AddCommerceModule(
        this IServiceCollection services, string databaseConnectionString, AppConfig config, bool isProduction)
    {
        // Production thiếu khóa SePay thì dừng ngay khi khởi động, trước khi nhận bất kỳ request nào.
        SepayConfigurationCheck.EnsureConfigured(config, isProduction);

        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<CommerceDbContext>(options =>
            CommerceDbContext.ConfigureOptions(options, databaseConnectionString));

        // Job hết hạn thanh toán và luật thời hạn cần đồng hồ. Module không giả định module khác đã đăng ký đồng hồ.
        services.TryAddSingleton(TimeProvider.System);

        // Giới hạn commerce đọc từ một lớp duy nhất. Giỏ hàng và đặt đơn cùng dùng nguồn giá trị này.
        services.AddScoped<CommerceSettingsProvider>();
        // Trang cấu hình của admin đọc và ghi dòng cấu hình qua lớp riêng, vì phải ghi audit cùng transaction.
        services.AddScoped<CommerceSettingsStore>();
        services.AddScoped<CartService>();
        services.AddScoped<CartMergeService>();
        // Sổ địa chỉ giữ khóa theo khách cho mỗi thao tác ghi, nên dịch vụ có vòng đời theo request.
        services.AddScoped<SavedAddressService>();
        // Preview đọc giỏ, catalog và shipping, rồi tính tiền mà không ghi DB.
        services.AddScoped<CheckoutPreviewService>();
        // Đặt đơn dùng lại phần tính tiền của preview, rồi giữ hàng và ghi đơn trong một transaction.
        services.AddScoped<PlaceOrderService>();
        // Dịch vụ theo dõi và tự hủy đơn hàng phục vụ các route phía khách hàng.
        // Khách tự hủy và nhân viên hủy dùng chung một lớp, nên hai bên trả kho và ghi lịch sử giống nhau.
        services.AddScoped<OrderCancellationService>();
        services.AddScoped<OrderTrackingService>();
        // Danh sách và chi tiết đơn của nhân viên đọc mọi đơn, nên tách khỏi dịch vụ theo dõi của khách.
        services.AddScoped<AdminOrderService>();
        // Thao tác ghi của nhân viên (xác nhận, hủy, đánh dấu hóa đơn) tách khỏi dịch vụ đọc để phạm vi quyền rõ ràng.
        services.AddScoped<AdminOrderActionService>();
        // Tạo và hủy vận đơn gọi GHN ngoài transaction, nên tách riêng để khóa dòng ngắn.
        services.AddScoped<OrderParcelResolver>();
        services.AddScoped<AdminOrderShipmentService>();
        // Nguồn mã đơn là singleton không giữ trạng thái. Test thay bằng nguồn có kịch bản để ép tình huống trùng mã.
        services.AddSingleton<IOrderCodeSource, RandomOrderCodeSource>();

        // Bộ đếm tần suất của route preview nằm trong bộ nhớ, nên phải là singleton để mọi request dùng chung một bộ đếm.
        services.AddSingleton<CheckoutPreviewRateLimiter>();
        // Webhook giữ bộ đếm chung theo IP, còn dịch vụ khớp dùng context riêng của từng request.
        services.AddSingleton<SepayWebhookRateLimiter>();
        services.AddScoped<SepayPaymentService>();
        // Luật tiền về muộn giữ kho trong cùng context và transaction khớp webhook.
        services.AddScoped<SepayLatePaymentMatcher>();
        // Luật khớp một giao dịch vào một đơn dùng chung cho webhook và route gắn tay của nhân viên.
        services.AddScoped<SepayOrderMatcher>();
        // Danh sách giao dịch cho nhân viên đối soát đọc riêng, không đi qua dịch vụ webhook.
        services.AddScoped<AdminPaymentTransactionService>();
        // Gắn tay và bỏ qua giao dịch ghi audit và khóa dòng, nên tách khỏi dịch vụ đọc danh sách.
        services.AddScoped<AdminPaymentReconciliationService>();
        // Processor dùng scope riêng để từng vòng đọc lại đơn và trả kho trong transaction của Commerce.
        services.AddScoped<ExpiredPaymentProcessor>();
        // Worker kiểm hạn mỗi phút ngay cả khi không có request từ khách.
        services.AddHostedService<ExpiredPaymentWorker>();

        // Gộp giỏ vãng lai khi khách đăng nhập, và xóa giỏ cùng sổ địa chỉ khi khách xóa tài khoản.
        services.AddScoped<IDomainEventHandler<UserLinkedEvent>, CartLinkHandler>();
        services.AddScoped<IDomainEventHandler<UserDeletedEvent>, UserDeletedCommerceHandler>();
        // Vận đơn báo đã giao hoặc đã hoàn thì đơn đổi trạng thái tương ứng.
        services.AddScoped<IDomainEventHandler<ShipmentStatusChangedEvent>, ShipmentStatusChangedHandler>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateCommerceDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommerceDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố, và gắn tag `Commerce` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("Commerce");
        CartEndpoints.Map(moduleRoutes);
        CheckoutEndpoints.Map(moduleRoutes);
        OrderEndpoints.Map(moduleRoutes);
        SavedAddressEndpoints.Map(moduleRoutes);
        AdminOrderEndpoints.Map(moduleRoutes);
        AdminPaymentEndpoints.Map(moduleRoutes);
        CommerceSettingsEndpoints.Map(moduleRoutes);
        // Route SePay có hợp đồng xác thực và phản hồi riêng, không yêu cầu phiên web.
        SepayWebhookEndpoint.Map(moduleRoutes);
        return endpoints;
    }
}
