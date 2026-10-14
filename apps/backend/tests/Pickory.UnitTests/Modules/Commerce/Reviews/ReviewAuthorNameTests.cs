using Pickory.Modules.Commerce.Reviews;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Reviews;

public sealed class ReviewAuthorNameTests
{
    [Theory]
    [InlineData("Nguyễn Văn An", "An N.")]
    // Tên hai từ: tên cuối cùng, rồi chữ cái đầu của họ.
    [InlineData("Trần Bình", "Bình T.")]
    // Tên một từ giữ nguyên, vì không có họ để rút gọn.
    [InlineData("Linh", "Linh")]
    // Khoảng trắng thừa ở đầu, cuối và giữa không làm lệch kết quả.
    [InlineData("  Lê   Thị  Hoa ", "Hoa L.")]
    // Chữ cái đầu của họ được viết hoa, kể cả khi người dùng gõ chữ thường.
    [InlineData("phạm quốc dũng", "dũng P.")]
    public void Shorten_UsesLastNameThenInitialOfFirstName(string fullName, string expected)
    {
        Assert.Equal(expected, ReviewAuthorName.Shorten(fullName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Không có tên người nhận thì hiện nhãn chung, không để trống.
    public void Shorten_WithoutName_ReturnsAnonymousLabel(string? fullName)
    {
        Assert.Equal(ReviewAuthorName.AnonymousLabel, ReviewAuthorName.Shorten(fullName));
        Assert.Equal("Khách hàng", ReviewAuthorName.AnonymousLabel);
    }

    [Fact]
    // Chữ cái đầu có dấu dạng tổ hợp vẫn được lấy trọn một ký tự người đọc thấy.
    public void Shorten_KeepsCombiningMarksOfTheInitial()
    {
        var decomposedFirstName = "Én Minh";

        Assert.Equal("Minh É.", ReviewAuthorName.Shorten(decomposedFirstName));
    }
}
