using ClosedXML.Excel;
using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.ArchitectureTests;

// API public của `Pickory.Shared.Exports` không được lộ ClosedXML, kể cả khi chưa có module nào gọi.
public class PublicApiClosedXmlExposureTests
{
    // Các lớp mẫu dưới đây cố ý lộ ClosedXML để chứng minh hàm kiểm báo vi phạm.
    public class ExposesInParameter
    {
        public void Write(IXLCell cell)
        {
        }
    }

    public class ExposesInReturnType
    {
        public IXLWorksheet Get() => throw new NotSupportedException();
    }

    public class ExposesInProperty
    {
        public XLWorkbook? Workbook { get; set; }
    }

    public class ExposesInGenericArgument
    {
        public List<IXLCell>? Cells { get; set; }
    }

    public class ExposesInBaseType : XLWorkbook
    {
    }

    public class ExposesInConstructor
    {
        public ExposesInConstructor(IXLCell cell)
        {
        }
    }

    public class ExposesInField
    {
        public IXLCell? Field;
    }

    public class ExposesInEvent
    {
        public event Action<IXLCell>? Changed;

        public void Raise() => Changed?.Invoke(null!);
    }

    public class ExposesInArrayParameter
    {
        public void M(IXLCell[] cells)
        {
        }
    }

    public class ExposesInGenericArrayParameter
    {
        public void M(List<IXLCell>[] lists)
        {
        }
    }

    public interface IExposesInInterfaceMethod
    {
        void Accept(IXLCell cell);
    }

    // Interface kế thừa interface của ClosedXML. Kiểu này lộ ClosedXML qua `GetInterfaces`, dù không tự khai báo member nào.
    public interface IInheritsClosedXmlInterface : IXLCell
    {
    }

    public class ExposesInTypeConstraint<T>
        where T : IXLCell
    {
    }

    public class ExposesInMethodConstraint
    {
        public void M<T>()
            where T : IXLCell
        {
        }
    }

    public class SelfReferencingConstraintIsNotAViolation<T>
        where T : IComparable<T>
    {
    }

    public class ExposesOnlyInInternalMember
    {
        internal void Write(IXLCell cell)
        {
        }
    }

    [Fact]
    // Mọi kiểu public trong namespace `Pickory.Shared.Exports` phải sạch ClosedXML.
    public void PickoryShared_Exports_PublicApi_KhongLoClosedXml()
    {
        var exportTypes = typeof(ExportColumn<>).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Pickory.Shared.Exports")
            .ToList();
        // Danh sách rỗng làm test xanh giả. Khẳng định quét thấy cả bộ ghi và kiểu cột.
        Assert.Contains(exportTypes, type => type == typeof(XlsxWorkbookWriter));
        Assert.Contains(exportTypes, type => type == typeof(ExportColumn<>));

        var violations = PublicApiClosedXmlExposureValidator.FindViolations(exportTypes);

        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Theory]
    [InlineData(typeof(ExposesInParameter))]
    [InlineData(typeof(ExposesInReturnType))]
    [InlineData(typeof(ExposesInProperty))]
    [InlineData(typeof(ExposesInGenericArgument))]
    [InlineData(typeof(ExposesInBaseType))]
    [InlineData(typeof(ExposesInConstructor))]
    [InlineData(typeof(ExposesInField))]
    [InlineData(typeof(ExposesInEvent))]
    [InlineData(typeof(ExposesInArrayParameter))]
    [InlineData(typeof(ExposesInGenericArrayParameter))]
    [InlineData(typeof(IExposesInInterfaceMethod))]
    [InlineData(typeof(IInheritsClosedXmlInterface))]
    [InlineData(typeof(ExposesInTypeConstraint<>))]
    [InlineData(typeof(ExposesInMethodConstraint))]
    // Hàm kiểm phải báo vi phạm ở mọi chỗ chữ ký lộ ClosedXML: member, kiểu cha, interface và ràng buộc generic.
    public void HamKiem_BaoViPham_KhiKieuPublicLoClosedXml(Type exposingType)
    {
        var violations = PublicApiClosedXmlExposureValidator.FindViolations([exposingType]);

        Assert.NotEmpty(violations);
        Assert.Contains(violations, violation => violation.Contains(exposingType.FullName!));
    }

    [Fact]
    // Ràng buộc tự tham chiếu như `T : IComparable<T>` không được làm hàm kiểm lặp vô hạn hay báo nhầm.
    public void HamKiem_KhongBaoViPham_KhiRangBuocGenericTuThamChieu()
    {
        var violations = PublicApiClosedXmlExposureValidator.FindViolations([typeof(SelfReferencingConstraintIsNotAViolation<>)]);

        Assert.Empty(violations);
    }

    [Fact]
    // Member internal không lộ ra ngoài assembly, nên không bị báo.
    public void HamKiem_KhongBaoViPham_KhiChiCoMemberInternalDungClosedXml()
    {
        var violations = PublicApiClosedXmlExposureValidator.FindViolations([typeof(ExposesOnlyInInternalMember)]);

        Assert.Empty(violations);
    }
}
