using System.Text.Json;
using Pickory.Modules.SupportDesk.Conversations;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Conversations;

// SD-15: body của `POST /api/support/conversations/{id}/messages`. Tin hợp lệ có 1 đến 2.000 ký tự sau khi cắt khoảng trắng hai đầu.
public sealed class SendMessageRequestValidatorTests
{
    private readonly SendMessageRequestValidator _validator = new();

    // Dựng request từ JSON thô, giống cách API đọc body.
    private static SendMessageRequest ReadBody(string json) =>
        JsonSerializer.Deserialize<SendMessageRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void Validate_ShortBody_IsValid()
    {
        Assert.True(_validator.Validate(ReadBody("""{"body":"Xin chào"}""")).IsValid);
    }

    [Fact]
    public void Validate_BodyOfExactlyMaxLength_IsValid()
    {
        var json = JsonSerializer.Serialize(new { body = new string('a', 2000) });

        Assert.True(_validator.Validate(ReadBody(json)).IsValid);
    }

    [Fact]
    public void Validate_BodyOneCharacterOverMaxLength_IsInvalid()
    {
        var json = JsonSerializer.Serialize(new { body = new string('a', 2001) });

        var result = _validator.Validate(ReadBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("Body", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    // Khoảng trắng hai đầu không tính vào độ dài, vì tin được lưu sau khi cắt.
    public void Validate_BodyOfMaxLengthPaddedWithSpaces_IsValid()
    {
        var json = JsonSerializer.Serialize(new { body = "  " + new string('a', 2000) + "  " });

        Assert.True(_validator.Validate(ReadBody(json)).IsValid);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"body":null}""")]
    [InlineData("""{"body":""}""")]
    [InlineData("""{"body":"   "}""")]
    [InlineData("""{"body":"\n\t"}""")]
    [InlineData("""{"body":5}""")]
    [InlineData("""{"body":true}""")]
    [InlineData("""{"body":["a"]}""")]
    [InlineData("""{"body":{"text":"a"}}""")]
    public void Validate_MissingEmptyOrWrongTypeBody_ReportsErrorOnBodyField(string json)
    {
        var result = _validator.Validate(ReadBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("Body", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void TrimmedBody_RemovesSpacesAtBothEnds()
    {
        Assert.Equal("Xin chào", ReadBody("""{"body":"  Xin chào \n"}""").TrimmedBody);
    }
}
