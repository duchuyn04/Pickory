using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;

namespace Pickory.Modules.Catalog.Seeding;

// Dữ liệu mẫu gồm 50 sản phẩm thật, mỗi danh mục gốc có 10 sản phẩm. Tên và thông số lấy từ trang công khai của hãng.
// Giá chỉ minh họa, không phải báo giá của hãng.
public static class CatalogSeedData
{
    public const string Laptop = "Laptop";
    public const string Phone = "Điện thoại";
    public const string Headphones = "Tai nghe";
    public const string Monitor = "Màn hình";
    public const string Accessory = "Phụ kiện";

    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

    public static SeedCatalog Create() => new(Categories(), Products());

    private static AttributeDefinition Enum(
        string key, string label, string group, string[] options, string? unit = null, bool required = false) =>
        new() { Key = key, Label = label, Group = group, Type = AttributeTypes.Enum, Options = options, Unit = unit, Required = required, Filterable = true };

    private static AttributeDefinition Number(
        string key, string label, string group, string unit, bool filterable = true, bool required = false) =>
        new() { Key = key, Label = label, Group = group, Type = AttributeTypes.Number, Unit = unit, Required = required, Filterable = filterable };

    private static AttributeDefinition Flag(string key, string label, string group) =>
        new() { Key = key, Label = label, Group = group, Type = AttributeTypes.Boolean, Filterable = true };

    private static AttributeDefinition Text(string key, string label, string group, bool filterable = false) =>
        new() { Key = key, Label = label, Group = group, Type = AttributeTypes.Text, Filterable = filterable };

    private static IReadOnlyList<SeedCategory> Categories() =>
    [
        new(Laptop,
        [
            Enum("ram_gb", "RAM", "Cấu hình", ["8", "16", "32", "64"], "GB", required: true),
            Text("cpu", "Vi xử lý", "Cấu hình", filterable: true),
            Number("screen_inch", "Kích thước màn hình", "Màn hình", "inch", required: true),
            Flag("touch", "Màn hình cảm ứng", "Màn hình"),
            Number("weight_kg", "Khối lượng", "Thân máy", "kg", filterable: false),
        ], new SeedPackaging(2200, 40, 30, 8)),
        new(Phone,
        [
            Enum("storage_gb", "Bộ nhớ trong", "Bộ nhớ", ["64", "128", "256", "512", "1024"], "GB", required: true),
            Enum("ram_gb", "RAM", "Bộ nhớ", ["4", "6", "8", "12", "16"], "GB"),
            Number("screen_inch", "Kích thước màn hình", "Màn hình", "inch", required: true),
            Number("battery_mah", "Dung lượng pin", "Pin", "mAh", filterable: false),
            Enum("os", "Hệ điều hành", "Phần mềm", ["Android", "iOS"]),
        ], new SeedPackaging(450, 20, 11, 6)),
        new(Headphones,
        [
            Enum("kind", "Kiểu tai nghe", "Thiết kế", ["Over-ear", "True wireless"], required: true),
            Flag("anc", "Chống ồn chủ động", "Âm thanh"),
            Number("battery_hours", "Thời lượng pin", "Pin", "giờ"),
            Text("connectivity", "Kết nối", "Kết nối"),
        ], new SeedPackaging(500, 22, 20, 9)),
        new(Monitor,
        [
            Number("size_inch", "Kích thước", "Hiển thị", "inch", required: true),
            Enum("panel", "Tấm nền", "Hiển thị", ["IPS", "VA", "OLED"]),
            Number("refresh_hz", "Tần số quét", "Hiển thị", "Hz"),
            Text("resolution", "Độ phân giải", "Hiển thị"),
        ], new SeedPackaging(6500, 70, 45, 20)),
        new(Accessory,
        [
            Enum("kind", "Loại phụ kiện", "Phân loại", ["Chuột", "Bàn phím", "Sạc", "Cáp"], required: true),
            Enum("connection", "Kết nối", "Kết nối", ["Có dây", "Bluetooth", "Không dây 2.4 GHz"]),
            Text("compatibility", "Tương thích", "Kết nối"),
        ], new SeedPackaging(400, 25, 15, 8)),
    ];

    private static IReadOnlyDictionary<string, string> Option(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value);

    private static IReadOnlyDictionary<string, object> Specs(params (string Key, object Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static IReadOnlyList<ProductOption> Options(params (string Name, string[] Values)[] options) =>
        options.Select(option => new ProductOption(option.Name, option.Values)).ToList();

    // Sản phẩm một biến thể, không có tùy chọn.
    private static SeedProduct Single(
        string name, string category, string brand, string shortDesc, string detail,
        IReadOnlyDictionary<string, object> specs, int price, int stock,
        ProductStatus status = ProductStatus.Active, int? compareAtPrice = null) =>
        new(name, category, brand, shortDesc, Describe(name, detail), specs, [],
            [new SeedVariant(NoOptions, price, stock, compareAtPrice)], status);

    // Đoạn mô tả thứ hai giống nhau cho mọi sản phẩm mẫu. Mô tả riêng của từng sản phẩm đứng trước.
    private static string Describe(string name, string detail) =>
        $"{detail}\n\n{name} là sản phẩm trong bộ dữ liệu mẫu của Pickory, dùng để thử giao diện và tìm kiếm.";

    private static IReadOnlyList<SeedProduct> Products() =>
        AllProducts().Select(WithImages).ToList();

    private static IEnumerable<SeedProduct> AllProducts() =>
    [
        .. LaptopProducts(),
        .. PhoneProducts(),
        .. HeadphoneProducts(),
        .. MonitorProducts(),
        .. AccessoryProducts(),
    ];

    // Gắn ảnh nguồn theo tên sản phẩm. Thiếu ảnh là lỗi dữ liệu mẫu, nên dừng ngay thay vì tạo sản phẩm không có ảnh.
    private static SeedProduct WithImages(SeedProduct product)
    {
        if (!CatalogSeedImages.SourceUrlsByProductName.TryGetValue(product.Name, out var sourceUrls))
        {
            throw new InvalidOperationException($"Sản phẩm mẫu \"{product.Name}\" chưa có URL ảnh nguồn.");
        }

        return product with { Images = sourceUrls.Select(sourceUrl => new SeedImage(sourceUrl)).ToList() };
    }

    private static IEnumerable<SeedProduct> LaptopProducts()
    {
        yield return Single("ASUS Vivobook 15 X1504VA", Laptop, "ASUS",
            "Laptop văn phòng 15.6 inch mỏng nhẹ, chip Intel Core i5 thế hệ 13.",
            "Màn hình 15.6 inch Full HD, chip Intel Core i5-1335U, RAM 16 GB và ổ SSD 512 GB.",
            Specs(("ram_gb", "16"), ("cpu", "Intel Core i5-1335U"), ("screen_inch", 15.6), ("touch", false), ("weight_kg", 1.7)),
            14_990_000, stock: 18);

        yield return new SeedProduct("ASUS ROG Zephyrus G14 GA403", Laptop, "ASUS",
            "Laptop gaming 14 inch màn hình OLED 3K 120 Hz, chip AMD Ryzen 9.",
            Describe("ASUS ROG Zephyrus G14 GA403", "Màn hình OLED 14 inch độ phân giải 3K, tần số quét 120 Hz, chip AMD Ryzen 9 8945HS."),
            Specs(("ram_gb", "32"), ("cpu", "AMD Ryzen 9 8945HS"), ("screen_inch", 14), ("touch", false), ("weight_kg", 1.5)),
            Options(("Màu", ["Xám Eclipse", "Trắng Platinum"]), ("Ổ cứng", ["1TB", "2TB"])),
            [
                new SeedVariant(Option(("Màu", "Xám Eclipse"), ("Ổ cứng", "1TB")), 49_990_000, 6),
                new SeedVariant(Option(("Màu", "Xám Eclipse"), ("Ổ cứng", "2TB")), 56_990_000, 3),
                new SeedVariant(Option(("Màu", "Trắng Platinum"), ("Ổ cứng", "1TB")), 49_990_000, 4),
                new SeedVariant(Option(("Màu", "Trắng Platinum"), ("Ổ cứng", "2TB")), 56_990_000, 0),
            ]);

        yield return Single("Dell XPS 13 9340", Laptop, "Dell",
            "Laptop cao cấp 13.4 inch, chip Intel Core Ultra 7, vỏ nhôm nguyên khối.",
            "Màn hình 13.4 inch Full HD+, chip Intel Core Ultra 7 155H, RAM 16 GB, ổ SSD 512 GB.",
            Specs(("ram_gb", "16"), ("cpu", "Intel Core Ultra 7 155H"), ("screen_inch", 13.4), ("touch", false), ("weight_kg", 1.2)),
            42_990_000, stock: 5);

        yield return new SeedProduct("Apple MacBook Air 13 inch M3", Laptop, "Apple",
            "MacBook Air 13.6 inch với chip Apple M3, pin dùng cả ngày.",
            Describe("Apple MacBook Air 13 inch M3", "Màn hình Liquid Retina 13.6 inch, chip Apple M3, thân máy nhôm không quạt."),
            Specs(("ram_gb", "8"), ("cpu", "Apple M3"), ("screen_inch", 13.6), ("touch", false), ("weight_kg", 1.24)),
            Options(("Màu", ["Bạc", "Xám", "Ánh sao", "Xanh đen"]), ("Ổ cứng", ["256GB", "512GB"])),
            [
                new SeedVariant(Option(("Màu", "Bạc"), ("Ổ cứng", "256GB")), 27_990_000, 8, CompareAtPrice: 29_990_000),
                new SeedVariant(Option(("Màu", "Bạc"), ("Ổ cứng", "512GB")), 32_990_000, 5),
                new SeedVariant(Option(("Màu", "Xám"), ("Ổ cứng", "256GB")), 27_990_000, 7),
                new SeedVariant(Option(("Màu", "Xám"), ("Ổ cứng", "512GB")), 32_990_000, 4),
                new SeedVariant(Option(("Màu", "Ánh sao"), ("Ổ cứng", "256GB")), 27_990_000, 6),
                new SeedVariant(Option(("Màu", "Ánh sao"), ("Ổ cứng", "512GB")), 32_990_000, 3),
                new SeedVariant(Option(("Màu", "Xanh đen"), ("Ổ cứng", "256GB")), 27_990_000, 6),
                new SeedVariant(Option(("Màu", "Xanh đen"), ("Ổ cứng", "512GB")), 32_990_000, 2),
            ]);

        yield return Single("Lenovo ThinkPad X1 Carbon Gen 12", Laptop, "Lenovo",
            "Laptop doanh nhân 14 inch siêu nhẹ, chip Intel Core Ultra 7.",
            "Màn hình 14 inch WUXGA, chip Intel Core Ultra 7 155U, RAM 32 GB, thân máy sợi carbon.",
            Specs(("ram_gb", "32"), ("cpu", "Intel Core Ultra 7 155U"), ("screen_inch", 14), ("touch", true), ("weight_kg", 1.09)),
            52_990_000, stock: 3);

        yield return Single("Lenovo IdeaPad Slim 5 15IRU9", Laptop, "Lenovo",
            "Laptop văn phòng 15.3 inch mỏng nhẹ, chip Intel Core 5 120U.",
            "Màn hình 15.3 inch WUXGA, chip Intel Core 5 120U, RAM 16 GB và ổ SSD 512 GB.",
            Specs(("ram_gb", "16"), ("cpu", "Intel Core 5 120U"), ("screen_inch", 15.3), ("touch", false), ("weight_kg", 1.7)),
            15_490_000, stock: 14);

        // Bốn sản phẩm sau thêm cho bộ 50 sản phẩm. Giá chỉ minh họa theo thị trường Việt Nam, không phải báo giá của hãng.
        yield return new SeedProduct("Apple MacBook Pro 14 inch M4", Laptop, "Apple",
            "MacBook Pro 14 inch với chip Apple M4, màn hình Liquid Retina XDR.",
            Describe("Apple MacBook Pro 14 inch M4", "Màn hình Liquid Retina XDR 14.2 inch, chip Apple M4, RAM 16 GB và thân máy nhôm tái chế."),
            Specs(("ram_gb", "16"), ("cpu", "Apple M4"), ("screen_inch", 14.2), ("touch", false), ("weight_kg", 1.55)),
            Options(("Màu", ["Đen không gian", "Bạc"]), ("Ổ cứng", ["512GB", "1TB"])),
            [
                new SeedVariant(Option(("Màu", "Đen không gian"), ("Ổ cứng", "512GB")), 39_990_000, 6),
                new SeedVariant(Option(("Màu", "Đen không gian"), ("Ổ cứng", "1TB")), 45_990_000, 4),
                new SeedVariant(Option(("Màu", "Bạc"), ("Ổ cứng", "512GB")), 39_990_000, 5),
                new SeedVariant(Option(("Màu", "Bạc"), ("Ổ cứng", "1TB")), 45_990_000, 3),
            ]);

        yield return Single("Lenovo Legion Pro 5 16IRX9", Laptop, "Lenovo",
            "Laptop gaming 16 inch màn hình WQXGA 165 Hz, chip Intel Core i7 HX.",
            "Màn hình 16 inch WQXGA, chip Intel Core i7-14650HX, RAM 32 GB và card đồ họa NVIDIA GeForce RTX 4060.",
            Specs(("ram_gb", "32"), ("cpu", "Intel Core i7-14650HX"), ("screen_inch", 16), ("touch", false), ("weight_kg", 2.5)),
            49_990_000, stock: 7);

        yield return Single("Acer Nitro V 15 ANV15-51", Laptop, "Acer",
            "Laptop gaming 15.6 inch Full HD 144 Hz, chip Intel Core i5 thế hệ 13.",
            "Màn hình 15.6 inch Full HD 144 Hz, chip Intel Core i5-13420H, RAM 16 GB và card đồ họa NVIDIA GeForce RTX 4050.",
            Specs(("ram_gb", "16"), ("cpu", "Intel Core i5-13420H"), ("screen_inch", 15.6), ("touch", false), ("weight_kg", 2.1)),
            23_990_000, stock: 12);

        yield return Single("Lenovo IdeaPad Flex 5 14IRU8", Laptop, "Lenovo",
            "Laptop lai 14 inch xoay gập 360 độ, cảm ứng, chip Intel Core i5 thế hệ 13.",
            "Màn hình cảm ứng 14 inch WUXGA xoay gập 360 độ, chip Intel Core i5-1335U, RAM 16 GB và ổ SSD 512 GB.",
            Specs(("ram_gb", "16"), ("cpu", "Intel Core i5-1335U"), ("screen_inch", 14), ("touch", true), ("weight_kg", 1.5)),
            16_990_000, stock: 8);
    }

    private static IEnumerable<SeedProduct> PhoneProducts()
    {
        yield return new SeedProduct("Apple iPhone 15", Phone, "Apple",
            "iPhone 15 màn hình Super Retina XDR 6.1 inch, cổng USB-C, camera chính 48 MP.",
            Describe("Apple iPhone 15", "Chip A16 Bionic, màn hình 6.1 inch, cổng sạc USB-C và camera chính 48 MP."),
            Specs(("storage_gb", "128"), ("ram_gb", "6"), ("screen_inch", 6.1), ("battery_mah", 3349), ("os", "iOS")),
            Options(("Màu", ["Hồng", "Xanh dương", "Đen"]), ("Bộ nhớ", ["128GB", "256GB"])),
            [
                new SeedVariant(Option(("Màu", "Hồng"), ("Bộ nhớ", "128GB")), 19_990_000, 12),
                new SeedVariant(Option(("Màu", "Hồng"), ("Bộ nhớ", "256GB")), 22_990_000, 6),
                new SeedVariant(Option(("Màu", "Xanh dương"), ("Bộ nhớ", "128GB")), 19_990_000, 10),
                new SeedVariant(Option(("Màu", "Xanh dương"), ("Bộ nhớ", "256GB")), 22_990_000, 5),
                new SeedVariant(Option(("Màu", "Đen"), ("Bộ nhớ", "128GB")), 19_990_000, 14),
                new SeedVariant(Option(("Màu", "Đen"), ("Bộ nhớ", "256GB")), 22_990_000, 7),
            ]);

        yield return new SeedProduct("Samsung Galaxy S24", Phone, "Samsung",
            "Galaxy S24 màn hình Dynamic AMOLED 2X 6.2 inch, chip Exynos 2400.",
            Describe("Samsung Galaxy S24", "Màn hình 6.2 inch tần số quét 120 Hz, chip Exynos 2400 và các tính năng Galaxy AI."),
            Specs(("storage_gb", "256"), ("ram_gb", "8"), ("screen_inch", 6.2), ("battery_mah", 4000), ("os", "Android")),
            Options(("Màu", ["Đen Onyx", "Tím Cobalt"]), ("Bộ nhớ", ["256GB", "512GB"])),
            [
                new SeedVariant(Option(("Màu", "Đen Onyx"), ("Bộ nhớ", "256GB")), 22_990_000, 9),
                new SeedVariant(Option(("Màu", "Đen Onyx"), ("Bộ nhớ", "512GB")), 26_990_000, 4),
                new SeedVariant(Option(("Màu", "Tím Cobalt"), ("Bộ nhớ", "256GB")), 22_990_000, 8),
                new SeedVariant(Option(("Màu", "Tím Cobalt"), ("Bộ nhớ", "512GB")), 26_990_000, 3),
            ]);

        yield return Single("Samsung Galaxy A55 5G", Phone, "Samsung",
            "Galaxy A55 5G màn hình Super AMOLED 6.6 inch, pin 5000 mAh.",
            "Màn hình 6.6 inch tần số quét 120 Hz, chip Exynos 1480, RAM 8 GB và bộ nhớ 256 GB.",
            Specs(("storage_gb", "256"), ("ram_gb", "8"), ("screen_inch", 6.6), ("battery_mah", 5000), ("os", "Android")),
            11_990_000, stock: 20);

        yield return Single("Xiaomi Redmi Note 13 Pro 5G", Phone, "Xiaomi",
            "Redmi Note 13 Pro 5G màn hình AMOLED 6.67 inch, camera chính 200 MP.",
            "Chip Snapdragon 7s Gen 2, màn hình AMOLED 6.67 inch 120 Hz, camera chính 200 MP, pin 5100 mAh sạc nhanh 67 W.",
            Specs(("storage_gb", "256"), ("ram_gb", "8"), ("screen_inch", 6.67), ("battery_mah", 5100), ("os", "Android")),
            7_990_000, stock: 25);

        yield return Single("Google Pixel 8", Phone, "Google",
            "Pixel 8 với chip Google Tensor G3 và 7 năm cập nhật hệ điều hành.",
            "Màn hình Actua 6.2 inch tần số quét 120 Hz, chip Google Tensor G3, camera chính 50 MP.",
            Specs(("storage_gb", "128"), ("ram_gb", "8"), ("screen_inch", 6.2), ("battery_mah", 4575), ("os", "Android")),
            16_990_000, stock: 6);

        yield return Single("Apple iPhone 13", Phone, "Apple",
            "iPhone 13 đã ngừng kinh doanh, màn hình Super Retina XDR 6.1 inch.",
            "Chip A15 Bionic, màn hình 6.1 inch, camera kép 12 MP.",
            Specs(("storage_gb", "128"), ("ram_gb", "4"), ("screen_inch", 6.1), ("battery_mah", 3240), ("os", "iOS")),
            13_990_000, stock: 3, status: ProductStatus.Discontinued);

        // Bốn sản phẩm sau thêm cho bộ 50 sản phẩm. Giá chỉ minh họa theo thị trường Việt Nam, không phải báo giá của hãng.
        yield return new SeedProduct("Apple iPhone 16 Pro", Phone, "Apple",
            "iPhone 16 Pro khung titan, màn hình 6.3 inch, chip A18 Pro.",
            Describe("Apple iPhone 16 Pro", "Chip A18 Pro, màn hình Super Retina XDR 6.3 inch, khung titan và nút Điều khiển camera."),
            Specs(("storage_gb", "128"), ("ram_gb", "8"), ("screen_inch", 6.3), ("battery_mah", 3582), ("os", "iOS")),
            Options(("Màu", ["Titan sa mạc", "Titan tự nhiên", "Titan đen"]), ("Bộ nhớ", ["128GB", "256GB"])),
            [
                new SeedVariant(Option(("Màu", "Titan sa mạc"), ("Bộ nhớ", "128GB")), 28_990_000, 9),
                new SeedVariant(Option(("Màu", "Titan sa mạc"), ("Bộ nhớ", "256GB")), 31_990_000, 5),
                new SeedVariant(Option(("Màu", "Titan tự nhiên"), ("Bộ nhớ", "128GB")), 28_990_000, 8),
                new SeedVariant(Option(("Màu", "Titan tự nhiên"), ("Bộ nhớ", "256GB")), 31_990_000, 4),
                new SeedVariant(Option(("Màu", "Titan đen"), ("Bộ nhớ", "128GB")), 28_990_000, 10),
                new SeedVariant(Option(("Màu", "Titan đen"), ("Bộ nhớ", "256GB")), 31_990_000, 6),
            ]);

        yield return new SeedProduct("Samsung Galaxy Z Flip6", Phone, "Samsung",
            "Điện thoại gập vỏ sò, màn hình chính 6.7 inch, chip Snapdragon 8 Gen 3.",
            Describe("Samsung Galaxy Z Flip6", "Màn hình gập Dynamic AMOLED 2X 6.7 inch, chip Snapdragon 8 Gen 3 for Galaxy, bộ nhớ 256 GB."),
            Specs(("storage_gb", "256"), ("ram_gb", "12"), ("screen_inch", 6.7), ("battery_mah", 4000), ("os", "Android")),
            Options(("Màu", ["Xanh bạc hà", "Bạc", "Vàng"])),
            [
                new SeedVariant(Option(("Màu", "Xanh bạc hà")), 25_990_000, 7),
                new SeedVariant(Option(("Màu", "Bạc")), 25_990_000, 6),
                new SeedVariant(Option(("Màu", "Vàng")), 25_990_000, 4),
            ]);

        yield return Single("Xiaomi 14T Pro", Phone, "Xiaomi",
            "Điện thoại 6.67 inch màn hình AMOLED 144 Hz, camera Leica, sạc 120 W.",
            "Chip MediaTek Dimensity 9300+, màn hình AMOLED 6.67 inch 144 Hz, camera Leica và sạc nhanh 120 W.",
            Specs(("storage_gb", "256"), ("ram_gb", "12"), ("screen_inch", 6.67), ("battery_mah", 5000), ("os", "Android")),
            14_990_000, stock: 11);

        yield return Single("OPPO Reno12 5G", Phone, "OPPO",
            "Điện thoại 5G màn hình AMOLED 6.7 inch, camera chân dung 50 MP.",
            "Chip MediaTek Dimensity 7300-Energy, màn hình AMOLED 6.7 inch, pin 5000 mAh và sạc nhanh 80 W.",
            Specs(("storage_gb", "256"), ("ram_gb", "12"), ("screen_inch", 6.7), ("battery_mah", 5000), ("os", "Android")),
            12_990_000, stock: 16);
    }

    private static IEnumerable<SeedProduct> HeadphoneProducts()
    {
        yield return Single("Sony WH-1000XM5", Headphones, "Sony",
            "Tai nghe chụp tai chống ồn chủ động, pin tới 30 giờ.",
            "Chống ồn chủ động với 8 micro, kết nối Bluetooth 5.2, pin 30 giờ khi bật chống ồn.",
            Specs(("kind", "Over-ear"), ("anc", true), ("battery_hours", 30), ("connectivity", "Bluetooth 5.2")),
            7_990_000, stock: 15);

        yield return Single("Apple AirPods Pro (thế hệ 2)", Headphones, "Apple",
            "Tai nghe true wireless chống ồn chủ động, hộp sạc USB-C.",
            "Chip H2, chống ồn chủ động, chế độ xuyên âm, hộp sạc MagSafe cổng USB-C.",
            Specs(("kind", "True wireless"), ("anc", true), ("battery_hours", 6), ("connectivity", "Bluetooth 5.3")),
            5_990_000, stock: 22);

        yield return Single("Samsung Galaxy Buds3 Pro", Headphones, "Samsung",
            "Tai nghe true wireless chống ồn chủ động thích ứng, Bluetooth 5.4.",
            "Chống ồn chủ động thích ứng, kết nối Bluetooth 5.4, pin tai nghe 6 giờ khi bật chống ồn và hộp sạc.",
            Specs(("kind", "True wireless"), ("anc", true), ("battery_hours", 6), ("connectivity", "Bluetooth 5.4")),
            6_490_000, stock: 9);

        yield return Single("Bose QuietComfort 45", Headphones, "Bose",
            "Tai nghe chụp tai chống ồn chủ động, pin tới 24 giờ.",
            "Chống ồn chủ động, chế độ nhận biết âm thanh xung quanh, kết nối Bluetooth 5.1.",
            Specs(("kind", "Over-ear"), ("anc", true), ("battery_hours", 24), ("connectivity", "Bluetooth 5.1")),
            7_490_000, stock: 0);

        yield return Single("JBL Tune 520BT", Headphones, "JBL",
            "Tai nghe chụp tai không dây, pin tới 57 giờ.",
            "Âm thanh JBL Pure Bass, kết nối Bluetooth 5.3, pin 57 giờ.",
            Specs(("kind", "Over-ear"), ("anc", false), ("battery_hours", 57), ("connectivity", "Bluetooth 5.3")),
            990_000, stock: 40);

        yield return Single("Samsung Galaxy Buds2 Pro", Headphones, "Samsung",
            "Tai nghe true wireless chống ồn chủ động cho điện thoại Galaxy.",
            "Chống ồn chủ động thông minh, âm thanh 24 bit, kết nối Bluetooth 5.3.",
            Specs(("kind", "True wireless"), ("anc", true), ("battery_hours", 5), ("connectivity", "Bluetooth 5.3")),
            3_990_000, stock: 11);

        // Bốn sản phẩm sau thêm cho bộ 50 sản phẩm. Giá chỉ minh họa theo thị trường Việt Nam, không phải báo giá của hãng.
        yield return new SeedProduct("Apple AirPods 4", Headphones, "Apple",
            "Tai nghe true wireless thiết kế mới, chip H2, hộp sạc USB-C.",
            Describe("Apple AirPods 4", "Chip H2, âm thanh không gian, hộp sạc USB-C và thiết kế vừa tai hơn."),
            Specs(("kind", "True wireless"), ("anc", false), ("battery_hours", 5), ("connectivity", "Bluetooth 5.3")),
            Options(("Phiên bản", ["Tiêu chuẩn", "Chống ồn chủ động"])),
            [
                new SeedVariant(Option(("Phiên bản", "Tiêu chuẩn")), 3_490_000, 20),
                new SeedVariant(Option(("Phiên bản", "Chống ồn chủ động")), 4_990_000, 12),
            ]);

        yield return Single("JBL Tune 770NC", Headphones, "JBL",
            "Tai nghe chụp tai chống ồn thích ứng, pin tới 70 giờ.",
            "Chống ồn thích ứng, kết nối Bluetooth 5.3, pin 44 giờ khi bật chống ồn và 70 giờ khi tắt chống ồn.",
            Specs(("kind", "Over-ear"), ("anc", true), ("battery_hours", 44), ("connectivity", "Bluetooth 5.3")),
            2_490_000, stock: 18);

        yield return Single("Sennheiser Momentum 4 Wireless", Headphones, "Sennheiser",
            "Tai nghe chụp tai chống ồn thích ứng, pin tới 60 giờ.",
            "Chống ồn chủ động thích ứng, kết nối Bluetooth 5.2, pin 60 giờ và âm thanh chi tiết.",
            Specs(("kind", "Over-ear"), ("anc", true), ("battery_hours", 60), ("connectivity", "Bluetooth 5.2")),
            8_490_000, stock: 8);

        yield return Single("Soundcore Liberty 4 NC", Headphones, "Soundcore",
            "Tai nghe true wireless chống ồn thích ứng, pin 10 giờ mỗi lần sạc.",
            "Chống ồn thích ứng, âm thanh Hi-Res, kết nối Bluetooth 5.3 và pin 10 giờ khi bật chống ồn.",
            Specs(("kind", "True wireless"), ("anc", true), ("battery_hours", 10), ("connectivity", "Bluetooth 5.3")),
            2_290_000, stock: 25);
    }

    private static IEnumerable<SeedProduct> MonitorProducts()
    {
        yield return Single("LG UltraGear 27GP850-B", Monitor, "LG",
            "Màn hình gaming 27 inch Nano IPS, độ phân giải 2K, tần số quét 165 Hz.",
            "Tấm nền Nano IPS 27 inch, phản hồi 1 ms, hỗ trợ NVIDIA G-SYNC Compatible.",
            Specs(("size_inch", 27), ("panel", "IPS"), ("refresh_hz", 165), ("resolution", "2560 x 1440")),
            8_990_000, stock: 7);

        yield return Single("Dell UltraSharp U2723QE", Monitor, "Dell",
            "Màn hình 27 inch 4K IPS Black, hub USB-C cho dân đồ họa.",
            "Tấm nền IPS Black 27 inch 4K, độ phủ màu 100% sRGB, cổng USB-C sạc 90 W.",
            Specs(("size_inch", 27), ("panel", "IPS"), ("refresh_hz", 60), ("resolution", "3840 x 2160")),
            13_990_000, stock: 5);

        yield return Single("Samsung Odyssey G5 LC27G55T", Monitor, "Samsung",
            "Màn hình gaming cong 27 inch VA, độ phân giải 2K, tần số quét 144 Hz.",
            "Tấm nền VA cong 1000R, phản hồi 1 ms, hỗ trợ AMD FreeSync Premium.",
            Specs(("size_inch", 27), ("panel", "VA"), ("refresh_hz", 144), ("resolution", "2560 x 1440")),
            6_490_000, stock: 12);

        yield return Single("AOC 24G2", Monitor, "AOC",
            "Màn hình gaming 24 inch IPS Full HD, tần số quét 144 Hz.",
            "Tấm nền IPS 23.8 inch, viền mỏng 3 cạnh, phản hồi 1 ms.",
            Specs(("size_inch", 23.8), ("panel", "IPS"), ("refresh_hz", 144), ("resolution", "1920 x 1080")),
            3_990_000, stock: 16);

        yield return Single("Dell P2422H", Monitor, "Dell",
            "Màn hình văn phòng 24 inch IPS Full HD đã ngừng kinh doanh.",
            "Tấm nền IPS 23.8 inch, chân đế điều chỉnh độ cao, cổng DisplayPort và HDMI.",
            Specs(("size_inch", 23.8), ("panel", "IPS"), ("refresh_hz", 60), ("resolution", "1920 x 1080")),
            3_790_000, stock: 2, status: ProductStatus.Discontinued);

        yield return Single("MSI Optix MAG274QRF-QD", Monitor, "MSI",
            "Màn hình gaming 27 inch Rapid IPS, độ phân giải 2K, tần số quét 165 Hz.",
            "Tấm nền Rapid IPS với công nghệ chấm lượng tử, phản hồi 1 ms.",
            Specs(("size_inch", 27), ("panel", "IPS"), ("refresh_hz", 165), ("resolution", "2560 x 1440")),
            7_490_000, stock: 0);

        // Bốn sản phẩm sau thêm cho bộ 50 sản phẩm. Giá chỉ minh họa theo thị trường Việt Nam, không phải báo giá của hãng.
        yield return Single("LG UltraGear 27GR95QE-B", Monitor, "LG",
            "Màn hình gaming 27 inch OLED 2K, tần số quét 240 Hz, phản hồi 0.03 ms.",
            "Tấm nền OLED 27 inch độ phân giải 2560 x 1440, tần số quét 240 Hz, hỗ trợ NVIDIA G-SYNC Compatible.",
            Specs(("size_inch", 27), ("panel", "OLED"), ("refresh_hz", 240), ("resolution", "2560 x 1440")),
            19_990_000, stock: 4);

        yield return Single("ASUS ProArt PA278CV", Monitor, "ASUS",
            "Màn hình đồ họa 27 inch IPS 2K, độ chính xác màu Delta E dưới 2.",
            "Tấm nền IPS 27 inch độ phân giải 2560 x 1440, 100% sRGB, hiệu chuẩn màu tại nhà máy.",
            Specs(("size_inch", 27), ("panel", "IPS"), ("refresh_hz", 75), ("resolution", "2560 x 1440")),
            7_490_000, stock: 9);

        yield return Single("ASUS TUF Gaming VG249Q1A", Monitor, "ASUS",
            "Màn hình gaming 24 inch IPS Full HD, tần số quét 165 Hz.",
            "Tấm nền IPS 23.8 inch độ phân giải 1920 x 1080, tần số quét 165 Hz, hỗ trợ AMD FreeSync Premium.",
            Specs(("size_inch", 23.8), ("panel", "IPS"), ("refresh_hz", 165), ("resolution", "1920 x 1080")),
            3_490_000, stock: 20);

        yield return Single("Gigabyte M27Q", Monitor, "Gigabyte",
            "Màn hình gaming 27 inch IPS 2K, tần số quét 170 Hz, tích hợp KVM.",
            "Tấm nền IPS 27 inch độ phân giải 2560 x 1440, tần số quét 170 Hz, hỗ trợ AMD FreeSync Premium.",
            Specs(("size_inch", 27), ("panel", "IPS"), ("refresh_hz", 170), ("resolution", "2560 x 1440")),
            6_490_000, stock: 13);
    }

    private static IEnumerable<SeedProduct> AccessoryProducts()
    {
        yield return new SeedProduct("Logitech MX Master 3S", Accessory, "Logitech",
            "Chuột không dây công thái học, cảm biến 8000 DPI, click êm.",
            Describe("Logitech MX Master 3S", "Cảm biến 8000 DPI, con lăn từ tính MagSpeed, kết nối Bluetooth hoặc Logi Bolt."),
            Specs(("kind", "Chuột"), ("connection", "Bluetooth"), ("compatibility", "Windows, macOS, Linux")),
            Options(("Màu", ["Đen", "Xám nhạt"])),
            [
                new SeedVariant(Option(("Màu", "Đen")), 2_390_000, 30),
                new SeedVariant(Option(("Màu", "Xám nhạt")), 2_390_000, 18),
            ]);

        yield return Single("Logitech MX Keys S", Accessory, "Logitech",
            "Bàn phím không dây phím thấp, đèn nền thông minh.",
            "Phím tròn lõm theo ngón tay, đèn nền tự bật khi tay lại gần, kết nối Bluetooth.",
            Specs(("kind", "Bàn phím"), ("connection", "Bluetooth"), ("compatibility", "Windows, macOS, Linux")),
            2_490_000, stock: 14);

        yield return Single("Anker 735 Charger (GaNPrime 65W)", Accessory, "Anker",
            "Sạc nhanh 65 W ba cổng, công nghệ GaN nhỏ gọn.",
            "Hai cổng USB-C và một cổng USB-A, tổng công suất 65 W, sạc được laptop và điện thoại.",
            Specs(("kind", "Sạc"), ("connection", "Có dây"), ("compatibility", "Laptop, điện thoại, máy tính bảng")),
            1_290_000, stock: 35);

        yield return Single("Anker PowerLine III USB-C to USB-C 2.0 Cable (1.8 m)", Accessory, "Anker",
            "Cáp USB-C sang USB-C dài 1.8 m, hỗ trợ sạc 100 W.",
            "Lõi sợi chống đạn, chịu 25.000 lần uốn, truyền dữ liệu USB 2.0 tốc độ 480 Mbps.",
            Specs(("kind", "Cáp"), ("connection", "Có dây"), ("compatibility", "Thiết bị USB-C")),
            390_000, stock: 60);

        yield return Single("Razer DeathAdder V3", Accessory, "Razer",
            "Chuột gaming có dây nhẹ 59 g, cảm biến quang học 30000 DPI.",
            "Thiết kế công thái học, cảm biến Focus Pro 30K, công tắc quang học thế hệ 3.",
            Specs(("kind", "Chuột"), ("connection", "Có dây"), ("compatibility", "Windows")),
            1_790_000, stock: 0);

        yield return Single("Keychron K2 V2", Accessory, "Keychron",
            "Bàn phím cơ không dây 75%, kết nối Bluetooth và cáp USB-C.",
            "Bố cục 84 phím, kết nối Bluetooth 5.1, tương thích Windows và macOS.",
            Specs(("kind", "Bàn phím"), ("connection", "Bluetooth"), ("compatibility", "Windows, macOS")),
            1_990_000, stock: 10, compareAtPrice: 2_290_000);

        // Bốn sản phẩm sau thêm cho bộ 50 sản phẩm. Giá chỉ minh họa theo thị trường Việt Nam, không phải báo giá của hãng.
        yield return new SeedProduct("Logitech G Pro X Superlight 2", Accessory, "Logitech",
            "Chuột gaming không dây siêu nhẹ dưới 60 g, cảm biến HERO 2 32000 DPI.",
            Describe("Logitech G Pro X Superlight 2", "Cảm biến HERO 2 32000 DPI, công tắc quang học LIGHTFORCE và kết nối LIGHTSPEED không dây."),
            Specs(("kind", "Chuột"), ("connection", "Không dây 2.4 GHz"), ("compatibility", "Windows, macOS")),
            Options(("Màu", ["Đen", "Trắng"])),
            [
                new SeedVariant(Option(("Màu", "Đen")), 3_690_000, 15),
                new SeedVariant(Option(("Màu", "Trắng")), 3_690_000, 9),
            ]);

        yield return Single("Apple Magic Keyboard (USB-C)", Accessory, "Apple",
            "Bàn phím không dây phím thấp, sạc qua cổng USB-C.",
            "Bàn phím Bluetooth phím cắt kéo, pin sạc dùng được khoảng một tháng, ghép nối với Mac, iPad và iPhone.",
            Specs(("kind", "Bàn phím"), ("connection", "Bluetooth"), ("compatibility", "macOS, iPadOS, iOS")),
            2_790_000, stock: 10);

        yield return Single("Samsung 45W Power Adapter EP-T4510", Accessory, "Samsung",
            "Củ sạc nhanh 45 W cổng USB-C, chuẩn Super Fast Charging 2.0.",
            "Một cổng USB-C công suất 45 W, sạc nhanh cho điện thoại và máy tính bảng Galaxy.",
            Specs(("kind", "Sạc"), ("connection", "Có dây"), ("compatibility", "Điện thoại, máy tính bảng Galaxy")),
            790_000, stock: 40);

        yield return Single("Samsung USB-C to USB-C Cable 5A (1.8 m)", Accessory, "Samsung",
            "Cáp sạc USB-C sang USB-C dài 1.8 m, dòng 5 A, hỗ trợ sạc tới 100 W.",
            "Hai đầu USB-C, dòng điện 5 A, công suất tối đa 100 W theo chuẩn PD và truyền dữ liệu USB 2.0.",
            Specs(("kind", "Cáp"), ("connection", "Có dây"), ("compatibility", "Thiết bị USB-C")),
            590_000, stock: 50);
    }
}
