using System.Reflection;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Commerce;
using Pickory.Modules.Identity;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Shipping;
using Xunit;

// Namespace giả lập phải mang tên `Contracts` của module Identity. Tên này cố ý khác thư mục của project test.
#pragma warning disable IDE0130
namespace Pickory.Modules.Identity.Contracts
{
    // Kiểu dữ liệu giả lập thuộc hợp đồng công khai của module Identity.
    public class SampleIdentityContract
    {
    }
}
#pragma warning restore IDE0130

namespace Pickory.ArchitectureTests
{
    // Lớp mẫu cố tình phụ thuộc vào module khác để chứng minh test kiến trúc bắt được vi phạm.
    public class SampleViolatingClass
    {
        // Tham chiếu trực tiếp tới module Identity để tạo ra một vi phạm ranh giới mẫu.
        public IdentityDbContext? DirectDependency { get; set; }
    }

    // Lớp mẫu phụ thuộc vào hợp đồng công khai của module khác theo đúng thiết kế.
    public class SampleAllowedContractConsumer
    {
        // Tham chiếu tới kiểu dữ liệu thuộc namespace Contracts được phép sử dụng.
        public Pickory.Modules.Identity.Contracts.SampleIdentityContract? ContractDependency { get; set; }
    }

    // Kiểm tra toàn vẹn ranh giới kiến trúc độc lập giữa các module của hệ thống.
    public class ModuleBoundaryTests
    {
        // Danh sách các assembly module đã biết để bảo đảm được nạp vào AppDomain.
        private static readonly Assembly[] KnownModules =
        [
            typeof(IdentityModule).Assembly,
            typeof(AiGatewayModule).Assembly,
            typeof(CatalogModule).Assembly,
            typeof(ShippingModule).Assembly,
            typeof(CommerceModule).Assembly,
        ];

        [Fact]
        // Hàm dùng chung duyệt toàn bộ module và xác nhận không có module nào vi phạm ranh giới.
        public void TatCaModule_TuanThuQuyUocKienTruc_KhongPhuThuocNoiBoModuleKhac()
        {
            // Lấy danh sách toàn bộ module thực thi hiện có trong hệ thống.
            var moduleAssemblies = ModuleArchitectureValidator.GetModuleAssemblies();
            Assert.NotEmpty(moduleAssemblies);

            foreach (var assembly in moduleAssemblies)
            {
                // Kiểm tra từng module qua hàm kiểm tra dùng chung duy nhất.
                var result = ModuleArchitectureValidator.ValidateModule(assembly);

                // Khẳng định module thỏa mãn toàn bộ quy tắc ranh giới kiến trúc.
                Assert.True(
                    result.IsSuccessful,
                    $"Module '{assembly.GetName().Name}' có vi phạm: {string.Join("; ", result.Violations)}");
            }
        }

        [Fact]
        // Hàm dùng chung phát hiện và báo lỗi khi có kiểu dữ liệu phụ thuộc trái phép vào module khác.
        public void HamKiemTraDungChung_BaoLoiViPham_KhiCoLopPhuThuocNoiBoModuleKhac()
        {
            // Áp dụng đúng hàm dùng chung lên kiểu vi phạm với giả định thuộc module Catalog.
            var result = ModuleArchitectureValidator.ValidateType(typeof(SampleViolatingClass), "Pickory.Modules.Catalog");

            // Khẳng định hàm kiểm tra dùng chung phát hiện đúng vi phạm và chỉ rõ kiểu vi phạm.
            Assert.False(result.IsSuccessful);
            Assert.NotEmpty(result.Violations);
            Assert.Contains(result.Violations, v => v.Contains(nameof(SampleViolatingClass)));
        }

        [Fact]
        // Shipping chỉ được thấy Contracts của module khác. Kiểu nội bộ của Identity hoặc Catalog phải bị chặn.
        public void HamKiemTraDungChung_BaoLoiViPham_KhiLopShippingPhuThuocNoiBoModuleKhac()
        {
            // Áp dụng đúng hàm dùng chung lên kiểu vi phạm với giả định thuộc module Shipping.
            var result = ModuleArchitectureValidator.ValidateType(typeof(SampleViolatingClass), "Pickory.Modules.Shipping");

            // Khẳng định luật biên áp dụng cho Shipping như cho các module khác.
            Assert.False(result.IsSuccessful);
            Assert.Contains(result.Violations, v => v.Contains(nameof(SampleViolatingClass)));
        }

        [Fact]
        // Shipping được thấy Contracts của module khác, giống mọi module.
        public void HamKiemTraDungChung_ChoPhepShipping_PhuThuocNamespaceContractsCuaModuleKhac()
        {
            var result = ModuleArchitectureValidator.ValidateType(typeof(SampleAllowedContractConsumer), "Pickory.Modules.Shipping");

            Assert.True(result.IsSuccessful);
            Assert.Empty(result.Violations);
        }

        [Fact]
        // Assembly Shipping thật phải có trong danh sách module mà test duyệt, nếu không luật biên không bao giờ kiểm nó.
        public void DanhSachModule_GomAssemblyShipping_VaKhongVoiCacModuleKhac()
        {
            var moduleNames = ModuleArchitectureValidator.GetModuleAssemblies().Select(assembly => assembly.GetName().Name).ToList();

            Assert.Contains("Pickory.Modules.Shipping", moduleNames);
            Assert.DoesNotContain("Pickory.Modules.Shipping.Contracts", moduleNames);
            var shippingReferences = typeof(ShippingModule).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(name => name.StartsWith("Pickory.", StringComparison.Ordinal))
                .ToList();
            Assert.All(shippingReferences, name => Assert.True(
                name == "Pickory.Shared" || name.EndsWith(".Contracts", StringComparison.Ordinal),
                $"Shipping không được tham chiếu {name}."));
        }

        [Fact]
        // Commerce chỉ được thấy Contracts của module khác. Kiểu nội bộ của Identity, Catalog hoặc Shipping phải bị chặn.
        public void HamKiemTraDungChung_BaoLoiViPham_KhiLopCommercePhuThuocNoiBoModuleKhac()
        {
            var result = ModuleArchitectureValidator.ValidateType(typeof(SampleViolatingClass), "Pickory.Modules.Commerce");

            Assert.False(result.IsSuccessful);
            Assert.Contains(result.Violations, v => v.Contains(nameof(SampleViolatingClass)));
        }

        [Fact]
        // Commerce được thấy Contracts của module khác, giống mọi module.
        public void HamKiemTraDungChung_ChoPhepCommerce_PhuThuocNamespaceContractsCuaModuleKhac()
        {
            var result = ModuleArchitectureValidator.ValidateType(typeof(SampleAllowedContractConsumer), "Pickory.Modules.Commerce");

            Assert.True(result.IsSuccessful);
            Assert.Empty(result.Violations);
        }

        [Fact]
        // Assembly Commerce thật phải có trong danh sách module mà test duyệt, và chỉ tham chiếu Shared hoặc Contracts.
        public void DanhSachModule_GomAssemblyCommerce_VaChiThamChieuSharedHoacContracts()
        {
            var moduleNames = ModuleArchitectureValidator.GetModuleAssemblies().Select(assembly => assembly.GetName().Name).ToList();

            Assert.Contains("Pickory.Modules.Commerce", moduleNames);
            Assert.DoesNotContain("Pickory.Modules.Commerce.Contracts", moduleNames);
            var commerceReferences = typeof(CommerceModule).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(name => name.StartsWith("Pickory.", StringComparison.Ordinal))
                .ToList();
            Assert.All(commerceReferences, name => Assert.True(
                name == "Pickory.Shared" || name.EndsWith(".Contracts", StringComparison.Ordinal),
                $"Commerce không được tham chiếu {name}."));
        }

        [Fact]
        // Hợp đồng của Commerce không được kéo module khác vào ngoài Shared. Module khác dùng Contracts này mà không thấy entity.
        public void CommerceContracts_ChiThamChieuShared()
        {
            var contractReferences = typeof(Pickory.Modules.Commerce.Contracts.Orders.OrderStatus).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(name => name.StartsWith("Pickory.", StringComparison.Ordinal))
                .ToList();

            Assert.All(contractReferences, name => Assert.Equal("Pickory.Shared", name));
        }

        [Fact]
        // Hàm dùng chung cho phép phụ thuộc vào namespace Contracts của module khác.
        public void HamKiemTraDungChung_ChoPhepPhuThuoc_VaoNamespaceContractsCuaModuleKhac()
        {
            // Áp dụng hàm dùng chung lên kiểu tham chiếu hợp đồng công khai với giả định thuộc Catalog.
            var result = ModuleArchitectureValidator.ValidateType(typeof(SampleAllowedContractConsumer), "Pickory.Modules.Catalog");

            // Khẳng định quy tắc chấp nhận phụ thuộc hợp đồng và không có vi phạm nào.
            Assert.True(result.IsSuccessful);
            Assert.Empty(result.Violations);
        }
    }
}
