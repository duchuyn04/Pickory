using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.SupportDesk;

// Bộ kiểm hợp đồng dùng chung cho route support-desk. Mỗi task có route mới so response thật với một ví dụ.
// Phép so chỉ kiểm khóa và kiểu giá trị, vì mã định danh và thời điểm khác nhau mỗi lần chạy.
public static class SupportDeskContract
{
    private const string FixtureFolderName = "support-desk";

    // Đọc mảng ví dụ của một file mẫu. Tên file tính từ `spec/fixtures/support-desk/`.
    public static JsonArray ReadExamples(string fixtureFileName)
    {
        // Đọc file mẫu để lấy mốc so. Mọi phép so của helper dựa trên dữ liệu trong file này.
        var fixture = ReadFixtureFile(fixtureFileName);
        // File chỉ có `events` không có response HTTP, nên thiếu `examples` là lỗi cấu hình test.
        if (fixture["examples"] is not JsonArray examples)
        {
            throw new InvalidOperationException($"File mẫu \"{fixtureFileName}\" không có mảng `examples`.");
        }

        return examples;
    }

    // Liệt kê tên file mẫu có `examples`. Hai file sự kiện không thuộc phép so response nên tự bị bỏ qua.
    public static IReadOnlyList<string> ReadExampleFileNames()
    {
        var fixtureDirectory = FindFixtureDirectory();
        // Bộ dữ liệu mẫu để chung một thư mục, nên chỉ cần lấy các file JSON trong đó.
        var jsonFileNames = fixtureDirectory.EnumerateFiles("*.json").Select(file => file.Name);
        // Chỉ file có `examples` mới có response mẫu để so. File sự kiện chỉ có `events`.
        var exampleFileNames = jsonFileNames.Where(fileName => ReadFixtureFile(fileName)["examples"] is JsonArray);
        // Thứ tự theo tên giữ danh sách test giống nhau ở mọi lần chạy và máy khác nhau.
        return exampleFileNames.OrderBy(fileName => fileName, StringComparer.Ordinal).ToList();
    }

    // So response thật với ví dụ cùng tên trong file mẫu.
    public static void AssertMatches(
        JsonNode? actualResponse, HttpStatusCode actualStatus, string fixtureFileName, string exampleName)
    {
        // Tìm ví dụ theo tên để lấy cả response mẫu và mã HTTP mẫu của chính ví dụ đó.
        var example = FindExample(fixtureFileName, exampleName);
        // Ví dụ thiếu `status` là lỗi dữ liệu mẫu, nên báo ngay thay vì so với một mã mặc định.
        var expectedStatus = example["status"]?.GetValue<int>()
            ?? throw new InvalidOperationException($"Ví dụ \"{exampleName}\" không có `status`.");

        var differences = new List<string>();
        // Mã HTTP là một phần của hợp đồng. Sai mã nghĩa là client đọc nhầm nhánh thành công hoặc nhánh lỗi.
        if ((int)actualStatus != expectedStatus)
        {
            differences.Add($"mã HTTP: response trả {(int)actualStatus}, ví dụ ghi {expectedStatus}");
        }

        // Đường dẫn khóa bắt đầu rỗng ở gốc response. Mỗi khóa con nối thêm một đoạn.
        CollectShapeDifferences(example["response"], actualResponse, string.Empty, differences);
        ThrowIfDifferencesExist(differences, fixtureFileName, exampleName);
    }

    private static void ThrowIfDifferencesExist(
        List<string> differences, string fixtureFileName, string exampleName)
    {
        if (differences.Count == 0)
        {
            return;
        }

        // Đánh dấu từng khác biệt bằng gạch đầu dòng để đọc nhanh danh sách lỗi.
        var detailLines = string.Join(Environment.NewLine, differences.Select(difference => $"- {difference}"));
        // Gom mọi lỗi vào một thông báo, để người sửa thấy đủ khóa sai trong một lần chạy.
        throw new XunitException(
            $"Response không khớp hợp đồng \"{fixtureFileName}\", ví dụ \"{exampleName}\":{Environment.NewLine}{detailLines}");
    }

    // So dạng dữ liệu của response với ví dụ theo đường dẫn khóa, để thông báo lỗi chỉ đúng chỗ sai.
    private static void CollectShapeDifferences(
        JsonNode? expected, JsonNode? actual, string path, List<string> differences)
    {
        // Giá trị `null` ở một bên khớp mọi kiểu ở bên kia, nên không so sâu hơn phần này.
        if (IsJsonNull(expected) || IsJsonNull(actual))
        {
            return;
        }

        // Đọc kiểu của hai bên trước, vì mọi nhánh bên dưới chọn theo kiểu của ví dụ.
        var expectedKind = expected!.GetValueKind();
        var actualKind = actual!.GetValueKind();
        // `true` và `false` cùng là boolean, nên chỉ hai kiểu này được coi là khớp nhau.
        if (!IsSameShapeKind(expectedKind, actualKind))
        {
            // Gốc response không có khóa cha, nên thông báo dùng chữ `response` thay đường dẫn rỗng.
            var location = path.Length == 0 ? "response" : path;
            differences.Add(
                $"{location}: response trả kiểu {ReadKindName(actualKind)}, ví dụ ghi kiểu {ReadKindName(expectedKind)}");
            return;
        }

        if (expectedKind == JsonValueKind.Object)
        {
            CollectObjectDifferences(expected.AsObject(), actual.AsObject(), path, differences);
            return;
        }

        if (expectedKind == JsonValueKind.Array)
        {
            CollectArrayDifferences(expected.AsArray(), actual.AsArray(), path, differences);
        }
    }

    private static void CollectObjectDifferences(
        JsonObject expected, JsonObject actual, string path, List<string> differences)
    {
        foreach (var property in expected)
        {
            // Nối khóa con vào đường dẫn để thông báo lỗi chỉ đúng vị trí trong response.
            var childPath = JoinKey(path, property.Key);
            // Thiếu khóa nghĩa là response bỏ trường mà hợp đồng yêu cầu, nên frontend không đọc được trường đó.
            if (!actual.ContainsKey(property.Key))
            {
                differences.Add($"{childPath}: thiếu khóa trong response");
                continue;
            }

            // So tiếp phần giá trị khi khóa có ở cả hai bên, vì chỗ sai kiểu nằm trong giá trị.
            CollectShapeDifferences(property.Value, actual[property.Key], childPath, differences);
        }

        foreach (var property in actual)
        {
            // Tập khóa phải chính xác. Khóa thừa nghĩa là backend trả trường không có trong hợp đồng.
            if (!expected.ContainsKey(property.Key))
            {
                differences.Add($"{JoinKey(path, property.Key)}: thừa khóa so với ví dụ");
            }
        }
    }

    private static void CollectArrayDifferences(
        JsonArray expected, JsonArray actual, string path, List<string> differences)
    {
        // Ví dụ không mô tả phần tử nào thì không có mẫu để so, nên chỉ kiểu mảng được kiểm ở trên.
        if (expected.Count == 0)
        {
            return;
        }

        // Phần tử đầu là mẫu cho mọi phần tử thật, vì ví dụ không gắn dạng dữ liệu với một vị trí cụ thể.
        var template = expected[0];
        for (var index = 0; index < actual.Count; index++)
        {
            // Ghi chỉ số phần tử vào đường dẫn để thông báo lỗi chỉ đúng phần tử sai.
            CollectShapeDifferences(template, actual[index], $"{path}[{index}]", differences);
        }
    }

    private static bool IsJsonNull(JsonNode? node)
    {
        return node is null || node.GetValueKind() == JsonValueKind.Null;
    }

    private static bool IsSameShapeKind(JsonValueKind expected, JsonValueKind actual)
    {
        if (expected == actual)
        {
            return true;
        }

        // `true` và `false` là hai giá trị của cùng kiểu boolean. Mọi số JSON dùng chung kiểu number.
        return IsBoolean(expected) && IsBoolean(actual);
    }

    private static bool IsBoolean(JsonValueKind kind)
    {
        return kind is JsonValueKind.True or JsonValueKind.False;
    }

    private static string ReadKindName(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "mảng",
            JsonValueKind.String => "chuỗi",
            JsonValueKind.Number => "số",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => "không xác định",
        };
    }

    private static string JoinKey(string path, string key)
    {
        // Gốc response không có khóa cha, nên đường dẫn bắt đầu bằng chính khóa con.
        return path.Length == 0 ? key : $"{path}.{key}";
    }

    private static JsonObject FindExample(string fixtureFileName, string exampleName)
    {
        // Đọc mọi ví dụ của file để chọn đúng tên ví dụ. Hợp đồng dùng tên ví dụ làm khóa tra cứu duy nhất.
        var example = ReadExamples(fixtureFileName)
            .SingleOrDefault(item => item?["name"]?.GetValue<string>() == exampleName);
        // Tên sai nghĩa là test gọi helper sai hợp đồng, nên báo rõ file và tên đã tìm.
        if (example is null)
        {
            throw new InvalidOperationException($"File mẫu \"{fixtureFileName}\" không có ví dụ \"{exampleName}\".");
        }

        // Ví dụ là object chứa `response` và `status`, nên đổi sang `JsonObject` để đọc khóa con.
        return example.AsObject();
    }

    private static JsonNode ReadFixtureFile(string fixtureFileName)
    {
        // Nối tên file với thư mục bộ dữ liệu mẫu, để đường dẫn không phụ thuộc thư mục chạy test.
        var path = Path.Combine(FindFixtureDirectory().FullName, fixtureFileName);
        // Thiếu file là lỗi cấu hình test, nên thông báo ghi đường dẫn đã tìm.
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Không tìm thấy file mẫu \"{path}\".", path);
        }

        // Phân tích nội dung thành cây JSON để so khóa và kiểu, không so chuỗi thô.
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static DirectoryInfo FindFixtureDirectory()
    {
        // Đi ngược từ thư mục build lên gốc repo. Gốc repo là thư mục có `spec/fixtures/support-desk`.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            // Thử dựng đường dẫn cố định của bộ dữ liệu mẫu dưới từng thư mục cha.
            var candidate = new DirectoryInfo(Path.Combine(directory.FullName, "spec", "fixtures", FixtureFolderName));
            if (candidate.Exists)
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Không tìm thấy thư mục `spec/fixtures/{FixtureFolderName}` tính từ `{AppContext.BaseDirectory}`.");
    }
}
