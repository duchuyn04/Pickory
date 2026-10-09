using System.Text.Json.Nodes;
using Pickory.Modules.Catalog.Descriptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Descriptions;

// Port phần chuyển đổi text của `description.test.ts` ở bản Node (CAT-13).
public sealed class DescriptionTextTests
{
    [Fact]
    // Text thuần giữ thứ tự khối, mỗi khối cách nhau một dòng trống, và bỏ mark.
    public void ToPlainText_KeepsOrderAndDropsMarks()
    {
        var doc = JsonNode.Parse("""
            {
              "type": "doc",
              "content": [
                { "type": "heading", "attrs": { "level": 1 }, "content": [{ "type": "text", "text": "Tiêu đề sản phẩm" }] },
                {
                  "type": "paragraph",
                  "content": [
                    { "type": "text", "text": "Đoạn một có " },
                    { "type": "text", "text": "chữ đậm", "marks": [{ "type": "bold" }] },
                    { "type": "text", "text": " và " },
                    { "type": "text", "text": "chữ nghiêng", "marks": [{ "type": "italic" }] },
                    { "type": "text", "text": "." }
                  ]
                },
                { "type": "heading", "attrs": { "level": 2 }, "content": [{ "type": "text", "text": "Thông số nổi bật" }] },
                { "type": "paragraph", "content": [{ "type": "text", "text": "Đoạn kết thúc." }] }
              ]
            }
            """)!;

        var plainText = DescriptionText.ToPlainText(doc);

        Assert.Equal("Tiêu đề sản phẩm\n\nĐoạn một có chữ đậm và chữ nghiêng.\n\nThông số nổi bật\n\nĐoạn kết thúc.", plainText);
    }

    [Fact]
    // Tài liệu chỉ có đoạn văn trống cho chuỗi rỗng, không sinh khoảng trắng thừa.
    public void ToPlainText_EmptyDocument()
    {
        Assert.Equal(string.Empty, DescriptionText.ToPlainText(JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}""")!));
    }

    [Fact]
    // Text thường tách đoạn theo dòng trống. Kết quả qua được schema.
    public void FromPlainText_SplitsParagraphsOnBlankLines()
    {
        var doc = DescriptionText.FromPlainText("Đoạn một\n\nĐoạn hai có nhiều câu.\n\n\r\n  \nĐoạn ba");

        Assert.True(DescriptionSchema.Validate(doc).Valid);
        var content = doc["content"]!.AsArray();
        Assert.Equal(3, content.Count);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"type":"paragraph","content":[{"type":"text","text":"Đoạn một"}]}"""),
            content[0]));
    }

    [Fact]
    // Chuỗi chỉ có khoảng trắng cho một tài liệu rỗng hợp lệ.
    public void FromPlainText_BlankTextGivesEmptyDocument()
    {
        var doc = DescriptionText.FromPlainText("   ");

        Assert.True(DescriptionSchema.Validate(doc).Valid);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}"""), doc));
    }

    [Fact]
    // Đổi text sang tài liệu rồi đổi lại giữ nguyên toàn bộ nội dung.
    public void RoundTrip_KeepsText()
    {
        const string original = "Dòng đầu tiên\n\nDòng thứ hai tiếp theo\n\nDòng kết";

        Assert.Equal(original, DescriptionText.ToPlainText(DescriptionText.FromPlainText(original)));
    }

    [Fact]
    // Text vượt 20.000 ký tự bị từ chối với thông báo của schema.
    public void FromPlainText_RejectsTooLongText()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => DescriptionText.FromPlainText(new string('a', DescriptionSchema.MaxTextLength + 1)));

        Assert.Equal("Mô tả không được vượt quá 20000 ký tự text.", error.Message);
    }

    [Fact]
    // 500 đoạn văn vượt giới hạn 500 nút, nên bị từ chối.
    public void FromPlainText_RejectsTooManyParagraphs()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 500).Select(index => $"Đoạn {index}"));

        var error = Assert.Throws<InvalidOperationException>(() => DescriptionText.FromPlainText(text));

        Assert.Equal("Mô tả không được vượt quá 500 nút.", error.Message);
    }

    [Fact]
    // Text đúng 20.000 ký tự và 100 đoạn văn đều nằm trong giới hạn.
    public void FromPlainText_AcceptsTextWithinLimits()
    {
        var atTextLimit = DescriptionText.FromPlainText(new string('a', DescriptionSchema.MaxTextLength));
        var hundredParagraphs = DescriptionText.FromPlainText(string.Join("\n\n", Enumerable.Range(1, 100).Select(index => $"Đoạn {index}")));

        Assert.True(DescriptionSchema.Validate(atTextLimit).Valid);
        Assert.True(DescriptionSchema.Validate(hundredParagraphs).Valid);
    }
}
