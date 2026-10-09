using Pickory.Modules.Catalog.Images;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Images;

// Chữ ký phải khớp cách Cloudinary tự tính lại. Sai một ký tự thì mọi lần tải ảnh bị từ chối.
public sealed class CloudinarySignatureTests
{
    [Fact]
    // Ví dụ trong tài liệu Cloudinary: secret `abcd`, kết quả SHA-1 tính sẵn. Đây là giá trị tính tay của tiêu chí.
    public void Sign_MatchesCloudinaryDocumentationExample()
    {
        var parameters = new Dictionary<string, string>
        {
            ["timestamp"] = "1315060510",
            ["public_id"] = "sample_image",
            ["eager"] = "w_400,h_300,c_pad|w_260,h_200,c_crop",
        };

        var signature = CloudinarySignature.Sign(parameters, "abcd");

        Assert.Equal("bfd09f95f331f558cbd1320e67aa8d488770583e", signature);
    }

    [Fact]
    // Thứ tự truyền tham số không được đổi chữ ký, vì Cloudinary sắp xếp theo tên trước khi ký.
    public void Sign_SortsParametersByName()
    {
        var forward = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var backward = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };

        Assert.Equal(CloudinarySignature.Sign(forward, "secret"), CloudinarySignature.Sign(backward, "secret"));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("cloud_name")]
    [InlineData("resource_type")]
    [InlineData("api_key")]
    // Bốn tham số này không bao giờ nằm trong chuỗi ký. Ký kèm chúng thì Cloudinary báo sai chữ ký.
    public void Sign_IgnoresParametersCloudinaryExcludes(string excludedName)
    {
        var withoutExcluded = new Dictionary<string, string> { ["timestamp"] = "1700000000" };
        var withExcluded = new Dictionary<string, string>
        {
            ["timestamp"] = "1700000000",
            [excludedName] = "bat-ky",
        };

        Assert.Equal(
            CloudinarySignature.Sign(withoutExcluded, "secret"),
            CloudinarySignature.Sign(withExcluded, "secret"));
    }

    [Fact]
    // Tham số rỗng không được gửi lên Cloudinary, nên cũng không được nằm trong chuỗi ký.
    public void Sign_IgnoresEmptyValues()
    {
        var withoutFolder = new Dictionary<string, string> { ["timestamp"] = "1700000000" };
        var withEmptyFolder = new Dictionary<string, string> { ["timestamp"] = "1700000000", ["folder"] = "" };

        Assert.Equal(
            CloudinarySignature.Sign(withoutFolder, "secret"),
            CloudinarySignature.Sign(withEmptyFolder, "secret"));
    }
}
