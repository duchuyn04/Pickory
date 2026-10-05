using System.Text.Json;
using Pickory.Shared.Json;
using Xunit;

namespace Pickory.UnitTests;

// Kiểm tra quy tắc định dạng thời gian ISO 8601 UTC của bộ chuyển đổi JSON.
public class DateTimeJsonConverterTests
{
    private record SampleDateRecord(DateTime CreatedAt, DateTimeOffset UpdatedAt);

    [Fact]
    // Xuất thời gian đúng 3 chữ số phần giây lẻ kèm đuôi Z theo chuẩn ISO 8601 UTC.
    public void DateTime_DuocDinhDangChuanIso8601Utc_VoiBaChuSoMiliGiayVaDuoiZ()
    {
        // Khởi tạo thời điểm xác định có đầy đủ thành phần ngày giờ.
        var utcDate = new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc);
        var utcOffset = new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.Zero);
        var record = new SampleDateRecord(utcDate, utcOffset);

        // Tuần tự hóa đối tượng sang chuỗi JSON theo cấu hình chuẩn.
        var json = JsonSerializer.Serialize(record, PickoryJsonDefaults.Options);

        // Khẳng định định dạng chuỗi chứa đúng 3 chữ số mili giây và ký tự Z.
        Assert.Contains("\"createdAt\":\"2026-10-05T07:00:00.000Z\"", json);
        Assert.Contains("\"updatedAt\":\"2026-10-05T08:30:00.000Z\"", json);
    }

    [Fact]
    // Chuyển đổi ngược lại từ chuỗi JSON bảo toàn chính xác giá trị thời gian ban đầu.
    public void DateTime_GiaiTuanTuHoaChinhXac_KhiNhanChuoiIso8601Utc()
    {
        // Chuỗi JSON mẫu đầu vào đại diện cho dữ liệu gửi lên từ client.
        var json = "{\"createdAt\":\"2026-10-05T07:00:00.000Z\",\"updatedAt\":\"2026-10-05T08:30:00.000Z\"}";

        // Giải tuần tự hóa chuỗi thành đối tượng strongly typed.
        var result = JsonSerializer.Deserialize<SampleDateRecord>(json, PickoryJsonDefaults.Options);

        // Khẳng định giá trị thời gian khớp chính xác với mốc UTC tương ứng.
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc), result.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.Zero), result.UpdatedAt);
    }
}
