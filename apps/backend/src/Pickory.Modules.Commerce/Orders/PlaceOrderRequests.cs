using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Invoices;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Commerce.Orders;

// Liên hệ đã kiểm của người đặt. `Phone` đã chuẩn hóa về dạng `0xxxxxxxxx`.
public sealed record OrderContactInput(string Name, string Phone, string Email);

// Thông tin hóa đơn VAT đã kiểm. Cả 4 trường đều có giá trị.
public sealed record OrderInvoiceInput(string CompanyName, string TaxCode, string Address, string Email);

// Giá trị đã kiểm của body `POST /api/orders`. `ExpectedTotal` là tổng của preview mà khách đã xem.
public sealed record PlaceOrderInput(
    OrderContactInput Contact,
    CheckoutAddressInput Address,
    string? Note,
    string PaymentMethod,
    OrderInvoiceInput? Invoice,
    int ExpectedTotal);

// Đọc body của route đặt đơn theo luật chặt từng trường. Binder mặc định nhận kiểu sai, nên lớp này đọc JSON thủ công.
public static class PlaceOrderRequests
{
    public const string ContactField = "contact";
    public const string ContactNameField = "contact.name";
    public const string ContactPhoneField = "contact.phone";
    public const string ContactEmailField = "contact.email";
    public const string NoteField = "note";
    public const string InvoiceField = "invoice";
    public const string InvoiceCompanyNameField = "invoice.companyName";
    public const string InvoiceTaxCodeField = "invoice.taxCode";
    public const string InvoiceAddressField = "invoice.address";
    public const string InvoiceEmailField = "invoice.email";
    public const string ExpectedTotalField = "expectedTotal";

    public const int NoteMaxLength = 500;
    public const int EmailMaxLength = 254;
    public const int CompanyNameMaxLength = 200;
    public const int InvoiceAddressMaxLength = 300;

    private const string ContactProperty = "contact";
    private const string InvoiceProperty = "invoice";
    private const string NoteProperty = "note";
    private const string ExpectedTotalProperty = "expectedTotal";

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string ContactMessage = "Cần nhập thông tin liên hệ.";
    private const string NameRequiredMessage = "Cần nhập họ và tên.";
    private const string PhoneRequiredMessage = "Cần nhập số điện thoại.";
    private const string EmailRequiredMessage = "Cần nhập email.";
    private const string EmailInvalidMessage = "Email không đúng định dạng.";
    private const string NoteMessage = "Ghi chú là chuỗi tối đa 500 ký tự.";
    private const string InvoiceMessage = "Thông tin hóa đơn phải là một đối tượng.";
    private const string CompanyNameMessage = "Tên công ty là chuỗi không rỗng, tối đa 200 ký tự.";
    private const string TaxCodeMessage = "Mã số thuế gồm 10 hoặc 13 chữ số, có thể có dấu gạch ngang.";
    private const string InvoiceAddressMessage = "Địa chỉ hóa đơn là chuỗi không rỗng, tối đa 300 ký tự.";
    private const string ExpectedTotalMessage = "Tổng tiền dự kiến phải là số nguyên không âm.";

    // Ném `ValidationException` gom mọi lỗi của body. Route gọi hàm này trước khi đọc DB hay gọi shipping.
    public static PlaceOrderInput Read(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();

        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp. Lỗi gắn với cả body.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
            throw new ValidationException(failures);
        }

        // Trường lạ ở cấp body không bị từ chối. Số tiền do client gửi (như `total`) bị bỏ qua và không bao giờ được đọc.
        var contact = ReadContact(json, failures);

        // Địa chỉ và phương thức thanh toán dùng đúng hàm của preview, để hai route không lệch luật.
        var address = CheckoutPreviewRequests.ReadAddress(json, failures);
        var note = ReadNote(json, failures);
        var paymentMethod = CheckoutPreviewRequests.ReadPaymentMethod(json, failures);
        var invoice = ReadInvoice(json, failures);
        var expectedTotal = ReadExpectedTotal(json, failures);

        // Gom mọi lỗi vào một phản hồi, để UI gán lỗi vào đúng ô.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new PlaceOrderInput(contact, address, note, paymentMethod, invoice, expectedTotal);
    }

    private static OrderContactInput ReadContact(JsonElement json, List<ValidationFailure> failures)
    {
        var empty = new OrderContactInput(string.Empty, string.Empty, string.Empty);

        // Liên hệ phải là đối tượng. Thiếu thì không có tên, số điện thoại và email để giao hàng.
        if (!json.TryGetProperty(ContactProperty, out var contact) || contact.ValueKind != JsonValueKind.Object)
        {
            failures.Add(new ValidationFailure(ContactField, ContactMessage));
            return empty;
        }

        var name = ReadName(contact, failures);
        var phone = ReadPhone(contact, failures);
        var email = ReadEmail(contact, "email", ContactEmailField, failures);
        return new OrderContactInput(name, phone, email);
    }

    private static string ReadName(JsonElement contact, List<ValidationFailure> failures)
    {
        var name = ReadTrimmedString(contact, "name");
        if (string.IsNullOrEmpty(name))
        {
            failures.Add(new ValidationFailure(ContactNameField, NameRequiredMessage));
            return string.Empty;
        }

        // Luật dùng chung với hồ sơ và sổ địa chỉ. Tên trống đã bị chặn ở bước trên, vì luật chung chấp nhận tên trống.
        var error = ContactFieldRules.GetNameError(name);
        if (error is not null)
        {
            failures.Add(new ValidationFailure(ContactNameField, error));
            return string.Empty;
        }

        return name;
    }

    private static string ReadPhone(JsonElement contact, List<ValidationFailure> failures)
    {
        var phone = ReadTrimmedString(contact, "phone");
        if (string.IsNullOrEmpty(phone))
        {
            failures.Add(new ValidationFailure(ContactPhoneField, PhoneRequiredMessage));
            return string.Empty;
        }

        // Lưu số ở dạng chuẩn `0xxxxxxxxx`, vì shipping cần một dạng số duy nhất.
        var normalized = ContactFieldRules.NormalizePhone(phone);
        if (normalized is null)
        {
            failures.Add(new ValidationFailure(ContactPhoneField, ContactFieldRules.PhoneInvalidMessage));
            return string.Empty;
        }

        return normalized;
    }

    private static string ReadEmail(JsonElement parent, string property, string field, List<ValidationFailure> failures)
    {
        var email = ReadTrimmedString(parent, property);
        if (string.IsNullOrEmpty(email))
        {
            failures.Add(new ValidationFailure(field, EmailRequiredMessage));
            return string.Empty;
        }

        // Cùng luật email với identity. Độ dài tối đa theo giới hạn địa chỉ thư của RFC 5321.
        if (email.Length > EmailMaxLength || !EmailAddressRule.IsValid(email))
        {
            failures.Add(new ValidationFailure(field, EmailInvalidMessage));
            return string.Empty;
        }

        return email;
    }

    private static string? ReadNote(JsonElement json, List<ValidationFailure> failures)
    {
        // Ghi chú là tùy chọn. Thiếu hoặc `null` nghĩa là khách không ghi gì.
        if (!json.TryGetProperty(NoteProperty, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(NoteField, NoteMessage));
            return null;
        }

        // Cắt khoảng trắng trước khi kiểm độ dài. Ghi chú toàn khoảng trắng coi như không có ghi chú.
        var note = value.GetString()!.Trim();
        if (note.Length > NoteMaxLength)
        {
            failures.Add(new ValidationFailure(NoteField, NoteMessage));
            return null;
        }

        return note.Length == 0 ? null : note;
    }

    private static OrderInvoiceInput? ReadInvoice(JsonElement json, List<ValidationFailure> failures)
    {
        // Hóa đơn là tùy chọn. Thiếu hoặc `null` nghĩa là khách không xin hóa đơn VAT.
        if (!json.TryGetProperty(InvoiceProperty, out var invoice) || invoice.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (invoice.ValueKind != JsonValueKind.Object)
        {
            failures.Add(new ValidationFailure(InvoiceField, InvoiceMessage));
            return null;
        }

        // Đã xin hóa đơn thì cả 4 trường bắt buộc, vì shop không xuất được hóa đơn thiếu thông tin.
        var companyName = ReadBoundedText(invoice, "companyName", CompanyNameMaxLength, InvoiceCompanyNameField, CompanyNameMessage, failures);
        var taxCode = ReadTaxCode(invoice, failures);
        var address = ReadBoundedText(invoice, "address", InvoiceAddressMaxLength, InvoiceAddressField, InvoiceAddressMessage, failures);
        var email = ReadEmail(invoice, "email", InvoiceEmailField, failures);
        return new OrderInvoiceInput(companyName, taxCode, address, email);
    }

    private static string ReadTaxCode(JsonElement invoice, List<ValidationFailure> failures)
    {
        // Cắt khoảng trắng trước khi kiểm, vì `TaxCodeRule` không tự cắt.
        var taxCode = ReadTrimmedString(invoice, "taxCode");
        if (TaxCodeRule.IsValid(taxCode))
        {
            return taxCode!;
        }

        failures.Add(new ValidationFailure(InvoiceTaxCodeField, TaxCodeMessage));
        return string.Empty;
    }

    private static string ReadBoundedText(
        JsonElement parent, string property, int maxLength, string field, string message, List<ValidationFailure> failures)
    {
        var text = ReadTrimmedString(parent, property);
        if (!string.IsNullOrEmpty(text) && text.Length <= maxLength)
        {
            return text;
        }

        failures.Add(new ValidationFailure(field, message));
        return string.Empty;
    }

    private static int ReadExpectedTotal(JsonElement json, List<ValidationFailure> failures)
    {
        // Chỉ nhận số nguyên JSON không âm. Chuỗi, số thập phân và `null` không so được với tổng tiền của server.
        if (json.TryGetProperty(ExpectedTotalProperty, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var total)
            && total >= 0)
        {
            return total;
        }

        failures.Add(new ValidationFailure(ExpectedTotalField, ExpectedTotalMessage));
        return 0;
    }

    // Trả chuỗi đã cắt khoảng trắng, hoặc `null` khi thiếu trường hoặc trường không phải chuỗi.
    private static string? ReadTrimmedString(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!.Trim();
        }

        return null;
    }
}
