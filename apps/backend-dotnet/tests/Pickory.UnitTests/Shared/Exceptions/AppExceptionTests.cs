using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Shared.Exceptions;

// Kiểm tra khởi tạo đối tượng ngoại lệ nghiệp vụ và các thuộc tính tương ứng.
public class AppExceptionTests
{
    [Fact]
    // Lưu trữ chính xác mã lỗi, mã trạng thái HTTP và thông điệp tiếng Việt.
    public void KhoiTaoAppException_LuuDungCacThuocTinhTruyenVao()
    {
        // Khởi tạo đối tượng ngoại lệ với các tham số nghiệp vụ cụ thể.
        var exception = new AppException("PRODUCT_NOT_FOUND", 404, "Không tìm thấy sản phẩm.");

        // Khẳng định các giá trị được gán chính xác vào thuộc tính của ngoại lệ.
        Assert.Equal("PRODUCT_NOT_FOUND", exception.Code);
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("Không tìm thấy sản phẩm.", exception.Message);
    }
}
