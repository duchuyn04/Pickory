using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pickory.Shared.Json;

// Chuyển đổi DateTime sang chuẩn ISO 8601 UTC có đúng 3 chữ số mili giây và đuôi Z.
public class DateTimeUtcJsonConverter : JsonConverter<DateTime>
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringValue = reader.GetString();
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return default;
        }

        // Chuyển chuỗi đầu vào về thời gian UTC chuẩn.
        return DateTime.Parse(
            stringValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Coi Unspecified là UTC từ cơ sở dữ liệu, chỉ đổi múi giờ khi là Local.
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utcValue.ToString(DateFormat, CultureInfo.InvariantCulture));
    }
}

// Chuyển đổi DateTimeOffset sang chuẩn ISO 8601 UTC có đúng 3 chữ số mili giây và đuôi Z.
public class DateTimeOffsetUtcJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringValue = reader.GetString();
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return default;
        }

        // Chuyển chuỗi đầu vào về mốc thời gian UTC chuẩn.
        return DateTimeOffset.Parse(
            stringValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        // Đồng bộ mốc thời gian sang UTC trước khi xuất chuỗi.
        var utcValue = value.ToUniversalTime();
        writer.WriteStringValue(utcValue.ToString(DateFormat, CultureInfo.InvariantCulture));
    }
}
