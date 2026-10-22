using Pickory.Modules.SupportDesk.CannedResponses;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.CannedResponses;

// SD-01: hàm thuần thay biến `{ten_khach}` và `{ma_don}` trong câu trả lời mẫu.
public sealed class CannedResponseTemplateTests
{
    [Fact]
    public void Render_ReplacesBothVariables()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = "Lan", ["ma_don"] = "PK-1001" };

        var text = CannedResponseTemplate.Render("Chào {ten_khach}, đơn {ma_don} đang được giao.", values);

        Assert.Equal("Chào Lan, đơn PK-1001 đang được giao.", text);
    }

    [Fact]
    public void Render_VariableUsedTwice_ReplacesBothOccurrences()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = "Lan" };

        var text = CannedResponseTemplate.Render("{ten_khach} ơi, cảm ơn {ten_khach}.", values);

        Assert.Equal("Lan ơi, cảm ơn Lan.", text);
    }

    [Fact]
    public void Render_MissingKey_KeepsVariableText()
    {
        var values = new Dictionary<string, string?> { ["ma_don"] = "PK-1001" };

        var text = CannedResponseTemplate.Render("Chào {ten_khach}, đơn {ma_don}.", values);

        Assert.Equal("Chào {ten_khach}, đơn PK-1001.", text);
    }

    [Fact]
    public void Render_NullValue_KeepsVariableText()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = null };

        var text = CannedResponseTemplate.Render("Chào {ten_khach}.", values);

        Assert.Equal("Chào {ten_khach}.", text);
    }

    // Giá trị rỗng mà bị thay thẳng thì câu thành "Chào ." và nhìn như lỗi. Giữ chuỗi biến để nhân viên thấy chỗ cần điền.
    [Fact]
    public void Render_EmptyValue_KeepsVariableText()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = string.Empty };

        var text = CannedResponseTemplate.Render("Chào {ten_khach}.", values);

        Assert.Equal("Chào {ten_khach}.", text);
    }

    [Fact]
    public void Render_UnknownVariable_KeepsItUnchanged()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = "Lan", ["email"] = "lan@example.com" };

        var text = CannedResponseTemplate.Render("Chào {ten_khach}, {email} và {khac} giữ nguyên.", values);

        Assert.Equal("Chào Lan, {email} và {khac} giữ nguyên.", text);
    }

    // Giá trị do khách nhập có thể chứa chuỗi biến. Hàm chỉ thay một lượt, nên chuỗi đó không bị thay tiếp.
    [Fact]
    public void Render_ValueContainingVariableText_IsNotReplacedAgain()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = "{ma_don}", ["ma_don"] = "PK-1001" };

        var text = CannedResponseTemplate.Render("Chào {ten_khach}.", values);

        Assert.Equal("Chào {ma_don}.", text);
    }

    [Fact]
    public void Render_BodyWithoutVariables_ReturnsBodyUnchanged()
    {
        var values = new Dictionary<string, string?> { ["ten_khach"] = "Lan" };

        var text = CannedResponseTemplate.Render("Cảm ơn bạn đã liên hệ.", values);

        Assert.Equal("Cảm ơn bạn đã liên hệ.", text);
    }

    [Fact]
    public void Render_NoValuesAtAll_KeepsEveryVariable()
    {
        var text = CannedResponseTemplate.Render("{ten_khach} {ma_don}", new Dictionary<string, string?>());

        Assert.Equal("{ten_khach} {ma_don}", text);
    }
}
