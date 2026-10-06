using System.Text.Json.Nodes;
using Pickory.Modules.Catalog.Descriptions;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Descriptions;

// Port phần `descriptionSchema` của `description.test.ts` ở bản Node (CAT-13).
public sealed class DescriptionSchemaTests
{
    private const string FixturePath = "catalog/description-cases.json";

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in SharedFixtures.Read(FixturePath)["cases"]!.AsArray())
        {
            data.Add(testCase!["name"]!.GetValue<string>());
        }

        return data;
    }

    private static JsonObject Paragraphs(int count, string? text = null)
    {
        var content = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            content.Add(text is null
                ? new JsonObject { ["type"] = "paragraph" }
                : new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) });
        }

        return new JsonObject { ["type"] = "doc", ["content"] = content };
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    // Mỗi trường hợp trong bộ dữ liệu mẫu cho cùng kết quả như schema Zod của frontend.
    public void Validate_MatchesSharedFixture(string caseName)
    {
        var testCase = SharedFixtures.Read(FixturePath)["cases"]!.AsArray()
            .Single(item => item!["name"]!.GetValue<string>() == caseName)!;

        var result = DescriptionSchema.Validate(testCase["doc"]);

        Assert.Equal(testCase["valid"]!.GetValue<bool>(), result.Valid);
    }

    [Fact]
    // Đúng 20.000 ký tự text được nhận. Thêm 1 ký tự thì bị từ chối với thông báo giống bản Node.
    public void Validate_EnforcesTextLengthBoundary()
    {
        var atLimit = DescriptionSchema.Validate(Paragraphs(1, new string('a', DescriptionSchema.MaxTextLength)));
        var overLimit = DescriptionSchema.Validate(Paragraphs(1, new string('a', DescriptionSchema.MaxTextLength + 1)));

        Assert.True(atLimit.Valid);
        Assert.False(overLimit.Valid);
        Assert.Contains("Mô tả không được vượt quá 20000 ký tự text.", overLimit.Errors);
    }

    [Fact]
    // Nút doc cộng 499 đoạn văn là 500 nút, vẫn hợp lệ. 500 đoạn văn là 501 nút, bị từ chối.
    public void Validate_EnforcesNodeCountBoundary()
    {
        var atLimit = DescriptionSchema.Validate(Paragraphs(DescriptionSchema.MaxNodeCount - 1));
        var overLimit = DescriptionSchema.Validate(Paragraphs(DescriptionSchema.MaxNodeCount));

        Assert.True(atLimit.Valid);
        Assert.False(overLimit.Valid);
        Assert.Contains("Mô tả không được vượt quá 500 nút.", overLimit.Errors);
    }

    [Fact]
    // Nút text cũng được đếm. 249 đoạn văn có text là 1 + 249 + 249 = 499 nút.
    public void Validate_CountsTextNodes()
    {
        Assert.True(DescriptionSchema.Validate(Paragraphs(249, "x")).Valid);
        Assert.False(DescriptionSchema.Validate(Paragraphs(250, "x")).Valid);
    }
}
