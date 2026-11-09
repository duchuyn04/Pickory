using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.CannedResponses;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Contracts.Metrics;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Metrics;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Modules.SupportDesk.Scheduling;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Modules.SupportDesk.Settings;
using Pickory.Shared.Events;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.SupportDesk;

// Điểm đăng ký duy nhất của module SupportDesk. `Program.cs` không cần biết `DbContext` bên trong module.
public static class SupportDeskModule
{
    public static IServiceCollection AddSupportDeskModule(
        this IServiceCollection services, string databaseConnectionString)
    {
        // Mọi module chia nhau một data source, nên `DATABASE_POOL_MAX` áp cho cả tiến trình.
        // Data source phải biết bốn enum Postgres của module, nếu không Npgsql không đọc và ghi được cột enum.
        services.AddSharedDataSource(databaseConnectionString, SupportDeskDbContext.MapEnums);

        // Ứng dụng không tự chạy migration khi khởi động. Chỉ lệnh chuẩn bị DB gọi `MigrateSupportDeskDatabaseAsync`.
        services.AddDbContext<SupportDeskDbContext>((provider, options) =>
            SupportDeskDbContext.ConfigureOptions(options, provider.GetRequiredService<NpgsqlDataSource>()));

        // Nhịp tim và job `away` cần đồng hồ. Module không giả định module khác đã đăng ký đồng hồ.
        services.TryAddSingleton(TimeProvider.System);
        // Validator của module đều là `internal`, nên phải bật `includeInternalTypes` thì container mới thấy chúng.
        services.AddValidatorsFromAssemblyContaining<SetPresenceRequestValidator>(includeInternalTypes: true);
        // Hub là singleton vì mọi request SSE và mọi nơi phát sự kiện phải dùng chung một danh sách kết nối trong bộ nhớ.
        services.AddSingleton<SupportEventHub>();
        // Streamer không giữ trạng thái riêng nên là singleton. Nó tự tạo scope khi cần ghi DB.
        services.AddSingleton<SupportEventStreamer>();
        // Hub phía khách cũng là singleton. Kết nối thuộc một hội thoại nên mọi producer phải phát qua cùng danh sách.
        services.AddSingleton<CustomerEventHub>();
        // Streamer phía khách không giữ trạng thái riêng. Nó đọc đồng hồ và vòng đời ứng dụng để đóng kết nối.
        services.AddSingleton<CustomerEventStreamer>();
        // Handler chuyển sự kiện miền đã commit cho kết nối của đúng hội thoại. Một handler nghe hai loại sự kiện của kênh khách.
        services.AddScoped<IDomainEventHandler<SupportMessageCreatedEvent>, CustomerEventBroadcastHandler>();
        services.AddScoped<IDomainEventHandler<SupportSessionStatusChangedEvent>, CustomerEventBroadcastHandler>();
        // Phiên chờ cũng phải báo cho nhân viên sẵn sàng. Handler thứ hai nghe cùng sự kiện nhưng phát qua kênh nhân viên.
        services.AddScoped<IDomainEventHandler<SupportSessionStatusChangedEvent>, StaffSessionEventBroadcastHandler>();
        // Sự kiện nhắc lại là sự kiện nội bộ của module. Handler đăng ký ở đây chứ không ở `Contracts`.
        services.AddScoped<IDomainEventHandler<SupportSessionRemindedEvent>, StaffSessionEventBroadcastHandler>();
        // Yêu cầu gọi lại mới cũng chỉ đi tới kênh nhân viên. Handler dùng chung với hai sự kiện phiên.
        services.AddScoped<IDomainEventHandler<CallbackRequestCreatedEvent>, StaffSessionEventBroadcastHandler>();
        // Tin nhắn mới của khách gửi tới nhân viên đang giữ phiên qua sự kiện nội bộ.
        services.AddScoped<IDomainEventHandler<SupportCustomerMessageDispatchedEvent>, StaffSessionEventBroadcastHandler>();
        // Vòng đời scoped vì dịch vụ và job giữ `SupportDeskDbContext` của request hoặc của một vòng worker.
        services.AddScoped<SupportConversationService>();
        services.AddScoped<CallbackRequestService>();
        services.AddScoped<StaffPresenceService>();
        services.AddScoped<StaffPresenceTimeoutProcessor>();
        // Dịch vụ đọc danh sách hàng chờ dùng DbContext theo từng phạm vi request.
        services.AddScoped<SupportSessionQueueService>();
        // Dịch vụ nhận phiên hỗ trợ cần DbContext của request để mở transaction.
        services.AddScoped<SupportSessionClaimService>();
        // Dịch vụ nhân viên gửi tin nhắn cần DbContext của request để mở transaction.
        services.AddScoped<SupportSessionMessageService>();
        // Dịch vụ chuyển phiên hỗ trợ cho đồng nghiệp cần `DbContext` để mở transaction.
        services.AddScoped<SupportSessionTransferService>();
        // Dịch vụ kết thúc phiên hỗ trợ CSKH cần `DbContext` để mở transaction.
        services.AddScoped<SupportSessionCloseService>();
        // Dịch vụ tổng hợp bảng thông tin khách hàng dùng `DbContext` và reader theo từng request.
        services.AddScoped<SupportSessionCustomerService>();
        // Dịch vụ lưu cấu hình CSKH cần `DbContext` để mở transaction chung với audit.
        services.AddScoped<SupportSettingsService>();
        // Dịch vụ mẫu trả lời nhanh cần `DbContext` để mở transaction chung với audit của mẫu dùng chung.
        services.AddScoped<CannedResponseService>();
        // Opener đọc cấu hình từ DB và chạy trong transaction của tin khách, nên dùng chung scope với request.
        services.AddScoped<SupportSettingsReader>();
        services.AddScoped<SupportSessionOpener>();
        // Processor của job nhắc lại cũng cần scope riêng cho mỗi vòng chạy.
        services.AddScoped<WaitNudgeProcessor>();
        // Processor của job tự đóng phiên im lặng cũng cần scope riêng cho mỗi vòng chạy.
        services.AddScoped<AutoCloseSessionProcessor>();
        // Đăng ký lớp cụ thể cho test và interface `ICallbackPhoneRetentionProcessor` cho worker nền.
        services.AddScoped<CallbackPhoneRetentionProcessor>();
        services.AddScoped<ICallbackPhoneRetentionProcessor>(
            provider => provider.GetRequiredService<CallbackPhoneRetentionProcessor>());
        // Đăng ký lớp cụ thể cho test và interface `ISupportConversationMaintenanceProcessor` cho worker nền.
        services.AddScoped<SupportConversationMaintenanceProcessor>();
        services.AddScoped<ISupportConversationMaintenanceProcessor>(
            provider => provider.GetRequiredService<SupportConversationMaintenanceProcessor>());
        // Cổng số liệu cho `admin-console` đọc bằng `DbContext` của request, nên dùng scoped.
        services.AddScoped<ISupportMetrics, SupportMetrics>();
        // Giới hạn tin là singleton vì bộ đếm trong bộ nhớ phải dùng chung cho mọi request của một khách.
        services.AddSingleton<SupportMessageLimiters>();
        // Đăng nhập phát `UserLinkedEvent`. Handler gắn hội thoại vãng lai vào tài khoản vừa đăng nhập.
        services.AddScoped<IDomainEventHandler<UserLinkedEvent>, SupportConversationLinkHandler>();
        // Xóa tài khoản phát `UserDeletedEvent`. Handler xóa dữ liệu gọi lại và hội thoại của người dùng đó.
        services.AddScoped<IDomainEventHandler<UserDeletedEvent>, SupportUserDeletedHandler>();
        // Worker chạy nền gọi processor mỗi 30 giây.
        services.AddHostedService<StaffPresenceTimeoutWorker>();
        // Worker nhắc lại phiên chờ quá lâu chạy mỗi phút, đúng nhịp yêu cầu của spec.
        services.AddHostedService<WaitNudgeWorker>();
        // Worker xóa số điện thoại quá hạn 30 ngày chạy mỗi ngày một lần.
        services.AddHostedService<CallbackPhoneRetentionWorker>();
        // Worker bảo trì hội thoại CSKH chạy mỗi ngày một lần.
        services.AddHostedService<SupportConversationMaintenanceWorker>();
        // Worker tự đóng phiên im lặng và phiên chờ ngoài giờ chạy mỗi phút một lần.
        services.AddHostedService<AutoCloseSessionWorker>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateSupportDeskDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Hàm có sẵn để `Program.cs` gọi một lần, nên các task sau thêm route vào đây mà không phải sửa `Program.cs`.
    public static IEndpointRouteBuilder MapSupportDeskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố. Nhóm chỉ gắn tag `SupportDesk` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("SupportDesk");
        SupportEventsEndpoints.Map(moduleRoutes);
        StaffPresenceEndpoints.Map(moduleRoutes);
        SupportConversationEndpoints.Map(moduleRoutes);
        CallbackRequestEndpoints.Map(moduleRoutes);
        SupportSessionEndpoints.Map(moduleRoutes);
        SupportSettingsEndpoints.Map(moduleRoutes);
        CannedResponseEndpoints.Map(moduleRoutes);
        return endpoints;
    }
}
