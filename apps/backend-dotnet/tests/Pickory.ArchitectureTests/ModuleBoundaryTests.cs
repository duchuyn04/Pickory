using NetArchTest.Rules;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Identity;
using Xunit;

namespace Pickory.ArchitectureTests;

// Lớp mẫu cố tình phụ thuộc vào module khác để chứng minh test kiến trúc bắt được vi phạm.
public class SampleViolatingClass
{
    // Tham chiếu trực tiếp tới module Identity để tạo ra một vi phạm ranh giới mẫu.
    public IdentityModule? DirectDependency { get; set; }
}

// Kiểm tra toàn vẹn ranh giới kiến trúc độc lập giữa các module của hệ thống.
public class ModuleBoundaryTests
{
    [Fact]
    // Module Identity không được phụ thuộc trực tiếp vào module AiGateway hoặc Catalog.
    public void IdentityModule_KhongPhuThuocTrucTiep_VaoCacModuleKhac()
    {
        // Kiểm tra toàn bộ kiểu dữ liệu bên trong assembly của module Identity.
        var result = Types.InAssembly(typeof(IdentityModule).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Pickory.Modules.AiGateway", "Pickory.Modules.Catalog")
            .GetResult();

        // Khẳng định không có vi phạm ranh giới nào tồn tại.
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    // Module AiGateway không được phụ thuộc trực tiếp vào module Identity hoặc Catalog.
    public void AiGatewayModule_KhongPhuThuocTrucTiep_VaoCacModuleKhac()
    {
        // Kiểm tra toàn bộ kiểu dữ liệu bên trong assembly của module AiGateway.
        var result = Types.InAssembly(typeof(AiGatewayModule).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Pickory.Modules.Identity", "Pickory.Modules.Catalog")
            .GetResult();

        // Khẳng định không có vi phạm ranh giới nào tồn tại.
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    // Module Catalog không được phụ thuộc trực tiếp vào module Identity hoặc AiGateway.
    public void CatalogModule_KhongPhuThuocTrucTiep_VaoCacModuleKhac()
    {
        // Kiểm tra toàn bộ kiểu dữ liệu bên trong assembly của module Catalog.
        var result = Types.InAssembly(typeof(CatalogModule).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Pickory.Modules.Identity", "Pickory.Modules.AiGateway")
            .GetResult();

        // Khẳng định không có vi phạm ranh giới nào tồn tại.
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    // Kiểm tra chứng minh NetArchTest bắt được vi phạm khi có phụ thuộc không hợp lệ.
    public void NetArchTest_PhatHienVaChanViPham_KhiCoLopPhuThuocTraiPhep()
    {
        // Áp dụng quy tắc cấm phụ thuộc vào module Identity lên lớp vi phạm mẫu.
        var result = Types.InAssembly(typeof(ModuleBoundaryTests).Assembly)
            .That()
            .HaveName(nameof(SampleViolatingClass))
            .ShouldNot()
            .HaveDependencyOn("Pickory.Modules.Identity")
            .GetResult();

        // Khẳng định quy tắc báo cáo thất bại và chỉ đích danh lớp vi phạm.
        Assert.False(result.IsSuccessful);
        Assert.Contains(typeof(SampleViolatingClass).FullName, result.FailingTypeNames);
    }
}
