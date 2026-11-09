using Pickory.Modules.SupportDesk.CannedResponses;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.CannedResponses;

// SD-10: luật `shortcut` và `body` của mẫu trả lời nhanh. Hàm thuần nên không cần DB.
public sealed class CannedShortcutRulesTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("camon")]
    [InlineData("ma-don_2")]
    [InlineData("0123456789")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void IsValidShortcut_AllowedCharactersWithinLength_ReturnsTrue(string shortcut)
    {
        Assert.True(CannedShortcutRules.IsValidShortcut(shortcut));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Camon")]
    [InlineData("ca mon")]
    [InlineData("/camon")]
    [InlineData("ca.mon")]
    [InlineData("camơn")]
    [InlineData("abc\n")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void IsValidShortcut_EmptyUppercaseSymbolsOrTooLong_ReturnsFalse(string? shortcut)
    {
        Assert.False(CannedShortcutRules.IsValidShortcut(shortcut));
    }

    [Fact]
    public void IsValidBody_CountsLengthAfterTrim()
    {
        Assert.True(CannedShortcutRules.IsValidBody("  x  "));
        Assert.True(CannedShortcutRules.IsValidBody(" " + new string('b', 2000) + " "));
        Assert.False(CannedShortcutRules.IsValidBody(new string('b', 2001)));
        Assert.False(CannedShortcutRules.IsValidBody("   "));
        Assert.False(CannedShortcutRules.IsValidBody(null));
    }
}
