using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Features.Inventory;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Inventory;

// CAT-08: kiểm body điều chỉnh tồn kho bằng tay.
public sealed class AdjustStockRequestValidatorTests
{
    private readonly AdjustStockRequestValidator _validator = new();

    [Fact]
    public void Valid_ManualAdjustment_Passes()
    {
        Assert.True(_validator.Validate(new AdjustStockRequest(-3, "Kiểm kê lệch", "manual")).IsValid);
    }

    [Fact]
    public void Valid_TypeOmitted_Passes()
    {
        Assert.True(_validator.Validate(new AdjustStockRequest(3, "Nhập thêm", null)).IsValid);
    }

    [Fact]
    public void Valid_ReturnWithPositiveDelta_Passes()
    {
        Assert.True(_validator.Validate(new AdjustStockRequest(1, "Khách trả hàng", "return")).IsValid);
    }

    [Fact]
    // Điều chỉnh 0 cái không đổi tồn kho nên không có lý do để ghi movement.
    public void Invalid_ZeroDelta_Fails()
    {
        Assert.False(_validator.Validate(new AdjustStockRequest(0, "Lý do", "manual")).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_BlankReason_Fails(string? reason)
    {
        Assert.False(_validator.Validate(new AdjustStockRequest(1, reason, "manual")).IsValid);
    }

    [Fact]
    public void Reason_AtLimit_PassesAndOverLimit_Fails()
    {
        Assert.True(_validator.Validate(new AdjustStockRequest(1, new string('a', 500), "manual")).IsValid);
        Assert.False(_validator.Validate(new AdjustStockRequest(1, new string('a', 501), "manual")).IsValid);
    }

    [Fact]
    // Độ dài tính sau khi bỏ khoảng trắng hai đầu, vì handler lưu lý do đã cắt.
    public void Reason_LengthIsMeasuredAfterTrim()
    {
        Assert.True(_validator.Validate(new AdjustStockRequest(1, " " + new string('a', 500) + " ", "manual")).IsValid);
    }

    [Theory]
    [InlineData("order_reserve")]
    [InlineData("order_release")]
    [InlineData("MANUAL")]
    [InlineData("")]
    // Chỉ `manual` và `return` dùng cho điều chỉnh tay. Loại của đơn hàng chỉ do `IStockService` ghi.
    public void Invalid_UnknownType_Fails(string type)
    {
        Assert.False(_validator.Validate(new AdjustStockRequest(1, "Lý do", type)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    // Trả hàng là nhập kho lại, nên số lượng phải dương.
    public void Invalid_ReturnWithNonPositiveDelta_Fails(int delta)
    {
        Assert.False(_validator.Validate(new AdjustStockRequest(delta, "Khách trả", "return")).IsValid);
    }

    [Fact]
    public void TypeOf_DefaultsToManual()
    {
        Assert.Equal(InventoryMovementType.Manual, new AdjustStockRequest(1, "x", null).MovementType);
        Assert.Equal(InventoryMovementType.Return, new AdjustStockRequest(1, "x", "return").MovementType);
    }
}
