using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.UnitTests.Shared.Http;

// FLT-01: từ khóa tìm kiếm dùng chung. Module chỉ ghép `LikePattern` vào `EF.Functions.ILike`.
public class SearchTermTests
{
    private static QueryCollection QueryOf(params string[] searchValues)
    {
        return new QueryCollection(new Dictionary<string, StringValues> { ["search"] = new StringValues(searchValues) });
    }

    private static SearchTerm? Read(string rawSearch, List<ValidationFailure> failures)
    {
        return SearchTerm.Read(QueryOf(rawSearch), failures);
    }

    [Fact]
    // Không gửi tham số thì không lọc.
    public void Read_MissingParameter_ReturnsNull()
    {
        var failures = new List<ValidationFailure>();

        var term = SearchTerm.Read(new QueryCollection(), failures);

        Assert.Null(term);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("  nguyen  ", "nguyen")]
    [InlineData("nguyen    van   an", "nguyen van an")]
    [InlineData("\t nguyen \n van ", "nguyen van")]
    // Khoảng trắng hai đầu bị bỏ và khoảng trắng liền nhau gộp thành một.
    public void Read_NormalizesWhitespace(string raw, string expectedText)
    {
        var failures = new List<ValidationFailure>();

        var term = Read(raw, failures);

        Assert.NotNull(term);
        Assert.Equal(expectedText, term.Text);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    // Chuỗi rỗng sau chuẩn hóa nghĩa là không lọc, và không phải lỗi.
    public void Read_BlankValue_ReturnsNullWithoutFailure(string raw)
    {
        var failures = new List<ValidationFailure>();

        var term = Read(raw, failures);

        Assert.Null(term);
        Assert.Empty(failures);
    }

    [Fact]
    // Đúng 100 ký tự vẫn đạt.
    public void Read_ExactlyMaxLength_IsAccepted()
    {
        var failures = new List<ValidationFailure>();

        var term = Read(new string('a', 100), failures);

        Assert.NotNull(term);
        Assert.Empty(failures);
    }

    [Fact]
    // 101 ký tự ghi lỗi cho trường `search`.
    public void Read_OverMaxLength_AddsFailureForSearchField()
    {
        var failures = new List<ValidationFailure>();

        var term = Read(new string('a', 101), failures);

        Assert.Null(term);
        var failure = Assert.Single(failures);
        Assert.Equal("search", failure.PropertyName);
    }

    [Fact]
    // Độ dài tính sau chuẩn hóa. Khoảng trắng thừa hai đầu không làm từ khóa 100 ký tự bị từ chối.
    public void Read_MaxLengthWithSurroundingSpaces_IsAccepted()
    {
        var failures = new List<ValidationFailure>();

        var term = Read("   " + new string('a', 100) + "   ", failures);

        Assert.NotNull(term);
        Assert.Empty(failures);
    }

    [Fact]
    // Tham số lặp lại ghi lỗi, vì không rõ giá trị nào là ý của người dùng.
    public void Read_RepeatedParameter_AddsFailureForSearchField()
    {
        var failures = new List<ValidationFailure>();

        var term = SearchTerm.Read(QueryOf("nguyen", "tran"), failures);

        Assert.Null(term);
        var failure = Assert.Single(failures);
        Assert.Equal("search", failure.PropertyName);
    }

    [Theory]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("\\%_", "%\\\\\\%\\_%")]
    [InlineData("nguyen", "%nguyen%")]
    // Ký tự `\`, `%` và `_` trong từ khóa giữ nghĩa ký tự thường. `\` phải được escape trước, để không escape hai lần.
    public void Read_EscapesLikeMetacharacters(string raw, string expectedPattern)
    {
        var failures = new List<ValidationFailure>();

        var term = Read(raw, failures);

        Assert.NotNull(term);
        Assert.Equal(expectedPattern, term.LikePattern);
    }

    [Fact]
    // Escape không đổi nội dung `Text`. Giao diện và log vẫn thấy từ khóa gốc đã chuẩn hóa.
    public void Read_TextIsNotEscaped()
    {
        var failures = new List<ValidationFailure>();

        var term = Read("a%b", failures);

        Assert.NotNull(term);
        Assert.Equal("a%b", term.Text);
    }

    [Fact]
    // Ký tự escape là hằng công khai để module truyền vào `EF.Functions.ILike`.
    public void EscapeCharacter_IsBackslash()
    {
        Assert.Equal("\\", SearchTerm.EscapeCharacter);
    }
}
