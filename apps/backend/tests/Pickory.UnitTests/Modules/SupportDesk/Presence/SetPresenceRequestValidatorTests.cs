using System.Text.Json;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Presence;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Presence;

// SD-03: body của `PUT /api/admin/support/presence` chỉ nhận chuỗi `available` hoặc `away`.
public sealed class SetPresenceRequestValidatorTests
{
    private readonly SetPresenceRequestValidator _validator = new();

    // Dựng request từ JSON thô, giống cách API đọc body.
    private static SetPresenceRequest ReadBody(string json) =>
        JsonSerializer.Deserialize<SetPresenceRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Theory]
    [InlineData("""{"status":"available"}""")]
    [InlineData("""{"status":"away"}""")]
    public void Validate_AllowedStatus_IsValid(string json)
    {
        var result = _validator.Validate(ReadBody(json));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"status":null}""")]
    [InlineData("""{"status":""}""")]
    [InlineData("""{"status":"   "}""")]
    [InlineData("""{"status":"busy"}""")]
    [InlineData("""{"status":"Available"}""")]
    [InlineData("""{"status":"AWAY"}""")]
    [InlineData("""{"status":" available"}""")]
    [InlineData("""{"status":5}""")]
    [InlineData("""{"status":true}""")]
    [InlineData("""{"status":["available"]}""")]
    [InlineData("""{"status":{"value":"available"}}""")]
    public void Validate_MissingOrUnknownStatus_ReportsErrorOnStatusField(string json)
    {
        var result = _validator.Validate(ReadBody(json));

        Assert.False(result.IsValid);
        Assert.Equal("Status", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("available", nameof(StaffPresenceStatus.Available))]
    [InlineData("away", nameof(StaffPresenceStatus.Away))]
    public void TryParse_AllowedStatus_ReturnsEnum(string wire, string expectedName)
    {
        // Enum của module là `internal`, nên test nhận tên enum dạng chuỗi và đổi lại bên trong.
        Assert.True(StaffPresenceWire.TryParse(wire, out var status));
        Assert.Equal(Enum.Parse<StaffPresenceStatus>(expectedName), status);
    }

    [Theory]
    [InlineData(nameof(StaffPresenceStatus.Available), "available")]
    [InlineData(nameof(StaffPresenceStatus.Away), "away")]
    public void ToWire_Status_ReturnsLowercaseName(string statusName, string expected)
    {
        Assert.Equal(expected, StaffPresenceWire.ToWire(Enum.Parse<StaffPresenceStatus>(statusName)));
    }

    [Fact]
    public void TryParse_UnknownText_ReturnsFalse()
    {
        Assert.False(StaffPresenceWire.TryParse("busy", out _));
        Assert.False(StaffPresenceWire.TryParse(null, out _));
    }
}
