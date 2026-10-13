using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Orders.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class CreateShipmentBodyTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Read_NoBody_ReturnsNullWithoutFailures()
    {
        var failures = new List<ValidationFailure>();

        var parcel = CreateShipmentBody.Read(null, failures);

        Assert.Null(parcel);
        Assert.Empty(failures);
    }

    [Fact]
    public void Read_EmptyBodyElementFromBinder_ReturnsNullWithoutFailures()
    {
        var failures = new List<ValidationFailure>();

        var parcel = CreateShipmentBody.Read(default(JsonElement), failures);

        Assert.Null(parcel);
        Assert.Empty(failures);
    }

    [Fact]
    public void Read_EmptyObject_ReturnsNullWithoutFailures()
    {
        var failures = new List<ValidationFailure>();

        var parcel = CreateShipmentBody.Read(Parse("{}"), failures);

        Assert.Null(parcel);
        Assert.Empty(failures);
    }

    [Fact]
    public void Read_ValidParcel_ReturnsParcel()
    {
        var failures = new List<ValidationFailure>();

        var parcel = CreateShipmentBody.Read(
            Parse("""{"parcel":{"weightGrams":1500,"lengthCm":30,"widthCm":20,"heightCm":12}}"""), failures);

        Assert.Empty(failures);
        Assert.NotNull(parcel);
        Assert.Equal((1500, 30, 20, 12), (parcel.WeightGrams, parcel.LengthCm, parcel.WidthCm, parcel.HeightCm));
    }

    [Theory]
    [InlineData("""{"parcel":{"weightGrams":0,"lengthCm":10,"widthCm":10,"heightCm":10}}""", "parcel.weightGrams")]
    [InlineData("""{"parcel":{"weightGrams":10,"lengthCm":-1,"widthCm":10,"heightCm":10}}""", "parcel.lengthCm")]
    [InlineData("""{"parcel":{"weightGrams":10,"lengthCm":10,"widthCm":1.5,"heightCm":10}}""", "parcel.widthCm")]
    [InlineData("""{"parcel":{"weightGrams":10,"lengthCm":10,"widthCm":10,"heightCm":"10"}}""", "parcel.heightCm")]
    [InlineData("""{"parcel":{"weightGrams":10,"lengthCm":10,"widthCm":10}}""", "parcel.heightCm")]
    [InlineData("""{"parcel":{"weightGrams":2147483648,"lengthCm":10,"widthCm":10,"heightCm":10}}""", "parcel.weightGrams")]
    [InlineData("""{"parcel":{"weightGrams":10,"lengthCm":10,"widthCm":10,"heightCm":10,"extra":1}}""", "parcel.extra")]
    [InlineData("""{"parcel":null}""", "parcel")]
    [InlineData("""{"parcel":"nặng"}""", "parcel")]
    [InlineData("""{"other":1}""", "other")]
    [InlineData("""[1]""", "parcel")]
    public void Read_InvalidBody_ReportsFieldAndReturnsNull(string json, string expectedField)
    {
        var failures = new List<ValidationFailure>();

        var parcel = CreateShipmentBody.Read(Parse(json), failures);

        Assert.Null(parcel);
        Assert.Contains(failures, failure => failure.PropertyName == expectedField);
    }

    [Fact]
    public void Read_IntegerBoundary_AcceptsIntMaxAndRejectsZero()
    {
        var accepted = new List<ValidationFailure>();
        var rejected = new List<ValidationFailure>();

        var parcel = CreateShipmentBody.Read(
            Parse("""{"parcel":{"weightGrams":2147483647,"lengthCm":1,"widthCm":1,"heightCm":1}}"""), accepted);
        CreateShipmentBody.Read(
            Parse("""{"parcel":{"weightGrams":0,"lengthCm":1,"widthCm":1,"heightCm":1}}"""), rejected);

        Assert.Empty(accepted);
        Assert.Equal(int.MaxValue, parcel!.WeightGrams);
        Assert.Single(rejected);
    }
}
