using System.Net;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-C1: bộ kiểm hợp đồng so response thật với ví dụ trong `spec/fixtures/support-desk/`.
// Test đọc dữ liệu mẫu trực tiếp, nên không chép response vào test.
public sealed class SupportDeskContractTests
{
    // Tạo một case cho mỗi ví dụ của mọi file mẫu có `examples`.
    public static TheoryData<string, string> EveryExample()
    {
        var theoryData = new TheoryData<string, string>();
        // Chỉ file có `examples` mới có response mẫu để tự khớp. File sự kiện không có gì để so.
        foreach (var fixtureFileName in SupportDeskContract.ReadExampleFileNames())
        {
            // Đọc mảng ví dụ của file để lấy tên từng ví dụ. Mỗi ví dụ thành một case riêng.
            foreach (var example in SupportDeskContract.ReadExamples(fixtureFileName))
            {
                // Ghép tên file và tên ví dụ làm khóa của case, để test tìm lại đúng ví dụ đó.
                theoryData.Add(fixtureFileName, example!["name"]!.GetValue<string>());
            }
        }

        return theoryData;
    }

    [Theory]
    [MemberData(nameof(EveryExample))]
    public void EveryExample_MatchesItsOwnResponse(string fixtureFileName, string exampleName)
    {
        // Ví dụ so với chính nó. Mọi dạng dữ liệu của hợp đồng phải khớp, gồm `null`, boolean, số và mảng.
        AssertContractMatches(ReadResponseCopy(fixtureFileName, exampleName), fixtureFileName, exampleName);
    }

    [Fact]
    public void AssertMatches_MissingSessionStatus_ReportsKeyPath()
    {
        const string fixtureFileName = "customer-api.json";
        const string exampleName = "mở lại hội thoại đang có phiên chờ";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        // Backend bỏ khóa bắt buộc khỏi response, mô phỏng lỗi hợp đồng thường gặp.
        response["data"]!["session"]!.AsObject().Remove("status");

        var message = AssertContractViolation(response, fixtureFileName, exampleName);

        // Thông báo phải ghi đường dẫn khóa để người sửa tìm đúng trường trong response.
        Assert.Contains("data.session.status", message);
        Assert.Contains("thiếu khóa", message);
    }

    [Fact]
    public void AssertMatches_ExtraSessionKey_ReportsKeyPath()
    {
        const string fixtureFileName = "customer-api.json";
        const string exampleName = "mở lại hội thoại đang có phiên chờ";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        // Backend trả thêm khóa ngoài hợp đồng. Frontend sẽ không đọc được khóa này.
        response["data"]!["session"]!.AsObject()["note"] = "Ghi chú nội bộ";

        var message = AssertContractViolation(response, fixtureFileName, exampleName);

        Assert.Contains("data.session.note", message);
        Assert.Contains("thừa khóa", message);
    }

    [Fact]
    public void AssertMatches_SessionStatusWrongKind_ReportsKeyPath()
    {
        const string fixtureFileName = "customer-api.json";
        const string exampleName = "mở lại hội thoại đang có phiên chờ";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        // Ví dụ ghi `status` kiểu chuỗi. Response thật trả số nghĩa là frontend đọc sai dạng.
        response["data"]!["session"]!.AsObject()["status"] = 7;

        var message = AssertContractViolation(response, fixtureFileName, exampleName);

        Assert.Contains("data.session.status", message);
        Assert.Contains("kiểu", message);
    }

    [Fact]
    public void AssertMatches_ExampleNull_AcceptsAnyActualKind()
    {
        const string fixtureFileName = "admin-customer-panel.json";
        const string exampleName = "khách vãng lai";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        // Đi tới `data.customer` để sửa đúng khóa mà test này cần kiểm.
        var customer = response["data"]!["customer"]!.AsObject();
        // Ví dụ để `name` và `email` bằng `null`, nên response thật trả kiểu gì cũng khớp.
        customer["name"] = 12345;
        customer["email"] = new JsonObject { ["value"] = "minhanh@example.com" };

        AssertContractMatches(response, fixtureFileName, exampleName);

        // Đối chứng: khóa không `null` vẫn so kiểu. Luật `null` không được che phép so.
        customer["isGuest"] = "false";
        var message = AssertContractViolation(response, fixtureFileName, exampleName);
        Assert.Contains("data.customer.isGuest", message);
    }

    [Fact]
    public void AssertMatches_ActualNull_AcceptsExampleKind()
    {
        const string fixtureFileName = "admin-customer-panel.json";
        const string exampleName = "khách đã đăng nhập";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        // Đi tới `data.customer` để sửa đúng khóa mà test này cần kiểm.
        var customer = response["data"]!["customer"]!.AsObject();
        // Ví dụ ghi `name` và `email` kiểu chuỗi. Response thật trả `null` thì vẫn khớp hợp đồng.
        customer["name"] = null;
        customer["email"] = null;

        AssertContractMatches(response, fixtureFileName, exampleName);

        // Đối chứng: `isGuest` sai kiểu vẫn bị bắt, nên nhánh `null` chỉ bỏ so sâu ở đúng khóa đó.
        customer["isGuest"] = "false";
        var message = AssertContractViolation(response, fixtureFileName, exampleName);
        Assert.Contains("data.customer.isGuest", message);
    }

    [Fact]
    public void AssertMatches_ArrayElementWrongKindAfterFirst_ReportsElementPath()
    {
        const string fixtureFileName = "admin-callbacks.json";
        const string exampleName = "danh sách yêu cầu gọi lại";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        var callbacks = response["data"]!.AsArray();
        // Kiểm điều kiện của ví dụ trước. Mẫu thiếu phần tử thứ hai thì test báo rõ lý do thay vì lỗi chỉ số.
        Assert.True(callbacks.Count > 1, "Ví dụ phải có ít nhất hai phần tử để kiểm phần tử không đứng đầu.");
        // Phần tử thứ hai sai kiểu. Helper phải so mọi phần tử thật theo mẫu phần tử đầu của ví dụ.
        callbacks[1]!["status"] = 1;

        var message = AssertContractViolation(response, fixtureFileName, exampleName);

        Assert.Contains("data[1].status", message);
    }

    [Fact]
    public void AssertMatches_SecondArrayElementMissingKey_ReportsElementPath()
    {
        const string fixtureFileName = "admin-customer-panel.json";
        const string exampleName = "khách đã đăng nhập";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        var orders = response["data"]!["orders"]!.AsArray();
        // Response thật có thêm đơn thứ hai. Đơn này thiếu `code`, nên phép so theo mẫu phải bắt.
        orders.Add(new JsonObject
        {
            ["status"] = "shipping",
            ["total"] = 120000,
            ["itemCount"] = 1,
            ["createdAt"] = "2026-11-02T03:00:00Z",
        });

        var message = AssertContractViolation(response, fixtureFileName, exampleName);

        Assert.Contains("data.orders[1].code", message);
    }

    [Fact]
    public void AssertMatches_EmptyExampleArray_IgnoresActualElements()
    {
        const string fixtureFileName = "admin-customer-panel.json";
        const string exampleName = "khách vãng lai";
        var response = ReadResponseCopy(fixtureFileName, exampleName);
        // Đi tới `data.orders` để thêm phần tử khác dạng vào bản sao của mảng rỗng.
        var orders = response["data"]!["orders"]!.AsArray();
        // Ví dụ phải để mảng rỗng, nếu không test sẽ kiểm nhầm nhánh so phần tử.
        Assert.Empty(orders);
        // Ví dụ không mô tả phần tử nào, nên response thật có dữ liệu khác dạng vẫn khớp.
        orders.Add(new JsonObject { ["không"] = "theo mẫu" });
        orders.Add(7);

        AssertContractMatches(response, fixtureFileName, exampleName);

        // Đối chứng: chính `orders` sai kiểu mảng vẫn bị bắt, nên nhánh bỏ qua chỉ áp cho phần tử.
        response["data"]!["orders"] = "không phải mảng";
        var message = AssertContractViolation(response, fixtureFileName, exampleName);
        Assert.Contains("data.orders", message);
    }

    [Fact]
    public void AssertMatches_HttpStatusDiffers_ReportsStatus()
    {
        const string fixtureFileName = "admin-sessions.json";
        const string exampleName = "nhận phiên";
        var response = ReadResponseCopy(fixtureFileName, exampleName);

        // Phần thân khớp ví dụ, nên lỗi duy nhất còn lại là mã HTTP của response.
        var message = AssertContractViolation(response, HttpStatusCode.BadRequest, fixtureFileName, exampleName);

        Assert.Contains("mã HTTP", message);
        Assert.Contains("400", message);
        Assert.Contains("201", message);
    }

    // Đòi helper khớp hoàn toàn với ví dụ, dùng mã HTTP của chính ví dụ đó.
    private static void AssertContractMatches(JsonNode response, string fixtureFileName, string exampleName)
    {
        SupportDeskContract.AssertMatches(
            response, ReadStatus(fixtureFileName, exampleName), fixtureFileName, exampleName);
    }

    // Đòi helper báo lỗi hợp đồng và giữ thông báo lại để test kiểm đường dẫn khóa.
    private static string AssertContractViolation(JsonNode response, string fixtureFileName, string exampleName)
    {
        return AssertContractViolation(
            response, ReadStatus(fixtureFileName, exampleName), fixtureFileName, exampleName);
    }

    // Đòi helper báo lỗi hợp đồng với mã HTTP chỉ định của response thật.
    private static string AssertContractViolation(
        JsonNode response, HttpStatusCode actualStatus, string fixtureFileName, string exampleName)
    {
        // Giữ thông báo của lỗi xunit lại, để test kiểm đường dẫn khóa trong đó.
        return Assert.Throws<XunitException>(() => SupportDeskContract.AssertMatches(
            response, actualStatus, fixtureFileName, exampleName)).Message;
    }

    // Tìm ví dụ theo tên để test lấy dữ liệu và mã HTTP của chính ví dụ đó.
    private static JsonObject FindExample(string fixtureFileName, string exampleName)
    {
        // Chọn ví dụ theo tên, vì tên ví dụ là khóa tra cứu của hợp đồng.
        var example = SupportDeskContract.ReadExamples(fixtureFileName)
            .Single(item => item!["name"]!.GetValue<string>() == exampleName);
        // Đổi sang `JsonObject` để đọc khóa `response` và `status` theo tên.
        return example!.AsObject();
    }

    private static JsonNode ReadResponseCopy(string fixtureFileName, string exampleName)
    {
        // Đọc ví dụ để lấy đúng phần `response`. Phần `request` không thuộc phép so dạng dữ liệu.
        var example = FindExample(fixtureFileName, exampleName);
        // Trả bản sao tách khỏi cây ví dụ. Test sửa bản sao, không phụ thuộc việc đọc lại file ở mỗi lần gọi.
        return example["response"]!.DeepClone();
    }

    private static HttpStatusCode ReadStatus(string fixtureFileName, string exampleName)
    {
        // Ví dụ ghi mã HTTP dạng số JSON, nên phải đổi sang `HttpStatusCode` để so.
        var exampleStatus = FindExample(fixtureFileName, exampleName)["status"]!.GetValue<int>();
        return (HttpStatusCode)exampleStatus;
    }
}
