using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Reviews.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Reviews;

public sealed class ReviewModerationRequestTests
{
    private static (ReviewModerationRequest Request, List<ValidationFailure> Failures) Read(string json)
    {
        var failures = new List<ValidationFailure>();
        using var document = JsonDocument.Parse(json);
        var request = ReviewModerationRequest.Read(document.RootElement.Clone(), failures);
        return (request, failures);
    }

    [Theory]
    [InlineData("spam")]
    [InlineData("offensive")]
    [InlineData("personal_info")]
    [InlineData("off_topic")]
    [InlineData("misleading")]
    // Mỗi mã lý do ẩn trong danh sách cố định được nhận cho hành động ẩn.
    public void Read_HideWithKnownReason_IsAccepted(string reason)
    {
        var (request, failures) = Read($$"""{ "hidden": true, "reason": "{{reason}}" }""");

        Assert.Empty(failures);
        Assert.True(request.Hidden);
        Assert.Equal(reason, request.Reason);
    }

    [Theory]
    [InlineData("report_rejected")]
    [InlineData("hidden_by_mistake")]
    // Mỗi mã lý do hiện lại trong danh sách cố định được nhận cho hành động hiện.
    public void Read_ShowWithKnownReason_IsAccepted(string reason)
    {
        var (request, failures) = Read($$"""{ "hidden": false, "reason": "{{reason}}" }""");

        Assert.Empty(failures);
        Assert.False(request.Hidden);
        Assert.Equal(reason, request.Reason);
    }

    [Fact]
    // Thiếu `hidden` thì không rõ nhân viên muốn ẩn hay hiện, nên lỗi gắn với trường `hidden`.
    public void Read_MissingHidden_FailsOnHidden()
    {
        var (_, failures) = Read("""{ "reason": "spam" }""");

        Assert.Contains(failures, failure => failure.PropertyName == "hidden");
    }

    [Theory]
    [InlineData("""{ "hidden": "true", "reason": "spam" }""")]
    [InlineData("""{ "hidden": 1, "reason": "spam" }""")]
    [InlineData("""{ "hidden": null, "reason": "spam" }""")]
    // `hidden` chỉ nhận đúng `true` hoặc `false` của JSON.
    public void Read_HiddenOfWrongType_FailsOnHidden(string json)
    {
        var (_, failures) = Read(json);

        Assert.Contains(failures, failure => failure.PropertyName == "hidden");
    }

    [Theory]
    [InlineData("""{ "hidden": true }""")]
    [InlineData("""{ "hidden": true, "reason": "" }""")]
    [InlineData("""{ "hidden": true, "reason": "   " }""")]
    [InlineData("""{ "hidden": true, "reason": null }""")]
    [InlineData("""{ "hidden": true, "reason": 5 }""")]
    [InlineData("""{ "hidden": false }""")]
    [InlineData("""{ "hidden": false, "reason": "" }""")]
    // Lý do là bắt buộc cho cả hai hành động, vì audit là nơi duy nhất giữ lý do.
    public void Read_MissingOrBlankReason_FailsOnReason(string json)
    {
        var (_, failures) = Read(json);

        Assert.Contains(failures, failure => failure.PropertyName == "reason");
    }

    [Theory]
    [InlineData("""{ "hidden": false, "reason": "spam" }""")]
    [InlineData("""{ "hidden": true, "reason": "report_rejected" }""")]
    [InlineData("""{ "hidden": true, "reason": "hidden_by_mistake" }""")]
    // Mã lý do của hành động này không dùng được cho hành động kia.
    public void Read_ReasonOfOtherAction_FailsOnReason(string json)
    {
        var (_, failures) = Read(json);

        Assert.Contains(failures, failure => failure.PropertyName == "reason");
    }

    [Theory]
    [InlineData("SPAM")]
    [InlineData("Spam")]
    [InlineData(" spam ")]
    [InlineData("unknown_code")]
    // Mã so khớp đúng chữ thường, không cắt khoảng trắng và không đoán gần đúng.
    public void Read_ReasonNotExactlyInList_FailsOnReason(string reason)
    {
        var (_, failures) = Read($$"""{ "hidden": true, "reason": "{{reason}}" }""");

        Assert.Contains(failures, failure => failure.PropertyName == "reason");
    }

    [Fact]
    // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi.
    public void Read_UnknownField_FailsOnThatField()
    {
        var (_, failures) = Read("""{ "hidden": true, "reason": "spam", "extra": 1 }""");

        Assert.Contains(failures, failure => failure.PropertyName == "extra");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"spam\"")]
    [InlineData("null")]
    // Body không phải đối tượng JSON thì không có trường nào để đọc.
    public void Read_BodyIsNotObject_Fails(string json)
    {
        var (_, failures) = Read(json);

        Assert.NotEmpty(failures);
    }

    [Fact]
    // Thiếu body hoàn toàn (không gửi nội dung) cũng bị từ chối.
    public void Read_NoBody_Fails()
    {
        var failures = new List<ValidationFailure>();

        ReviewModerationRequest.Read(null, failures);

        Assert.NotEmpty(failures);
    }
}
