using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Reading;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Reading;

public sealed class CatalogReaderBuildProductImageUrlTests
{
    [Fact]
    public void BuildProductImageUrl_ReturnsProductImageUrlBuildResult()
    {
        // Kiểm tra CatalogReader ủy quyền dựng URL cho ProductImageUrl.
        var credentials = new CloudinaryCredentials("test-cloud", "key", "secret");
        var reader = new CatalogReader(null!, null!, null!, null!, credentials);

        var url = reader.BuildProductImageUrl("img-123");

        Assert.Equal("https://res.cloudinary.com/test-cloud/image/upload/img-123", url);
    }
}
