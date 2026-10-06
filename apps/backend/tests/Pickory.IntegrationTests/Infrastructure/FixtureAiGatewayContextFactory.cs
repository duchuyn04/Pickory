using Microsoft.EntityFrameworkCore;
using Pickory.Modules.AiGateway.Persistence;

namespace Pickory.IntegrationTests.Infrastructure;

// Factory trỏ vào DB test của collection. Lớp của gateway tạo context riêng cho mỗi thao tác, giống khi chạy thật.
public sealed class FixtureAiGatewayContextFactory(IntegrationDatabaseFixture database)
    : IDbContextFactory<AiGatewayDbContext>
{
    public AiGatewayDbContext CreateDbContext()
    {
        return database.CreateAiGatewayContext();
    }
}
