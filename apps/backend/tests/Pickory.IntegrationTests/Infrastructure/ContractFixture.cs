using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// So response thật của route với ví dụ trong `spec/fixtures`. Helper so status, tập khóa và kiểu giá trị, không so giá trị cụ thể.
public static class ContractFixture
{
    private const string RootLocation = "(gốc của body)";

    // Trả `Task` vì phải đọc body bằng `await`. Test phải `await` hàm này, nếu không lỗi sẽ bị bỏ sót.
    public static async Task AssertMatches(string fixtureFile, string exampleName, HttpResponseMessage response)
    {
        var example = FixtureFiles.FindExample(fixtureFile, exampleName);
        await AssertExampleMatches(fixtureFile, example, response);
    }

    internal static async Task AssertExampleMatches(string fixtureFile, JsonObject example, HttpResponseMessage response)
    {
        var context = FixtureContext.For(fixtureFile, example);

        // Ví dụ file Excel không có `response`. Nếu để qua, helper chỉ so status và báo xanh giả.
        if (example["workbook"] is not null)
        {
            throw new XunitException(
                $"Ví dụ \"{context.ExampleName}\" trong `{fixtureFile}` là ví dụ file Excel vì có khóa `workbook`. Dùng `WorkbookFixture` cho ví dụ này.");
        }

        var expectedStatus = context.RequireInt(example["status"], "khóa `status`");
        var mismatches = new FixtureMismatches();

        // So status trước. Status sai thường kéo theo body sai, nhưng người sửa vẫn cần thấy cả hai.
        var actualStatus = (int)response.StatusCode;
        if (expectedStatus != actualStatus)
        {
            mismatches.Add("status", $"mong đợi {expectedStatus}, thực tế {actualStatus}");
        }

        // Ví dụ không có `response` (hoặc `null`) không mô tả body. Bỏ qua phần body.
        var expectedBody = example["response"];
        if (expectedBody is not null)
        {
            var bodyText = await response.Content.ReadAsStringAsync();
            CompareBody(expectedBody, bodyText, context, mismatches);
        }

        mismatches.ThrowIfAny($"Response không khớp ví dụ \"{context.ExampleName}\" trong `{fixtureFile}`:");
    }

    private static void CompareBody(JsonNode expectedBody, string bodyText, FixtureContext context, FixtureMismatches mismatches)
    {
        // Dựng hình dạng fixture trước khi đọc body. Fixture sai thì dừng ngay, kể cả khi body của route cũng hỏng.
        var expectedShape = JsonShape.FromNode(expectedBody, string.Empty, context);

        // Body không phải JSON là lỗi của route. Báo rõ thay vì để bộ đọc JSON ném lỗi khó hiểu.
        JsonNode? actualBody;
        try
        {
            actualBody = JsonNode.Parse(bodyText);
        }
        catch (JsonException exception)
        {
            mismatches.Add("body", $"không phải JSON hợp lệ ({exception.Message})");
            return;
        }

        // Quy tắc "null khớp mọi kiểu" chỉ dành cho trường con. Body gốc là `null` mà fixture có dữ liệu là lỗi của route.
        if (actualBody is null && expectedShape.Kind != JsonShapeKind.Null)
        {
            mismatches.Add(RootLocation, $"sai kiểu, mong đợi {JsonShape.KindName(expectedShape.Kind)}, thực tế null");
            return;
        }

        CompareNode(expectedShape, actualBody, string.Empty, mismatches);
    }

    private static void CompareNode(JsonShape expected, JsonNode? actual, string path, FixtureMismatches mismatches)
    {
        var actualKind = JsonShape.KindOf(actual);

        // `null` ở một bên khớp mọi kiểu. Trường tùy chọn có thể rỗng ở response mà vẫn có giá trị trong fixture.
        if (expected.Kind == JsonShapeKind.Null || actualKind == JsonShapeKind.Null)
        {
            return;
        }

        // Khác kiểu thì dừng ở nút này. So khóa con của hai kiểu khác nhau không có nghĩa.
        if (expected.Kind != actualKind)
        {
            mismatches.Add(
                Describe(path),
                $"sai kiểu, mong đợi {JsonShape.KindName(expected.Kind)}, thực tế {JsonShape.KindName(actualKind)}");
            return;
        }

        switch (expected.Kind)
        {
            case JsonShapeKind.Object:
                CompareObject(expected, actual!.AsObject(), path, mismatches);
                break;
            case JsonShapeKind.Array:
                CompareArray(expected, actual!.AsArray(), path, mismatches);
                break;
        }
    }

    private static void CompareObject(JsonShape expected, JsonObject actual, string path, FixtureMismatches mismatches)
    {
        // Khóa có trong fixture mà response không có là thiếu khóa. Route bỏ sót dữ liệu của contract.
        foreach (var expectedProperty in expected.Properties)
        {
            if (!actual.ContainsKey(expectedProperty.Key))
            {
                mismatches.Add(JsonShape.JoinKey(path, expectedProperty.Key), "thiếu khóa");
            }
        }

        foreach (var actualProperty in actual)
        {
            var propertyPath = JsonShape.JoinKey(path, actualProperty.Key);

            // Khóa có trong response mà fixture không có là thừa khóa. Route lộ dữ liệu ngoài contract.
            if (!expected.Properties.TryGetValue(actualProperty.Key, out var expectedProperty))
            {
                mismatches.Add(propertyPath, "thừa khóa");
                continue;
            }

            CompareNode(expectedProperty, actualProperty.Value, propertyPath, mismatches);
        }
    }

    private static void CompareArray(JsonShape expected, JsonArray actual, string path, FixtureMismatches mismatches)
    {
        // Mảng fixture rỗng không cho biết hình dạng phần tử. Không có gì để so.
        if (expected.Element is null)
        {
            return;
        }

        // Mỗi phần tử response so với hình dạng chung của fixture. Đường dẫn dùng chỉ số thật của response.
        for (var index = 0; index < actual.Count; index++)
        {
            CompareNode(expected.Element, actual[index], $"{path}[{index}]", mismatches);
        }
    }

    private static string Describe(string path)
    {
        return path.Length == 0 ? RootLocation : path;
    }
}
