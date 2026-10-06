using Pickory.Modules.Catalog.Products;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Port từ `domain/variants.test.ts` của bản Node (CAT-02).
public sealed class VariantCombinationsTests
{
    private static Dictionary<string, string> Combo(params (string Axis, string Value)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Axis, pair => pair.Value);
    }

    [Fact]
    // Hai trục có 2 giá trị mỗi trục sinh đúng 4 tổ hợp theo thứ tự tích Descartes.
    public void Build_TwoAxesGiveFourCombinations()
    {
        var combinations = VariantCombinations.Build(
        [
            new ProductOption("Dung lượng", ["128GB", "256GB"]),
            new ProductOption("Màu", ["Đen", "Trắng"]),
        ]);

        Assert.Equal(
            [
                Combo(("Dung lượng", "128GB"), ("Màu", "Đen")),
                Combo(("Dung lượng", "128GB"), ("Màu", "Trắng")),
                Combo(("Dung lượng", "256GB"), ("Màu", "Đen")),
                Combo(("Dung lượng", "256GB"), ("Màu", "Trắng")),
            ],
            combinations);
    }

    [Fact]
    // Sản phẩm không có trục tùy chọn vẫn có đúng một biến thể mặc định.
    public void Build_NoAxisGivesOneDefaultVariant()
    {
        var combination = Assert.Single(VariantCombinations.Build([]));

        Assert.Empty(combination);
    }

    [Fact]
    // Một trục có 3 giá trị sinh 3 tổ hợp.
    public void Build_OneAxis()
    {
        var combinations = VariantCombinations.Build([new ProductOption("Màu", ["Đen", "Trắng", "Xanh"])]);

        Assert.Equal([Combo(("Màu", "Đen")), Combo(("Màu", "Trắng")), Combo(("Màu", "Xanh"))], combinations);
    }

    [Fact]
    // Ba trục có 2 giá trị mỗi trục sinh 8 tổ hợp.
    public void Build_ThreeAxesGiveEightCombinations()
    {
        var combinations = VariantCombinations.Build(
        [
            new ProductOption("RAM", ["8GB", "16GB"]),
            new ProductOption("Ổ cứng", ["256GB", "512GB"]),
            new ProductOption("Màu", ["Đen", "Bạc"]),
        ]);

        Assert.Equal(8, combinations.Count);
    }

    [Fact]
    // Bốn trục bị từ chối. Số tổ hợp tăng quá nhanh khi có nhiều trục.
    public void Build_RejectsFourAxes()
    {
        var error = Assert.Throws<AppException>(() => VariantCombinations.Build(
        [
            new ProductOption("Trục 1", ["A", "B"]),
            new ProductOption("Trục 2", ["C", "D"]),
            new ProductOption("Trục 3", ["E", "F"]),
            new ProductOption("Trục 4", ["G", "H"]),
        ]));

        Assert.Contains("tối đa 3 trục", error.Message);
    }

    [Fact]
    // Tên trục rỗng hoặc trùng nhau sau khi cắt khoảng trắng bị từ chối.
    public void Build_RejectsBlankOrDuplicateAxisNames()
    {
        var blank = Assert.Throws<AppException>(() => VariantCombinations.Build([new ProductOption("   ", ["A", "B"])]));
        var duplicate = Assert.Throws<AppException>(() => VariantCombinations.Build(
        [
            new ProductOption("Màu sắc", ["Đen"]),
            new ProductOption(" Màu sắc ", ["Trắng"]),
        ]));

        Assert.Contains("Tên trục tùy chọn không được để trống", blank.Message);
        Assert.Contains("trùng lặp", duplicate.Message);
    }

    [Fact]
    // Giá trị rỗng hoặc trùng nhau trong một trục bị từ chối.
    public void Build_RejectsBlankOrDuplicateValues()
    {
        var blank = Assert.Throws<AppException>(() => VariantCombinations.Build([new ProductOption("Màu", ["Đen", "   "])]));
        var duplicate = Assert.Throws<AppException>(() => VariantCombinations.Build([new ProductOption("Màu", ["Đen", " Đen "])]));

        Assert.Contains("không được để trống", blank.Message);
        Assert.Contains("trùng lặp", duplicate.Message);
    }

    [Fact]
    // Quá 100 tổ hợp bị từ chối, kể cả khi chỉ có 2 trục.
    public void Build_RejectsMoreThanHundredCombinations()
    {
        Assert.Equal(100, VariantCombinations.MaxCombinations);

        var error = Assert.Throws<AppException>(() => VariantCombinations.Build(
        [
            new ProductOption("Trục 1", Enumerable.Range(0, 11).Select(index => $"Val1_{index}").ToList()),
            new ProductOption("Trục 2", Enumerable.Range(0, 10).Select(index => $"Val2_{index}").ToList()),
        ]));

        Assert.Contains("vượt quá giới hạn tối đa 100", error.Message);
    }

    [Fact]
    // Trục có đúng 100 tổ hợp vẫn hợp lệ.
    public void Build_AcceptsExactlyHundredCombinations()
    {
        var combinations = VariantCombinations.Build(
        [
            new ProductOption("Trục 1", Enumerable.Range(0, 10).Select(index => $"Val1_{index}").ToList()),
            new ProductOption("Trục 2", Enumerable.Range(0, 10).Select(index => $"Val2_{index}").ToList()),
        ]);

        Assert.Equal(100, combinations.Count);
    }
}
