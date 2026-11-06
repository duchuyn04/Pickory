using System.IO;
using System.Reflection;
using System.Text.Json;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.ArchitectureTests;

public class IntegrationTestArchitectureTests
{
    [Fact]
    public void IntegrationTests_MoiLopTestPhaiThuocDungMotCollection()
    {
        // Quét assembly kiểm thử tích hợp từ kiểu dữ liệu fixture gốc.
        var integrationAssembly = typeof(IntegrationDatabaseFixture).Assembly;

        // Lọc các lớp kiểm thử thật có chứa ít nhất một phương thức Fact hoặc Theory.
        var testClasses = integrationAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsNested)
            .Where(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.GetCustomAttributes(typeof(FactAttribute), true).Length != 0 ||
                               method.GetCustomAttributes(typeof(TheoryAttribute), true).Length != 0))
            .ToList();

        // Xác nhận assembly có chứa các lớp kiểm thử để tránh quét rỗng khi đổi cấu trúc.
        Assert.NotEmpty(testClasses);

        // Tìm các lớp kiểm thử thiếu collection hoặc có nhiều hơn một collection.
        var invalidClasses = testClasses
            .Where(type => type.GetCustomAttributes(typeof(CollectionAttribute), true).Length != 1)
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name)
            .ToList();
        Assert.True(
            invalidClasses.Count == 0,
            $"Tổng số lớp test: {testClasses.Count}. Các lớp kiểm thử tích hợp sau không có đúng một Collection: {string.Join(", ", invalidClasses)}");
    }

    [Fact]
    public void XUnitRunnerConfiguration_GioiHanToiDaBaLuongSongSong()
    {
        // Xác định thư mục đầu ra chứa assembly kiểm thử tích hợp.
        var assemblyDir = Path.GetDirectoryName(typeof(IntegrationDatabaseFixture).Assembly.Location)!;
        var configPath = Path.Combine(assemblyDir, "xunit.runner.json");

        // Kiểm tra file cấu hình runner tồn tại trong thư mục đầu ra.
        Assert.True(File.Exists(configPath), "Thiếu file xunit.runner.json trong thư mục đầu ra của Pickory.IntegrationTests.");

        // Đọc nội dung cấu hình để kiểm tra các thuộc tính song song hóa.
        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Kiểm tra thuộc tính bật song song hóa collection.
        Assert.True(root.GetProperty("parallelizeTestCollections").GetBoolean());

        // Kiểm tra số luồng tối đa bằng 3 để bảo đảm không vượt quá dung lượng bộ nhớ.
        Assert.Equal(3, root.GetProperty("maxParallelThreads").GetInt32());
    }

    [Fact]
    public void IntegrationTests_MoiCollectionDinhNghiaDungMotFixtureDatabase()
    {
        // Quét assembly kiểm thử tích hợp để tìm các định nghĩa collection.
        var integrationAssembly = typeof(IntegrationDatabaseFixture).Assembly;
        var collectionDefinitions = integrationAssembly.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttribute<CollectionDefinitionAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(collectionDefinitions);

        // Kiểm tra mỗi collection definition chỉ đăng ký duy nhất một fixture cơ sở dữ liệu.
        var invalidCollections = collectionDefinitions
            .Where(def =>
            {
                var fixtureCount = def.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))
                    .Count(i => typeof(IntegrationDatabaseFixture).IsAssignableFrom(i.GetGenericArguments()[0]));
                return fixtureCount != 1;
            })
            .Select(def => def.FullName ?? def.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            invalidCollections.Count == 0,
            $"Các collection sau không có đúng một fixture database: {string.Join(", ", invalidCollections)}");
    }

    [Fact]
    public void IntegrationTests_CacHauToDatabaseLaDuyNhat()
    {
        // Lấy danh sách fixture từ các collection definition để kiểm tra hậu tố.
        var integrationAssembly = typeof(IntegrationDatabaseFixture).Assembly;
        var collectionDefinitions = integrationAssembly.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttribute<CollectionDefinitionAttribute>() is not null)
            .ToList();
        var fixtureTypes = collectionDefinitions
            .SelectMany(def => def.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))
                .Select(i => i.GetGenericArguments()[0]))
            .Where(type => typeof(IntegrationDatabaseFixture).IsAssignableFrom(type))
            .Distinct()
            .ToList();

        Assert.NotEmpty(fixtureTypes);

        // Đọc hậu tố của từng fixture chuyên biệt.
        var suffixes = fixtureTypes
            .Select(type => ((IntegrationDatabaseFixture)Activator.CreateInstance(type)!).DatabaseSuffix)
            .ToList();

        // Kiểm tra không có hậu tố nào bị trùng lặp giữa các fixture.
        var duplicateSuffixes = suffixes
            .GroupBy(suffix => suffix, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(
            duplicateSuffixes.Count == 0,
            $"Phát hiện các hậu tố database bị trùng lặp: {string.Join(", ", duplicateSuffixes)}");
    }
}
