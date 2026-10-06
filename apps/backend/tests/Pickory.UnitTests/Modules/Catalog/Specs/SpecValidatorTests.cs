using System.Text.Json;
using System.Text.Json.Nodes;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Specs;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Specs;

// Port từ `validate-specs.test.ts` của bản Node (CAT-01). Phần lớn trường hợp nằm trong bộ dữ liệu mẫu dùng chung với frontend.
public sealed class SpecValidatorTests
{
    private const string FixturePath = "catalog/spec-validation-cases.json";

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in SharedFixtures.Read(FixturePath)["cases"]!.AsArray())
        {
            data.Add(testCase!["name"]!.GetValue<string>());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    // Mỗi trường hợp trong bộ dữ liệu mẫu cho cùng kết quả và cùng danh sách khóa lỗi như frontend.
    public void Validate_MatchesSharedFixture(string caseName)
    {
        var fixture = SharedFixtures.Read(FixturePath);
        var attributes = fixture["attributes"].Deserialize<List<AttributeDefinition>>(AttributeDefinition.JsonOptions)!;
        var testCase = fixture["cases"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == caseName)!;

        var result = SpecValidator.Validate(testCase["specs"], attributes);

        Assert.Equal(testCase["valid"]!.GetValue<bool>(), result.Valid);
        var expectedKeys = testCase["errorKeys"]!.AsArray().Select(key => key!.GetValue<string>()).Order();
        Assert.Equal(expectedKeys, result.Errors.Select(error => error.Key).Order());
    }

    [Fact]
    // Khóa `constructor` bị thiếu được báo là thiếu thuộc tính, không bị nhầm thành sai kiểu.
    public void Validate_ReportsMissingConstructorKey()
    {
        var definition = new AttributeDefinition
        {
            Key = "constructor",
            Label = "Nhà sản xuất",
            Group = "Tổng quan",
            Type = AttributeTypes.Text,
            Required = true,
            Filterable = false,
        };

        var result = SpecValidator.Validate(new JsonObject(), [definition]);

        var error = Assert.Single(result.Errors);
        Assert.Equal("constructor", error.Key);
        Assert.Contains("Thiếu thuộc tính bắt buộc", error.Message);
    }

    [Fact]
    // Thông báo lỗi nêu nhãn và khóa của thuộc tính, giống bản Node.
    public void Validate_MessageNamesLabelAndKey()
    {
        var definition = new AttributeDefinition
        {
            Key = "ram_gb",
            Label = "Dung lượng RAM",
            Group = "Bộ nhớ",
            Type = AttributeTypes.Number,
            Required = true,
            Filterable = true,
        };

        var result = SpecValidator.Validate(JsonNode.Parse("""{"ram_gb":"16"}"""), [definition]);

        Assert.Equal("Thuộc tính \"Dung lượng RAM\" (ram_gb) phải là số hợp lệ.", Assert.Single(result.Errors).Message);
    }
}
