using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// Test của chính `ContractFixture`. Test không dùng database, `Collection` chỉ để qua luật kiến trúc.
[Collection(CommerceCollection.Name)]
public sealed class ContractFixtureTests
{
    private const string InvoicesFixture = "exports-printing/order-invoices.json";
    private const string ExportsFixture = "exports-printing/exports.json";
    private const string TwoOrdersExample = "order invoices two orders with vat and cancelled";
    private const string ShopNotSetExample = "order invoices shop profile not set";

    private static HttpResponseMessage JsonResponse(int status, string body)
    {
        return new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)status,
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    // Dựng response từ chính `response` của ví dụ. Hàm `change` sửa bản sao để tạo từng lỗi cần thử.
    private static HttpResponseMessage ResponseFromExample(string fixtureFile, string exampleName, Action<JsonNode>? change = null)
    {
        var example = FixtureFiles.FindExample(fixtureFile, exampleName);
        var body = example["response"]!.DeepClone();
        change?.Invoke(body);
        return JsonResponse(example["status"]!.GetValue<int>(), body.ToJsonString());
    }

    // Ví dụ dựng tay trong test. Cần khi hình dạng thử không có trong fixtures thật.
    private static JsonObject InlineExample(string responseJson, int status = 200)
    {
        return (JsonObject)JsonNode.Parse($$"""{ "name": "inline", "status": {{status}}, "response": {{responseJson}} }""")!;
    }

    private static JsonArray Invoices(JsonNode body)
    {
        return body["data"]!["invoices"]!.AsArray();
    }

    private static async Task<string> FailureMessage(Task assertion)
    {
        var exception = await Assert.ThrowsAnyAsync<XunitException>(() => assertion);
        return exception.Message;
    }

    [Fact]
    // Tên ví dụ sai là lỗi của người viết test. Thông báo nêu tên đã tìm và các tên có trong file.
    public async Task UnknownExampleName_FailsWithSearchedNameAndAvailableNames()
    {
        using var response = new HttpResponseMessage();

        var message = await FailureMessage(ContractFixture.AssertMatches(InvoicesFixture, "khong co vi du nay", response));

        Assert.Contains("khong co vi du nay", message, StringComparison.Ordinal);
        Assert.Contains("order invoices empty codes", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InvoicesFixture, TwoOrdersExample)]
    [InlineData(InvoicesFixture, ShopNotSetExample)]
    [InlineData(InvoicesFixture, "order invoices empty codes")]
    [InlineData(InvoicesFixture, "order invoices unknown code")]
    [InlineData(ExportsFixture, "orders export invalid filter")]
    // Đường xanh: response dựng từ chính ví dụ phải khớp. Nếu đường này đỏ thì helper sai, không phải code route.
    public async Task ResponseBuiltFromSameExample_Matches(string fixtureFile, string exampleName)
    {
        using var response = ResponseFromExample(fixtureFile, exampleName);

        await ContractFixture.AssertMatches(fixtureFile, exampleName, response);
    }

    [Fact]
    // Ví dụ lỗi 422 của route xuất khớp response 422 viết tay bằng JSON thô.
    public async Task OrdersExportTooLarge_MatchesHandWritten422Response()
    {
        using var response = JsonResponse(
            422,
            """{ "error": { "code": "EXPORT_TOO_LARGE", "message": "Danh sách có hơn 50.000 dòng." } }""");

        await ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response);
    }

    [Fact]
    // Response sai status thì báo cả status mong đợi và status thực tế.
    public async Task WrongStatus_FailsWithExpectedAndActualStatus()
    {
        using var response = JsonResponse(
            200,
            """{ "error": { "code": "EXPORT_TOO_LARGE", "message": "x" } }""");

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response));

        Assert.Contains("status", message, StringComparison.Ordinal);
        Assert.Contains("422", message, StringComparison.Ordinal);
        Assert.Contains("200", message, StringComparison.Ordinal);
    }

    [Fact]
    // Thiếu khóa ở hóa đơn đầu thì thông báo nêu đúng đường dẫn có chỉ số mảng.
    public async Task MissingKey_FailsWithIndexedKeyPath()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body => Invoices(body)[0]!.AsObject().Remove("vatInvoice"));

        var message = await FailureMessage(ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response));

        Assert.Contains("thiếu khóa", message, StringComparison.Ordinal);
        Assert.Contains("data.invoices[0].vatInvoice", message, StringComparison.Ordinal);
    }

    [Fact]
    // Thừa khóa là dữ liệu route lộ thêm ngoài contract. Thông báo nêu đường dẫn của khóa thừa.
    public async Task ExtraKey_FailsWithKeyPath()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body => Invoices(body)[0]!["customer"]!.AsObject().Add("internalNote", "x"));

        var message = await FailureMessage(ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response));

        Assert.Contains("thừa khóa", message, StringComparison.Ordinal);
        Assert.Contains("data.invoices[0].customer.internalNote", message, StringComparison.Ordinal);
    }

    [Fact]
    // Số trong fixture mà chuỗi trong response là lỗi kiểu. Thông báo nêu kiểu mong đợi và kiểu thực tế.
    public async Task NumberInFixture_StringInResponse_FailsWithBothTypes()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body => Invoices(body)[0]!["total"] = "25370000");

        var message = await FailureMessage(ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response));

        Assert.Contains("data.invoices[0].total", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi số", message, StringComparison.Ordinal);
        Assert.Contains("thực tế chuỗi", message, StringComparison.Ordinal);
    }

    [Fact]
    // Helper không so giá trị cụ thể. Cùng kiểu số nhưng giá trị khác vẫn khớp.
    public async Task DifferentValueOfSameType_Matches()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body => Invoices(body)[0]!["total"] = 1);

        await ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response);
    }

    [Fact]
    // `null` ở response khớp mọi kiểu của fixture. Trường tùy chọn của route được phép rỗng.
    public async Task NullInResponse_MatchesAnyFixtureType()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body =>
        {
            Invoices(body)[0]!["total"] = null;
            Invoices(body)[0]!["customer"] = null;
            Invoices(body)[0]!["lines"] = null;
        });

        await ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response);
    }

    [Fact]
    // `null` ở fixture khớp mọi kiểu của response. Fixture `shop` toàn `null` vẫn nhận chuỗi và object thật.
    public async Task NullInFixture_MatchesAnyResponseType()
    {
        using var response = ResponseFromExample(InvoicesFixture, ShopNotSetExample, body =>
        {
            body["data"]!["shop"]!["name"] = "Pickory";
            Invoices(body)[0]!["vatInvoice"] = JsonNode.Parse("""{ "bất kỳ": 1 }""");
        });

        await ContractFixture.AssertMatches(InvoicesFixture, ShopNotSetExample, response);
    }

    [Fact]
    // Fixture có hai hóa đơn: `vatInvoice` là object ở hóa đơn đầu và `null` ở hóa đơn sau. Response đổi thứ tự hai hóa đơn vẫn khớp.
    public async Task ArrayElementsWithObjectAndNullInFixture_MatchInEitherOrder()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body =>
        {
            var invoices = Invoices(body);
            var first = invoices[0]!.DeepClone();
            invoices[0] = invoices[1]!.DeepClone();
            invoices[1] = first;
            invoices.Add(first.DeepClone());
        });

        await ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response);
    }

    [Fact]
    // Hình dạng chung lấy từ mọi hóa đơn của fixture. Hóa đơn sau của response phải có đủ khóa của `vatInvoice`, dù hóa đơn sau của fixture có `null`.
    public async Task LaterResponseElement_IsComparedWithMergedShape()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body =>
            Invoices(body)[1]!["vatInvoice"] = JsonNode.Parse("""{ "companyName": "A", "address": "B", "email": "C" }"""));

        var message = await FailureMessage(ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response));

        Assert.Contains("thiếu khóa", message, StringComparison.Ordinal);
        Assert.Contains("data.invoices[1].vatInvoice.taxCode", message, StringComparison.Ordinal);
    }

    [Fact]
    // Phần tử đầu của fixture có `null`, phần tử sau có kiểu thật. Hình dạng chung lấy kiểu thật nên response sai kiểu phải đỏ.
    public async Task NullInFirstFixtureElement_RealTypeInLaterElement_IsStillChecked()
    {
        var example = InlineExample("""{ "items": [ { "owner": null }, { "owner": { "age": 1 } } ] }""");
        using var response = JsonResponse(200, """{ "items": [ { "owner": { "age": "mười" } } ] }""");

        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, response));

        Assert.Contains("items[0].owner.age", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi số", message, StringComparison.Ordinal);
    }

    [Fact]
    // Response mảng rỗng khớp mọi mảng. Route không có dòng dữ liệu vẫn trả đúng hình dạng.
    public async Task EmptyResponseArray_Matches()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body => body["data"]!["invoices"] = new JsonArray());

        await ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response);
    }

    [Fact]
    // Mảng fixture rỗng không nói gì về phần tử. Helper không kiểm phần tử của response.
    public async Task EmptyFixtureArray_SkipsResponseElements()
    {
        var example = InlineExample("""{ "items": [] }""");
        using var response = JsonResponse(200, """{ "items": [ 1, "hai", { "ba": 3 } ] }""");

        await ContractFixture.AssertExampleMatches("inline.json", example, response);
    }

    [Fact]
    // Ví dụ không có `response` thì helper chỉ so status. Body của response không bị đọc.
    public async Task ExampleWithoutResponse_ComparesStatusOnly()
    {
        var example = (JsonObject)JsonNode.Parse("""{ "name": "inline", "status": 204 }""")!;
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        await ContractFixture.AssertExampleMatches("inline.json", example, response);
    }

    [Fact]
    // Body không phải JSON là lỗi của route. Thông báo nói rõ thay vì ném lỗi của bộ đọc JSON.
    public async Task BodyThatIsNotJson_FailsWithClearMessage()
    {
        using var response = JsonResponse(422, "<html>lỗi</html>");

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, "orders export too large", response));

        Assert.Contains("không phải JSON", message, StringComparison.Ordinal);
    }

    [Fact]
    // Helper gom mọi lỗi của một lần so. Người sửa route thấy hết lỗi trong một lần chạy.
    public async Task SeveralMismatches_AreReportedTogether()
    {
        using var response = ResponseFromExample(InvoicesFixture, TwoOrdersExample, body =>
        {
            Invoices(body)[0]!.AsObject().Remove("vatInvoice");
            Invoices(body)[0]!["customer"]!.AsObject().Add("internalNote", "x");
            Invoices(body)[1]!["total"] = "6020000";
        });

        var message = await FailureMessage(ContractFixture.AssertMatches(InvoicesFixture, TwoOrdersExample, response));

        Assert.Contains("data.invoices[0].vatInvoice", message, StringComparison.Ordinal);
        Assert.Contains("data.invoices[0].customer.internalNote", message, StringComparison.Ordinal);
        Assert.Contains("data.invoices[1].total", message, StringComparison.Ordinal);
    }

    private const string TooLargeExample = "orders export too large";

    [Fact]
    // Body gốc `null` không được khớp fixture có object. Quy tắc "null khớp mọi kiểu" chỉ dành cho trường con.
    public async Task RootBodyNull_FailsWhenFixtureIsObject()
    {
        using var response = JsonResponse(422, "null");

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, TooLargeExample, response));

        Assert.Contains("(gốc của body)", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi object, thực tế null", message, StringComparison.Ordinal);
    }

    [Fact]
    // Body gốc là mảng trong khi fixture là object thì báo sai kiểu ở gốc.
    public async Task RootBodyArray_FailsWhenFixtureIsObject()
    {
        using var response = JsonResponse(422, "[]");

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, TooLargeExample, response));

        Assert.Contains("(gốc của body)", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi object, thực tế mảng", message, StringComparison.Ordinal);
    }

    [Fact]
    // Body gốc là object trong khi fixture là mảng thì báo sai kiểu ở gốc.
    public async Task RootBodyObject_FailsWhenFixtureIsArray()
    {
        var example = InlineExample("[ { \"id\": 1 } ]");
        using var response = JsonResponse(200, """{ "id": 1 }""");

        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, response));

        Assert.Contains("(gốc của body)", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi mảng, thực tế object", message, StringComparison.Ordinal);
    }

    [Fact]
    // Body `{}` khi fixture có khóa thì báo thiếu từng khóa. Object rỗng không phải `null`.
    public async Task RootBodyEmptyObject_FailsWithMissingKeys()
    {
        using var response = JsonResponse(422, "{}");

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, TooLargeExample, response));

        Assert.Contains("error: thiếu khóa", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Body rỗng hoặc chỉ có khoảng trắng không phải JSON. Route quên ghi body bị bắt ở đây.
    public async Task EmptyBody_FailsAsNotJson(string body)
    {
        using var response = JsonResponse(422, body);

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, TooLargeExample, response));

        Assert.Contains("không phải JSON", message, StringComparison.Ordinal);
    }

    [Fact]
    // Ví dụ file Excel không có `response`. Helper từ chối thay vì chỉ so status và báo xanh giả.
    public async Task WorkbookExample_FailsAndPointsToWorkbookFixture()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, "orders export success", response));

        Assert.Contains("là ví dụ file Excel", message, StringComparison.Ordinal);
        Assert.Contains("WorkbookFixture", message, StringComparison.Ordinal);
    }

    [Fact]
    // Hai phần tử mảng fixture có hai kiểu thật khác nhau là fixture sai. Thông báo dùng tên kiểu tiếng Việt, không dùng tên enum.
    public async Task ArrayWithConflictingTypesInFixture_FailsAsFixtureFault()
    {
        var example = InlineExample("""{ "items": [ 1, "a" ] }""");
        using var response = JsonResponse(200, """{ "items": [] }""");

        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, response));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", message, StringComparison.Ordinal);
        Assert.Contains("items[]", message, StringComparison.Ordinal);
        Assert.Contains("số và chuỗi", message, StringComparison.Ordinal);
    }

    [Fact]
    // Fixture sai được báo riêng, không lẫn lỗi của route. Status sai của response không xuất hiện trong thông báo.
    public async Task FixtureFault_IsNotMixedWithRouteMismatches()
    {
        var example = InlineExample("""{ "items": [ 1, "a" ] }""");
        using var response = JsonResponse(500, """{ "items": [] }""");

        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, response));

        Assert.StartsWith("Fixture sai", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Response không khớp", message, StringComparison.Ordinal);
    }

    [Fact]
    // Fixture thiếu `status` thì báo tiếng Việt, không để ngoại lệ thô của JSON lọt ra.
    public async Task FixtureWithoutStatus_FailsAsFixtureFault()
    {
        var example = (JsonObject)JsonNode.Parse("""{ "name": "inline", "response": {} }""")!;
        using var response = JsonResponse(200, "{}");

        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, response));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", message, StringComparison.Ordinal);
        Assert.Contains("khóa `status`", message, StringComparison.Ordinal);
    }

    [Fact]
    // Số nguyên và số thực cùng là kiểu số. Helper không phân biệt hai dạng này.
    public async Task IntegerInFixture_FractionInResponse_Matches()
    {
        var example = InlineExample("""{ "price": 100 }""");
        using var response = JsonResponse(200, """{ "price": 99.5 }""");

        await ContractFixture.AssertExampleMatches("inline.json", example, response);
    }

    [Fact]
    // Kiểu true/false khớp cả `true` và `false`. Chuỗi thay cho true/false bị báo kèm tên kiểu.
    public async Task BooleanInFixture_ChecksTypeNotValue()
    {
        var example = InlineExample("""{ "isPaid": true }""");
        using var sameTypeResponse = JsonResponse(200, """{ "isPaid": false }""");
        using var wrongTypeResponse = JsonResponse(200, """{ "isPaid": "false" }""");

        await ContractFixture.AssertExampleMatches("inline.json", example, sameTypeResponse);
        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, wrongTypeResponse));

        Assert.Contains("isPaid", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi true/false", message, StringComparison.Ordinal);
        Assert.Contains("thực tế chuỗi", message, StringComparison.Ordinal);
    }

    [Fact]
    // Status sai và body sai xuất hiện cùng lúc trong một thông báo.
    public async Task WrongStatus_AndBodyMismatch_AreReportedTogether()
    {
        using var response = JsonResponse(200, """{ "error": { "code": "EXPORT_TOO_LARGE" } }""");

        var message = await FailureMessage(ContractFixture.AssertMatches(ExportsFixture, TooLargeExample, response));

        Assert.Contains("status: mong đợi 422, thực tế 200", message, StringComparison.Ordinal);
        Assert.Contains("error.message: thiếu khóa", message, StringComparison.Ordinal);
    }

    [Fact]
    // Route trả HTML cộng fixture tự mâu thuẫn: người đọc phải thấy "Fixture sai" trước, không chỉ lỗi JSON của route.
    public async Task FixtureFault_IsReportedBeforeNotJsonBody()
    {
        var example = InlineExample("""{ "items": [ 1, "a" ] }""");
        using var response = JsonResponse(200, "<html>lỗi</html>");

        var message = await FailureMessage(ContractFixture.AssertExampleMatches("inline.json", example, response));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", message, StringComparison.Ordinal);
    }

    [Fact]
    // File fixture không phải JSON là fixture sai. Thông báo không phải ngoại lệ thô của bộ đọc JSON.
    public void FixtureFileThatIsNotJson_FailsAsFixtureFault()
    {
        var exception = Assert.ThrowsAny<XunitException>(() => FixtureFiles.ParseText("hong.json", "{ không phải json"));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("hong.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("không phải JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    // File fixture chỉ chứa `null` là fixture sai, không phải `NullReferenceException`.
    public void FixtureFileWithOnlyNull_FailsAsFixtureFault()
    {
        var exception = Assert.ThrowsAny<XunitException>(() => FixtureFiles.ParseText("rong.json", "null"));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("chỉ chứa null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    // Gốc file fixture là mảng là fixture sai, không phải `InvalidOperationException`.
    public void FixtureFileWithArrayRoot_FailsAsFixtureFault()
    {
        var root = FixtureFiles.ParseText("mang.json", "[]");

        var exception = Assert.ThrowsAny<XunitException>(() => FixtureFiles.FindExampleIn("mang.json", root, "bat ky"));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("gốc của file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    // Hai ví dụ trùng tên là fixture sai. Helper không lấy ví dụ đầu mà không báo.
    public void DuplicateExampleName_FailsAsFixtureFault()
    {
        var root = FixtureFiles.ParseText(
            "trung.json",
            """{ "examples": [ { "name": "a", "status": 200 }, { "name": "a", "status": 404 } ] }""");

        var exception = Assert.ThrowsAny<XunitException>(() => FixtureFiles.FindExampleIn("trung.json", root, "a"));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("2 ví dụ cùng tên \"a\"", exception.Message, StringComparison.Ordinal);
    }
}
