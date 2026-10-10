using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-08: server chưa cấu hình `GHN_WEBHOOK_SECRET`. Header rỗng không được so bằng với secret rỗng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GhnWebhookUnconfiguredSecretTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mot-secret-bat-ky")]
    public async Task Webhook_ServerHasNoSecret_Returns401ForEveryHeader(string? secret)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/ghn")
        {
            Content = new StringContent("{\"OrderCode\":\"ABC\"}", Encoding.UTF8, "application/json"),
        };
        if (secret is not null)
        {
            request.Headers.Add("X-Pickory-Webhook-Secret", secret);
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
