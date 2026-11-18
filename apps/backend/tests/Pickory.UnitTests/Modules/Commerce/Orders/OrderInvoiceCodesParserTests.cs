using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Orders.Invoices;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Kiểm tra lớp tách và kiểm định dạng tham số `codes` cho route in hóa đơn.
public sealed class OrderInvoiceCodesParserTests
{
    private static string MakeCode(int index)
    {
        var alphabet = OrderCodeGenerator.Alphabet;
        var chars = new char[OrderCodeGenerator.RandomPartLength];
        var remaining = index;
        for (var position = chars.Length - 1; position >= 0; position--)
        {
            chars[position] = alphabet[remaining % alphabet.Length];
            remaining /= alphabet.Length;
        }

        return OrderCodeGenerator.Prefix + new string(chars);
    }

    private static List<string> GenerateDistinctCodes(int count) =>
        Enumerable.Range(0, count).Select(MakeCode).ToList();

    [Fact]
    // Một mã hợp lệ trả về danh sách một phần tử và không tạo lỗi.
    public void Parse_SingleValidCode_ReturnsCode()
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse("PK7KQ2M9XA", failures);

        Assert.Empty(failures);
        Assert.Equal(["PK7KQ2M9XA"], codes);
    }

    [Fact]
    // Nhiều mã cách nhau bởi dấu phẩy giữ nguyên thứ tự xuất hiện ban đầu.
    public void Parse_MultipleValidCodes_PreservesOrder()
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse("PK7KQ2M9XA,PK3HN8RT2B,PK9VW4XY5Z", failures);

        Assert.Empty(failures);
        Assert.Equal(["PK7KQ2M9XA", "PK3HN8RT2B", "PK9VW4XY5Z"], codes);
    }

    [Fact]
    // Khoảng trắng hai đầu và phần tử rỗng bị loại bỏ trước khi kiểm định dạng.
    public void Parse_WhitespaceAndEmptySegments_AreTrimmedAndSkipped()
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse("  PK7KQ2M9XA , , PK3HN8RT2B , ", failures);

        Assert.Empty(failures);
        Assert.Equal(["PK7KQ2M9XA", "PK3HN8RT2B"], codes);
    }

    [Fact]
    // Mã trùng chỉ giữ lại lần xuất hiện đầu để mỗi đơn chỉ in một hóa đơn.
    public void Parse_DuplicateCodes_KeepsFirstOccurrenceOnly()
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse("PK7KQ2M9XA,PK3HN8RT2B,PK7KQ2M9XA,PK9VW4XY5Z,PK3HN8RT2B", failures);

        Assert.Empty(failures);
        Assert.Equal(["PK7KQ2M9XA", "PK3HN8RT2B", "PK9VW4XY5Z"], codes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(", , ,")]
    // Thiếu tham số hoặc chuỗi toàn phần tử rỗng phải báo lỗi trường `codes`.
    public void Parse_MissingOrEmptyCodes_AddsValidationFailure(string? raw)
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(raw, failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
        Assert.Equal("Chọn ít nhất 1 đơn để in.", failure.ErrorMessage);
    }

    [Fact]
    // Đúng 50 mã khác nhau nằm trong giới hạn cho phép mỗi lần in.
    public void Parse_Exactly50DistinctCodes_Succeeds()
    {
        var expected = GenerateDistinctCodes(50);
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(string.Join(',', expected), failures);

        Assert.Empty(failures);
        Assert.Equal(expected, codes);
    }

    [Fact]
    // Vượt quá 50 mã sau khi bỏ trùng phải bị từ chối với khóa `codes`.
    public void Parse_51DistinctCodes_AddsValidationFailure()
    {
        var input = GenerateDistinctCodes(51);
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(string.Join(',', input), failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
        Assert.Equal("Mỗi lần in tối đa 50 đơn.", failure.ErrorMessage);
    }

    [Fact]
    // Giới hạn 50 tính sau khi bỏ trùng nên 51 phần tử gồm 50 mã duy nhất vẫn hợp lệ.
    public void Parse_51CodesWithDuplicateMaking50Unique_Succeeds()
    {
        var distinct = GenerateDistinctCodes(50);
        var raw = string.Join(',', distinct) + "," + distinct[0];
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(raw, failures);

        Assert.Empty(failures);
        Assert.Equal(distinct, codes);
    }

    [Fact]
    // Chuỗi đúng 4096 ký tự chứa mã hợp lệ và dấu phẩy vẫn đi qua bước kiểm độ dài.
    public void Parse_RawLengthExactly4096WithCommas_IsNotBlockedByLengthLimit()
    {
        var expected = GenerateDistinctCodes(50);
        var joined = string.Join(',', expected);
        var paddedTo4096 = joined + "," + new string(' ', 4096 - joined.Length - 1);
        Assert.Equal(4096, paddedTo4096.Length);
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(paddedTo4096, failures);

        Assert.Empty(failures);
        Assert.Equal(expected, codes);
    }

    [Fact]
    // Chuỗi dài 4097 ký tự bị chặn bởi giới hạn độ dài thô ngay cả khi chỉ dư khoảng trắng.
    public void Parse_RawLength4097WithCommas_AddsLengthValidationFailure()
    {
        var expected = GenerateDistinctCodes(50);
        var joined = string.Join(',', expected);
        var paddedTo4097 = joined + "," + new string(' ', 4097 - joined.Length - 1);
        Assert.Equal(4097, paddedTo4097.Length);
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(paddedTo4097, failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
        Assert.Equal("Danh sách mã đơn vượt quá độ dài cho phép.", failure.ErrorMessage);
    }

    [Fact]
    // Mã trùng chỉ khác chữ hoa và chữ thường vẫn giữ bản chữ thường để báo lỗi định dạng.
    public void Parse_DuplicateDifferingOnlyByCase_RejectsLowercaseAsInvalidFormat()
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse("PK7KQ2M9XA,pk7kq2m9xa", failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
        Assert.Contains("pk7kq2m9xa", failure.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PK7KQ2M9X")]
    [InlineData("PK7KQ2M9XAA")]
    [InlineData("pk7kq2m9xa")]
    [InlineData("PK7kq2m9xa")]
    [InlineData("pK7KQ2M9XA")]
    [InlineData("PK7KQ2M9XO")]
    [InlineData("PK7KQ2M9XI")]
    [InlineData("PK7KQ2M9X0")]
    [InlineData("PK7KQ2M9X1")]
    [InlineData("AB7KQ2M9XA")]
    [InlineData("PK123")]
    // Mã sai độ dài, sai tiền tố, chứa chữ thường hoặc ký tự dễ nhầm phải bị từ chối.
    public void Parse_InvalidCodeFormat_AddsValidationFailureWithCode(string invalidCode)
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse(invalidCode, failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
        Assert.Contains(invalidCode, failure.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    // Khi có nhiều mã sai định dạng, mỗi mã sai sinh một thông báo lỗi riêng.
    public void Parse_MultipleInvalidCodes_ReportsEachInvalidCode()
    {
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Parse("PK7KQ2M9XA,BAD1,BAD2", failures);

        Assert.Empty(codes);
        Assert.Equal(2, failures.Count);
        Assert.All(failures, failure => Assert.Equal("codes", failure.PropertyName));
        Assert.Contains("BAD1", failures[0].ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("BAD2", failures[1].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    // Thiếu khóa `codes` trên query string báo cùng lỗi thiếu mã đơn.
    public void Read_MissingQueryKey_AddsValidationFailure()
    {
        var query = new QueryCollection();
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Read(query, failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
        Assert.Equal("Chọn ít nhất 1 đơn để in.", failure.ErrorMessage);
    }

    [Fact]
    // Gửi tham số `codes` nhiều lần trên URL không rõ ý định nên báo lỗi.
    public void Read_RepeatedQueryKey_AddsValidationFailure()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["codes"] = new StringValues(["PK7KQ2M9XA", "PK3HN8RT2B"]),
        });
        var failures = new List<ValidationFailure>();

        var codes = OrderInvoiceCodesParser.Read(query, failures);

        Assert.Empty(codes);
        var failure = Assert.Single(failures);
        Assert.Equal("codes", failure.PropertyName);
    }
}
