using System.Text.Json;
using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Callbacks;

// SD-05: kiểm bộ lọc dữ liệu đầu vào của yêu cầu gọi lại và hàm chuyển đổi trạng thái trên dây.
public sealed class CallbackRequestValidatorTests
{
    private readonly CreateCallbackRequestValidator _createValidator = new();
    private readonly UpdateCallbackRequestValidator _updateValidator = new();

    // Đọc JSON thô bằng cấu hình web, khớp cách API giải mã body request.
    private static CreateCallbackRequest ReadCreateBody(string json) =>
        JsonSerializer.Deserialize<CreateCallbackRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static UpdateCallbackRequest ReadUpdateBody(string json) =>
        JsonSerializer.Deserialize<UpdateCallbackRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Theory]
    [InlineData("""{"conversationId":"conv-1","phone":"0901234567","note":"Gọi buổi tối"}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"+84 901 234 567","note":null}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"0901-234-567"}""")]
    public void Create_ValidBody_IsValid(string json)
    {
        Assert.True(_createValidator.Validate(ReadCreateBody(json)).IsValid);
    }

    [Fact]
    // Hệ thống cắt khoảng trắng hai đầu trước khi lưu, nên validator đo độ dài trên chuỗi đã cắt.
    public void Create_NoteOfMaxLengthPaddedWithSpaces_IsValid()
    {
        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv-1",
            phone = "0901234567",
            note = "  " + new string('a', CreateCallbackRequest.MaxNoteLength) + "  ",
        });

        Assert.True(_createValidator.Validate(ReadCreateBody(json)).IsValid);
    }

    [Theory]
    [InlineData("""{"phone":"0901234567"}""")]
    [InlineData("""{"conversationId":null,"phone":"0901234567"}""")]
    [InlineData("""{"conversationId":"","phone":"0901234567"}""")]
    [InlineData("""{"conversationId":"   ","phone":"0901234567"}""")]
    [InlineData("""{"conversationId":123,"phone":"0901234567"}""")]
    public void Create_InvalidConversationId_ReportsErrorOnConversationId(string json)
    {
        var result = _createValidator.Validate(ReadCreateBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("conversationId", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("""{"conversationId":"conv-1"}""")]
    [InlineData("""{"conversationId":"conv-1","phone":null}""")]
    [InlineData("""{"conversationId":"conv-1","phone":""}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"   "}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"0123456789"}""")]
    [InlineData("""{"conversationId":"conv-1","phone":901234567}""")]
    public void Create_InvalidPhone_ReportsErrorOnPhone(string json)
    {
        var result = _createValidator.Validate(ReadCreateBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("phone", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("""{"conversationId":"conv-1","phone":"0901234567","note":123}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"0901234567","note":true}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"0901234567","note":["a"]}""")]
    [InlineData("""{"conversationId":"conv-1","phone":"0901234567","note":{"text":"a"}}""")]
    // Ghi chú gửi sai kiểu JSON phải trả 400 thay vì âm thầm mất dữ liệu.
    public void Create_WrongTypeNote_ReportsErrorOnNote(string json)
    {
        var result = _createValidator.Validate(ReadCreateBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("note", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Create_NoteOverMaxLength_ReportsErrorOnNote()
    {
        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv-1",
            phone = "0901234567",
            note = new string('a', CreateCallbackRequest.MaxNoteLength + 1),
        });

        var result = _createValidator.Validate(ReadCreateBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("note", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(nameof(CallbackRequestStatus.Pending), "pending")]
    [InlineData(nameof(CallbackRequestStatus.Done), "done")]
    [InlineData(nameof(CallbackRequestStatus.Unreachable), "unreachable")]
    public void ToWire_KnownStatus_ReturnsLowercaseValue(string statusName, string expected)
    {
        Assert.Equal(expected, CallbackRequestWire.ToWire(Enum.Parse<CallbackRequestStatus>(statusName)));
    }

    [Theory]
    [InlineData("done", nameof(CallbackRequestStatus.Done))]
    [InlineData("unreachable", nameof(CallbackRequestStatus.Unreachable))]
    public void TryParseHandleStatus_AllowedStatus_ReturnsTrue(string wire, string expectedName)
    {
        Assert.True(CallbackRequestWire.TryParseHandleStatus(wire, out var status));
        Assert.Equal(Enum.Parse<CallbackRequestStatus>(expectedName), status);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("DONE")]
    [InlineData(" done")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseHandleStatus_DisallowedStatus_ReturnsFalse(string? wire)
    {
        Assert.False(CallbackRequestWire.TryParseHandleStatus(wire, out _));
    }

    [Theory]
    [InlineData("""{"status":"done"}""")]
    [InlineData("""{"status":"unreachable"}""")]
    public void Update_AllowedStatus_IsValid(string json)
    {
        Assert.True(_updateValidator.Validate(ReadUpdateBody(json)).IsValid);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"status":null}""")]
    [InlineData("""{"status":""}""")]
    [InlineData("""{"status":"pending"}""")]
    [InlineData("""{"status":"DONE"}""")]
    [InlineData("""{"status":" done"}""")]
    [InlineData("""{"status":5}""")]
    [InlineData("""{"status":true}""")]
    [InlineData("""{"status":["done"]}""")]
    [InlineData("""{"status":{"value":"done"}}""")]
    public void Update_MissingOrDisallowedStatus_ReportsErrorOnStatus(string json)
    {
        var result = _updateValidator.Validate(ReadUpdateBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("status", Assert.Single(result.Errors).PropertyName);
    }
}
