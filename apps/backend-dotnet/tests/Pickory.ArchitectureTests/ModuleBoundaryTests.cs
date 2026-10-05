using System.Reflection;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Identity;
using Xunit;

namespace Pickory.Modules.Identity.Contracts
{
    // Kiểu dữ liệu giả lập thuộc hợp đồng công khai của module Identity.
    public class SampleIdentityContract
    {
    }
}

namespace Pickory.ArchitectureTests
{
    // Lớp mẫu cố tình phụ thuộc vào module khác để chứng minh test kiến trúc bắt được vi phạm.
    public class SampleViolatingClass
    {
        // Tham chiếu trực tiếp tới module Identity để tạo ra một vi phạm ranh giới mẫu.
        public IdentityModule? DirectDependency { get; set; }
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
