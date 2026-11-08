using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk;

// SD-02: `AddSupportDeskModule` là điểm đăng ký duy nhất của module, nên `Program.cs` chỉ cần gọi hàm này.
public sealed class SupportDeskModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    public void AddSupportDeskModule_RegistersDbContext()
    {
        var services = new ServiceCollection();

        services.AddSupportDeskModule(ConnectionString);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SupportDeskDbContext));
    }

    [Fact]
    // SD-17: API phải chạy worker và processor bảo trì hội thoại CSKH hằng ngày.
    public void AddSupportDeskModule_RegistersSupportConversationMaintenanceWorkerAndProcessor()
    {
        var services = new ServiceCollection();

        services.AddSupportDeskModule(ConnectionString);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(SupportConversationMaintenanceWorker));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SupportConversationMaintenanceProcessor)
            && descriptor.ImplementationType == typeof(SupportConversationMaintenanceProcessor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISupportConversationMaintenanceProcessor));
    }

    [Fact]
    // SD-17: Module phải đăng ký handler dọn dẹp dữ liệu hỗ trợ khi tài khoản bị xóa.
    public void AddSupportDeskModule_RegistersSupportUserDeletedHandler()
    {
        var services = new ServiceCollection();

        services.AddSupportDeskModule(ConnectionString);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDomainEventHandler<UserDeletedEvent>)
            && descriptor.ImplementationType == typeof(SupportUserDeletedHandler));
    }
}
