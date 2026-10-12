using FluentValidation;
using Pickory.Modules.Commerce.Orders;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class IdempotencyKeyRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_MissingOrBlank_ThrowsIdempotencyKeyRequired(string? value)
    {
        var exception = Assert.Throws<AppException>(() => IdempotencyKeyRules.Read(value));

        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", exception.Code);
        Assert.Equal(400, exception.StatusCode);
    }

    [Theory]
    [InlineData("0123456789abcdef")]
    [InlineData("A1b2-C3d4_E5f6-G7h8")]
    public void Read_ValidKey_ReturnsKey(string value)
    {
        Assert.Equal(value, IdempotencyKeyRules.Read(value));
    }

    [Fact]
    public void Read_KeyOfOneHundredCharacters_IsAccepted()
    {
        var value = new string('a', 100);

        Assert.Equal(value, IdempotencyKeyRules.Read(value));
    }

    [Theory]
    [InlineData("short-key")]
    [InlineData("0123456789abcde")]
    [InlineData("has space in the key 1234")]
    [InlineData("has.dot.in.the.key.1234")]
    [InlineData("khóa-có-dấu-0123456789")]
    public void Read_BadFormat_ThrowsValidationErrorOnKeyField(string value)
    {
        var exception = Assert.Throws<ValidationException>(() => IdempotencyKeyRules.Read(value));

        Assert.Equal(["idempotencyKey"], exception.Errors.Select(failure => failure.PropertyName).ToArray());
    }

    [Fact]
    public void Read_KeyOfOneHundredOneCharacters_ThrowsValidationError()
    {
        Assert.Throws<ValidationException>(() => IdempotencyKeyRules.Read(new string('a', 101)));
    }
}
