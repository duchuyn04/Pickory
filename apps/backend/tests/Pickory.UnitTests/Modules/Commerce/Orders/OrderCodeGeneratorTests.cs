using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderCodeGeneratorTests
{
    [Fact]
    // Duyệt đủ mọi chỉ số của bảng chữ, để mọi ký tự có thể xuất hiện đều bị kiểm.
    public void Generate_WithEveryAlphabetIndex_NeverEmitsAmbiguousCharacters()
    {
        var allCharacters = new List<char>();
        for (var start = 0; start < OrderCodeGenerator.Alphabet.Length; start += OrderCodeGenerator.RandomPartLength)
        {
            var offset = start;
            var step = 0;
            var code = OrderCodeGenerator.Generate(_ => (offset + step++) % OrderCodeGenerator.Alphabet.Length);
            allCharacters.AddRange(code);
        }

        Assert.All(allCharacters, character => Assert.DoesNotContain(character, "0O1I"));
    }

    [Fact]
    // Mã luôn gồm tiền tố `PK` và 8 ký tự thuộc bảng chữ, tổng 10 ký tự.
    public void Generate_ReturnsPrefixPlusEightAlphabetCharacters()
    {
        var random = new Random(20261011);

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var code = OrderCodeGenerator.Generate(random.Next);

            Assert.Equal(10, code.Length);
            Assert.StartsWith("PK", code, StringComparison.Ordinal);
            Assert.All(code[2..], character => Assert.Contains(character, OrderCodeGenerator.Alphabet));
        }
    }

    [Fact]
    // Mã sinh từ bộ sinh số mật mã thật cũng phải đúng độ dài và bảng chữ.
    public void Generate_WithoutArguments_ReturnsWellFormedCode()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = OrderCodeGenerator.Generate();

            Assert.Equal(10, code.Length);
            Assert.StartsWith("PK", code, StringComparison.Ordinal);
            Assert.All(code[2..], character => Assert.Contains(character, OrderCodeGenerator.Alphabet));
        }
    }

    [Fact]
    // Cùng nguồn ngẫu nhiên thì ra cùng mã. Đây là điều kiện để test lặp lại được.
    public void Generate_WithSameRandomSource_ReturnsSameCode()
    {
        var first = OrderCodeGenerator.Generate(new Random(7).Next);
        var second = OrderCodeGenerator.Generate(new Random(7).Next);

        Assert.Equal(first, second);
    }

    [Fact]
    // Bảng chữ có đúng 24 chữ cái và 8 chữ số, không lặp ký tự.
    public void Alphabet_HasThirtyTwoDistinctCharactersWithoutAmbiguousOnes()
    {
        Assert.Equal(32, OrderCodeGenerator.Alphabet.Length);
        Assert.Equal(32, OrderCodeGenerator.Alphabet.Distinct().Count());
        Assert.Equal(24, OrderCodeGenerator.Alphabet.Count(char.IsAsciiLetterUpper));
        Assert.Equal(8, OrderCodeGenerator.Alphabet.Count(char.IsAsciiDigit));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    // Nguồn ngẫu nhiên trả chỉ số ngoài bảng thì hàm dừng, không sinh mã chứa ký tự lạ.
    public void Generate_WhenRandomSourceReturnsIndexOutsideAlphabet_Throws(int badIndex)
    {
        Assert.Throws<InvalidOperationException>(() => OrderCodeGenerator.Generate(_ => badIndex));
    }

    [Fact]
    // Bộ sinh phải hỏi nguồn ngẫu nhiên với đúng độ dài bảng chữ, nếu không một phần bảng không bao giờ được chọn.
    public void Generate_AsksRandomSourceWithAlphabetLength()
    {
        var requestedBounds = new List<int>();

        OrderCodeGenerator.Generate(bound =>
        {
            requestedBounds.Add(bound);
            return 0;
        });

        Assert.Equal(8, requestedBounds.Count);
        Assert.All(requestedBounds, bound => Assert.Equal(32, bound));
    }
}
