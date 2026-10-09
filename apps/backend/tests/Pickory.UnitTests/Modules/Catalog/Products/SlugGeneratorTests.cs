using Pickory.Modules.Catalog.Products;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Port từ `domain/slug.test.ts` của bản Node (CAT-02).
public sealed class SlugGeneratorTests
{
    [Theory]
    [InlineData("Laptop ASUS ROG Strix G16 Đen", "laptop-asus-rog-strix-g16-den")]
    [InlineData("Laptop   ASUS -- ROG!!! Strix #$% G16", "laptop-asus-rog-strix-g16")]
    [InlineData("---Tai nghe Sony WH-1000XM5---", "tai-nghe-sony-wh-1000xm5")]
    [InlineData("Á À Ả Ã Ạ Ă Ắ Ằ Ẳ Ẵ Ặ Â Ấ Ầ Ẩ Ẫ Ậ É È Ẻ Ẽ Ẹ Ê Ế Ề Ể Ễ Ệ", "a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-e-e-e-e-e-e-e-e-e-e-e")]
    [InlineData("   @@@ ###   ", "")]
    // Slug bỏ dấu tiếng Việt, gộp ký tự đặc biệt liên tiếp thành một `-`, và không có `-` ở hai đầu.
    public void ToSlug_MatchesNodeBehavior(string name, string expected)
    {
        Assert.Equal(expected, SlugGenerator.ToSlug(name));
    }
}
