namespace Pickory.Modules.Catalog.Seeding;

// URL ảnh nguồn của từng sản phẩm mẫu, lấy từ trang sản phẩm hoặc CDN chính thức của hãng. Ảnh đầu tiên là ảnh đại diện.
// Repo chỉ giữ URL. Lệnh `catalog:seed` nhờ kho ảnh tự tải từ các URL này khi chạy.
internal static class CatalogSeedImages
{
    public static IReadOnlyDictionary<string, string[]> SourceUrlsByProductName { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ASUS Vivobook 15 X1504VA"] =
            [
                "https://dlcdnwebimgs.asus.com/gain/d76a41bc-780e-41ef-938b-4ee8ac4a5634/w800",
                "https://dlcdnwebimgs.asus.com/gain/749f4ca8-435c-4a3e-b24f-d05e8e96603c/w800",
                "https://dlcdnwebimgs.asus.com/gain/26679019-9676-4715-83a9-522f1ca32acd/w800",
                "https://dlcdnwebimgs.asus.com/gain/7caf3875-b180-4985-b1ba-7e024a6b70fb/w800",
                "https://dlcdnwebimgs.asus.com/gain/2c6da486-da10-4f40-89d5-09fd71787fe7/w800",
                "https://dlcdnwebimgs.asus.com/gain/0466a73f-e382-4683-840b-75c84ccd9552/w800",
            ],
            ["ASUS ROG Zephyrus G14 GA403"] =
            [
                "https://dlcdnwebimgs.asus.com/gain/87DCDC3E-F5C1-4195-915A-3251D2AB06A1/w1200",
                "https://dlcdnwebimgs.asus.com/gain/D213D8B4-EBED-4A06-BF3D-B09F4F8B3A2B/w1200",
                "https://dlcdnwebimgs.asus.com/gain/6D242E91-1127-4073-A046-2692DB8CA2D4/w1200",
                "https://dlcdnwebimgs.asus.com/gain/E43F0084-F416-4D47-9ED8-DDF5F84FF32B/w1200",
                "https://dlcdnwebimgs.asus.com/gain/63F5E6BA-BC40-4680-98BF-06DF1673C6BC/w1200",
            ],
            ["Dell XPS 13 9340"] =
            [
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/notebooks/xps-notebooks/13-9340/media-gallery/silver/touch/notebook-xps-13-9340-t-sl-gallery-2.psd?fmt=png-alpha&wid=1200",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/notebooks/xps-notebooks/13-9340/media-gallery/silver/touch/notebook-xps-13-9340-t-sl-gallery-4.psd?fmt=png-alpha&wid=1200",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/notebooks/xps-notebooks/13-9340/media-gallery/silver/touch/notebook-xps-13-9340-t-sl-gallery-5.psd?fmt=png-alpha&wid=1200",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/notebooks/xps-notebooks/13-9340/media-gallery/silver/touch/notebook-xps-13-9340-t-sl-gallery-9.psd?fmt=png-alpha&wid=1200",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/notebooks/xps-notebooks/13-9340/media-gallery/silver/touch/notebook-xps-13-9340-t-sl-gallery-10.psd?fmt=png-alpha&wid=1200",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/notebooks/xps-notebooks/13-9340/media-gallery/silver/touch/notebook-xps-13-9340-t-sl-gallery-7.psd?fmt=png-alpha&wid=1200",
            ],
            ["Apple MacBook Air 13 inch M3"] =
            [
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mba13-midnight-select-202402?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mba13-starlight-select-202402?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mba13-spacegray-select-202402?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mba13-silver-select-202402?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mba13-midnight-witb-202402?wid=1200&hei=1200&fmt=jpeg&qlt=90",
            ],
            ["Lenovo ThinkPad X1 Carbon Gen 12"] =
            [
                "https://psref.lenovo.com/syspool/Sys/Image/ThinkPad/ThinkPad_X1_Carbon_Gen_12/ThinkPad_X1_Carbon_Gen_12_CT1_03.png",
                "https://psref.lenovo.com/syspool/Sys/Image/ThinkPad/ThinkPad_X1_Carbon_Gen_12/ThinkPad_X1_Carbon_Gen_12_CT1_01.png",
                "https://psref.lenovo.com/syspool/Sys/Image/ThinkPad/ThinkPad_X1_Carbon_Gen_12/ThinkPad_X1_Carbon_Gen_12_CT1_07.png",
                "https://psref.lenovo.com/syspool/Sys/Image/ThinkPad/ThinkPad_X1_Carbon_Gen_12/ThinkPad_X1_Carbon_Gen_12_CT1_09.png",
                "https://psref.lenovo.com/syspool/Sys/Image/ThinkPad/ThinkPad_X1_Carbon_Gen_12/ThinkPad_X1_Carbon_Gen_12_CT1_11.png",
                "https://psref.lenovo.com/syspool/Sys/Image/ThinkPad/ThinkPad_X1_Carbon_Gen_12/ThinkPad_X1_Carbon_Gen_12_CT1_05.png",
            ],
            ["Lenovo IdeaPad Slim 5 15IRU9"] =
            [
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Slim_5_15IRU9/IdeaPad_Slim_5_15IRU9_CT1_01.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Slim_5_15IRU9/IdeaPad_Slim_5_15IRU9_CT1_02.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Slim_5_15IRU9/IdeaPad_Slim_5_15IRU9_CT1_03.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Slim_5_15IRU9/IdeaPad_Slim_5_15IRU9_CT1_04.png",
            ],
            ["Apple MacBook Pro 14 inch M4"] =
            [
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mbp14-spaceblack-gallery1-202410?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mbp14-spaceblack-gallery2-202410?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mbp14-spaceblack-gallery3-202410?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mbp14-spaceblack-gallery4-202410?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/mbp14-silver-select-202410?wid=1200&hei=1200&fmt=jpeg&qlt=90",
            ],
            ["Lenovo Legion Pro 5 16IRX9"] =
            [
                "https://psref.lenovo.com/syspool/Sys/Image/Legion/Legion_Pro_5_16IRX9/Legion_Pro_5_16IRX9_CT1_03.png",
                "https://psref.lenovo.com/syspool/Sys/Image/Legion/Legion_Pro_5_16IRX9/Legion_Pro_5_16IRX9_CT1_01.png",
                "https://psref.lenovo.com/syspool/Sys/Image/Legion/Legion_Pro_5_16IRX9/Legion_Pro_5_16IRX9_CT1_04.png",
                "https://psref.lenovo.com/syspool/Sys/Image/Legion/Legion_Pro_5_16IRX9/Legion_Pro_5_16IRX9_CT1_05.png",
                "https://psref.lenovo.com/syspool/Sys/Image/Legion/Legion_Pro_5_16IRX9/Legion_Pro_5_16IRX9_CT1_07.png",
            ],
            ["Acer Nitro V 15 ANV15-51"] =
            [
                "https://static-ecpa.acer.com/media/catalog/product/a/c/acer-nitro-v-15-anv15-51-non-fingerprint-with-backlit-on-wp-black-01-1000x1000_nh.qqeaa.003.png",
                "https://static-ecpa.acer.com/media/catalog/product/a/c/acer-nitro-v-15-anv15-51-non-fingerprint-with-backlit-on-wp-black-02-1000x1000_nh.qqeaa.003.png",
                "https://static-ecpa.acer.com/media/catalog/product/a/c/acer-nitro-v-15-anv15-51-non-fingerprint-with-backlit-on-wp-black-04-1000x1000_nh.qqeaa.003.png",
                "https://static-ecpa.acer.com/media/catalog/product/a/c/acer-nitro-v-15-anv15-51-non-fingerprint-with-backlit-on-black-05-1000x1000_nh.qqeaa.003.png",
                "https://static-ecpa.acer.com/media/catalog/product/a/c/acer-nitro-v-15-anv15-51-non-fingerprint-with-backlit-black-06-1000x1000_nh.qqeaa.003.png",
                "https://static-ecpa.acer.com/media/catalog/product/a/c/acer-nitro-v-15-anv15-51-non-fingerprint-with-backlit-black-08-1000x1000_nh.qqeaa.003.png",
            ],
            ["Lenovo IdeaPad Flex 5 14IRU8"] =
            [
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Flex_5_14IRU8/IdeaPad_Flex_5_14IRU8_CT1_03.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Flex_5_14IRU8/IdeaPad_Flex_5_14IRU8_CT1_04.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Flex_5_14IRU8/IdeaPad_Flex_5_14IRU8_CT1_02.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Flex_5_14IRU8/IdeaPad_Flex_5_14IRU8_CT1_05.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Flex_5_14IRU8/IdeaPad_Flex_5_14IRU8_CT1_06.png",
                "https://psref.lenovo.com/syspool/Sys/Image/IdeaPad/IdeaPad_Flex_5_14IRU8/IdeaPad_Flex_5_14IRU8_CT1_07.png",
            ],
            ["Apple iPhone 15"] =
            [
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-15-finish-select-202309-6-1inch-blue?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-15-finish-select-202309-6-1inch-pink?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-15-finish-select-202309-6-1inch-yellow?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-15-finish-select-202309-6-1inch-black?wid=2400&hei=2400&fmt=jpeg&qlt=90",
            ],
            ["Samsung Galaxy S24"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/vn/2401/gallery/vn-galaxy-s24-sm-s921bzkcxxv-539304568",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/2401/gallery/vn-galaxy-s24-sm-s921bzvcxxv-539304693",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/2401/gallery/vn-galaxy-s24-sm-s921bzycxxv-539304778",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/2401/gallery/vn-galaxy-s24-sm-s921bzacxxv-539304473",
            ],
            ["Samsung Galaxy A55 5G"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-a556ezkaxxv/gallery/vn-galaxy-a55-5g-sm-a556-sm-a556ezkaxxv-540189658",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-a556elbaxxv/gallery/vn-galaxy-a55-5g-sm-a556-sm-a556elbaxxv-540189535",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-a556elvaxxv/gallery/vn-galaxy-a55-5g-sm-a556-sm-a556elvaxxv-540189593",
            ],
            ["Xiaomi Redmi Note 13 Pro 5G"] =
            [
                "https://i02.appmifile.com/855_operatorx_operatorx_opx/11/01/2024/4fad3c8039e37a4fa97f8be0462e88e5.png",
                "https://i02.appmifile.com/mi-com-product/fly-birds/redmi-note-13-pro-5g/pc/pro-5g.png",
                "https://i02.appmifile.com/mi-com-product/fly-birds/redmi-note-13-pro-5g/pc/black-bg.png",
                "https://i02.appmifile.com/mi-com-product/fly-birds/redmi-note-13-pro-5g/pc/teal-bg.png",
            ],
            ["Google Pixel 8"] =
            [
                "https://lh3.googleusercontent.com/aw4PrQvrABZAFao5WzLE89Ftic2d5KFp9i25qJVSMJAqhJ06c71Fw4DwisgKlj3K82awrLFiuYf4VPJfBX2Vzr2rDUdJDFAi2r10=s2000",
                "https://lh3.googleusercontent.com/E_TpiLFZ1qh6G8s1UwEIwEZCC8-P-8jvtqBalSdAhnP5tdqTxpBdwqsX_mbLwCzsaV6tKZeWTJC8j8WwfAUh-InUWHUdn8H8Loo=s2000",
                "https://lh3.googleusercontent.com/QtLV3C42wAw1debxUyksqKY5KV0Bq4JO2Fhp2Q3ly01nfrC24lwReoJUrU92FVdyOsvsY8CnZKtF-YRb8wBHwRyoJQBN62mApvQ=s2000",
                "https://lh3.googleusercontent.com/jYEje05UQPvKenwwRvSn_eJkdBU8m7YH4ik1A-GL-6DYTw5D1oVW3ixkNSHpC9ZiwnOngAm5CdN5P6UOn1LEAZEaokiP4Dysaw=s2000",
            ],
            ["Apple iPhone 13"] =
            [
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-13-finish-select-202207-6-1inch-blue?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-13-finish-select-202207-6-1inch-pink?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-13-finish-select-202207-6-1inch-midnight?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-13-finish-select-202207-6-1inch-starlight?wid=2400&hei=2400&fmt=jpeg&qlt=90",
            ],
            ["Apple iPhone 16 Pro"] =
            [
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-16-pro-finish-select-202409-6-3inch-naturaltitanium?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-16-pro-finish-select-202409-6-3inch-blacktitanium?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-16-pro-finish-select-202409-6-3inch-whitetitanium?wid=2400&hei=2400&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/iphone-16-pro-finish-select-202409-6-3inch-deserttitanium?wid=2400&hei=2400&fmt=jpeg&qlt=90",
            ],
            ["Samsung Galaxy Z Flip6"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-f741blbaxxv/gallery/vn-galaxy-zflip6-f741-513550-sm-f741blbaxxv-542637032",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-f741blgaxxv/gallery/vn-galaxy-zflip6-f741-513550-sm-f741blgaxxv-542637095",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-f741bzsaxxv/gallery/vn-galaxy-zflip6-f741-513550-sm-f741bzsaxxv-542637133",
                "https://images.samsung.com/is/image/samsung/p6pim/vn/sm-f741bzyaxxv/gallery/vn-galaxy-zflip6-f741-513550-sm-f741bzyaxxv-542637150",
            ],
            ["Xiaomi 14T Pro"] =
            [
                "https://i02.appmifile.com/27_item_vn/03/01/2025/0dbf7c3bc1129467ff82b31536dfcaed.png",
                "https://i02.appmifile.com/mi-com-product/fly-birds/xiaomi-14t-pro/PC/19.jpg",
                "https://i02.appmifile.com/mi-com-product/fly-birds/xiaomi-14t-pro/PC/bezel_bg.jpg",
                "https://i02.appmifile.com/mi-com-product/fly-birds/xiaomi-14t-pro/PC/carema_bg.jpg",
                "https://i02.appmifile.com/mi-com-product/fly-birds/xiaomi-14t-pro/PC/359.jpg",
            ],
            ["OPPO Reno12 5G"] =
            [
                "https://www.oppo.com/content/dam/oppo/product-asset-library/reno/reno12-series/en/reno12/assets/images-pack-I1407_5150_252_3695-1.png",
                "https://www.oppo.com/content/dam/oppo/product-asset-library/reno/reno12-series/en/reno12/assets/images-pack-1243_3551_s3-1.png",
                "https://www.oppo.com/content/dam/oppo/product-asset-library/reno/reno12-series/en/reno12/assets/images-pack-2766_4180-1.jpg",
                "https://www.oppo.com/content/dam/oppo/product-asset-library/reno/reno12-series/en/reno12/assets/images-pack-2766_4181-1.jpg",
                "https://www.oppo.com/content/dam/oppo/product-asset-library/reno/reno12-series/en/reno12/assets/images-pack-1241_2599-1.jpg",
            ],
            ["Sony WH-1000XM5"] =
            [
                "https://d1ncau8tqf99kp.cloudfront.net/360/M5_360view/WF-1000XM5_black_3600010_web.png",
                "https://d1ncau8tqf99kp.cloudfront.net/360/M5_360view/WF-1000XM5_black_3600000_web.png",
                "https://d1ncau8tqf99kp.cloudfront.net/360/M5_360view/WF-1000XM5_black_3600020_web.png",
                "https://d1ncau8tqf99kp.cloudfront.net/360/M5_360view/WF-1000XM5_black_3600030_web.png",
            ],
            ["Apple AirPods Pro (thế hệ 2)"] =
            [
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/MTJV3?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/MTJV3_AV1?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/MTJV3_AV2?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/MTJV3_AV3?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/MTJV3_AV4?wid=1200&hei=1200&fmt=jpeg&qlt=90",
            ],
            ["Samsung Galaxy Buds3 Pro"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/ph/2407/gallery/ph-galaxy-buds3-pro-r630-sm-r630nzwaasa-542136399?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ph/2407/gallery/ph-galaxy-buds3-pro-r630-sm-r630nzwaasa-542136400?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ph/2407/gallery/ph-galaxy-buds3-pro-r630-sm-r630nzwaasa-542136401?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ph/2407/gallery/ph-galaxy-buds3-pro-r630-sm-r630nzwaasa-542136402?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ph/2407/gallery/ph-galaxy-buds3-pro-r630-sm-r630nzwaasa-542136406?$Q90_1368_1094_F_JPG$",
            ],
            ["Bose QuietComfort 45"] =
            [
                "https://assets.bosecreative.com/transform/788fa4e9-26f2-4c34-ba4b-be028d456603/QC45_WhiteSmoke_001_RGB",
                "https://assets.bosecreative.com/transform/a03cf976-47d0-46dd-8973-4d08c8aea2ab/QC45_WhiteSmoke_004_RGB",
                "https://assets.bosecreative.com/transform/a4c08137-92c7-4833-ad68-9d3cf13c668f/QC45_WhiteSmoke_003_RGB",
                "https://assets.bosecreative.com/transform/27af6d95-87ee-41a9-a04e-5b856e6fe411/QC45_WhiteSmoke_4514_RGB",
                "https://assets.bosecreative.com/transform/d73f23b9-207e-4c6b-b914-d50accd3557d/QC45_TripleBlk_1360_RGB",
            ],
            ["JBL Tune 520BT"] =
            [
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw243e33a8/01.JBL_Tune_520BT_ProductImage_Hero_Blue.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw1caa8f25/02.JBL_Tune%20520BT_Product%20Image_Front_Blue.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dwd8b3a69b/04.JBL_Tune%20520BT_Product%20Image_Left_Blue.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw3befb739/06.JBL_Tune%20520BT_Product%20Image_Fold_Blue.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dwcf320ff6/pdp/JBL_Tune_520BT_Lifestyle_Image_White_904x560.png",
            ],
            ["Samsung Galaxy Buds2 Pro"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/ca/2208/gallery/ca-galaxy-buds2-pro-r510-sm-r510nzaaxac-533189030?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ca/2208/gallery/ca-galaxy-buds2-pro-r510-sm-r510nzaaxac-533189015?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ca/2208/gallery/ca-galaxy-buds2-pro-r510-sm-r510nzaaxac-533189016?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ca/2208/gallery/ca-galaxy-buds2-pro-r510-sm-r510nzaaxac-533189017?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/ca/2208/gallery/ca-galaxy-buds2-pro-r510-sm-r510nzaaxac-533189018?$Q90_1368_1094_F_JPG$",
            ],
            ["Apple AirPods 4"] =
            [
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/airpods-4-select-202409?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/airpods-4-anc-select-202409?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/airpods-4-202409-gallery-1?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/airpods-4-202409-gallery-2?wid=1200&hei=1200&fmt=jpeg&qlt=90",
                "https://store.storeimages.cdn-apple.com/4668/as-images.apple.com/is/airpods-4-202409-gallery-3?wid=1200&hei=1200&fmt=jpeg&qlt=90",
            ],
            ["JBL Tune 770NC"] =
            [
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw679be5de/1.JBL_Tune_770NC_Product%20Image_Hero_White.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dwdd461b7a/2.JBL_Tune_770NC_Product%20Image_Front_White.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw82dbef21/4.JBL_Tune_770NC_Product%20Image_Left_White.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw81a72903/6.JBL_Tune_770NC_Product%20Image_Folded_White.png",
                "https://www.jbl.com/dw/image/v2/BFND_PRD/on/demandware.static/-/Sites-masterCatalog_Harman/default/dw76414fd9/pdp/JBL_Tune_770NC_Lifestyle_Image_01_904x560.png",
            ],
            ["Sennheiser Momentum 4 Wireless"] =
            [
                "https://us.sennheiser-hearing.com/cdn/shop/files/MOMENTUM_4_Black.jpg",
                "https://us.sennheiser-hearing.com/cdn/shop/files/M4_Black_Case.jpg",
                "https://us.sennheiser-hearing.com/cdn/shop/files/Sennheiser_MOMENTUM_4_Wireless_Denim_Bluetooth_Headphones_Side_View.jpg",
                "https://us.sennheiser-hearing.com/cdn/shop/files/Sennheiser_MOMENTUM_4_Wireless_Denim_Bluetooth_Headphones_Lifestyle_Image.jpg",
                "https://us.sennheiser-hearing.com/cdn/shop/files/M4_Graphite_Side.jpg",
            ],
            ["Soundcore Liberty 4 NC"] =
            [
                "https://cdn.shopify.com/s/files/1/0519/2355/0368/files/3947Z11-Photoroom.png",
                "https://cdn.shopify.com/s/files/1/0733/1171/5555/files/A3947G21_TD01_V1_1200x_76aa753e-155c-4b98-80b7-cfdeaa0fe7ca.webp",
                "https://cdn.shopify.com/s/files/1/0733/1171/5555/files/A3947G31_TD01_V1_1200x_deef870f-0701-425e-a708-306ab82e4da7.webp",
                "https://cdn.shopify.com/s/files/1/0733/1171/5555/files/A3947G51_TD01_V1_1200x_f5a16697-17f8-46df-89eb-96ce6b618237.webp",
                "https://cdn.shopify.com/s/files/1/0733/1171/5555/files/A3947G61_TD01_V1_1200x_8b8c0b75-4671-4cb5-ab2a-0b5ac691341d.webp",
            ],
            ["LG UltraGear 27GP850-B"] =
            [
                "https://media.us.lg.com/transform/ecomm-PDPGalleryZoom-1600x1062/0d1e62da-95ec-40e6-b2a3-0ef94b03aea3/md08000370-Z-01-jpg",
                "https://media.us.lg.com/transform/ecomm-PDPGalleryZoom-1600x1062/dc491d40-d262-4cf2-8d02-32dacf57523d/md08000370-Z-04-jpg",
                "https://media.us.lg.com/transform/ecomm-PDPGalleryZoom-1600x1062/3b40daa7-01dc-4cb6-8655-3d10ca3dafd1/md08000370-Z-05-jpg",
                "https://media.us.lg.com/transform/ecomm-PDPGalleryZoom-1600x1062/85455ee4-15d2-4f5e-b7d2-491de5a9c23f/md08000370-Z-08-jpg",
            ],
            ["Dell UltraSharp U2723QE"] =
            [
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/u-series/u2723qe/media-gallery/monitor-u2723qe-gallery-1.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/u-series/u2723qe/spi/ng/monitor-u2723qe-campaign-hero-504x350-ng.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/u-series/u2723qe/media-gallery/monitor-u2723qe-gallery-6.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/u-series/u2723qe/media-gallery/monitor-u2723qe-gallery-9.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/u-series/u2723qe/pdp/monitor-u2723qe-pdp-module-8.psd?fmt=png-alpha&hei=1000",
            ],
            ["Samsung Odyssey G5 LC27G55T"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/sa_en/lc27g55tqwmxue/gallery/sa-en-odyssey-g5-g55t-lc27g55tqwmxue-371721906",
                "https://images.samsung.com/is/image/samsung/p6pim/sa_en/lc27g55tqwmxue/gallery/sa-en-odyssey-g5-g55t-lc27g55tqwmxue-371721902",
                "https://images.samsung.com/is/image/samsung/p6pim/sa_en/lc27g55tqwmxue/gallery/sa-en-odyssey-g5-g55t-lc27g55tqwmxue-371721901",
            ],
            ["AOC 24G2"] =
            [
                "https://cdn.sanity.io/images/hf5b3axp/production/d711ca9aaf71f961f8ab64eff8d6624d4d532839-3840x3840.png?w=1600&fit=max&auto=format",
                "https://cdn.sanity.io/images/hf5b3axp/production/b45f4682910f387c5128b64314e250cc3f426c5d-3840x3840.png?w=1600&fit=max&auto=format",
                "https://cdn.sanity.io/images/hf5b3axp/production/975b1a5367bddffbae1439cb343fb60e54032f5c-6238x4990.png?w=1600&fit=max&auto=format",
                "https://cdn.sanity.io/images/hf5b3axp/production/9b3c6d33a01f64e940ce8d1a07f249282c426453-7065x5652.png?w=1600&fit=max&auto=format",
                "https://cdn.sanity.io/images/hf5b3axp/production/38c3da7ca39014684e0aa1f1869c99d893866e3b-6432x5145.png?w=1600&fit=max&auto=format",
            ],
            ["Dell P2422H"] =
            [
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/p-series/p2422h/media-gallery/peripherals_monitor_p2422h_gallery_1.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/p-series/p2422h/media-gallery/peripherals_monitor_p2422h_gallery_2.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/p-series/p2422h/media-gallery/peripherals_monitor_p2422h_gallery_6.psd?fmt=png-alpha&hei=1000",
                "https://i.dell.com/is/image/DellContent/content/dam/ss2/product-images/dell-client-products/peripherals/monitors/p-series/p2422h/media-gallery/peripherals_monitor_p2422h_gallery_3.psd?fmt=png-alpha&hei=1000",
            ],
            ["MSI Optix MAG274QRF-QD"] =
            [
                "https://storage-asset.msi.com/global/picture/product/product_0_20200909170938_5f589bd21b624.webp",
                "https://storage-asset.msi.com/global/picture/product/product_1_20200909170941_5f589bd53a487.webp",
                "https://storage-asset.msi.com/global/picture/product/product_1_20200909170935_5f589bcf1f1c2.webp",
                "https://storage-asset.msi.com/global/picture/product/product_1_20200909170932_5f589bcc1af75.webp",
            ],
            ["LG UltraGear 27GR95QE-B"] =
            [
                "https://media.us.lg.com/transform/c6fc912d-f378-4972-83f7-852672fa11bd/Monitor_27GR95QE-B_gallery_01_5000x5000",
                "https://media.us.lg.com/transform/5a674e43-680c-4027-933c-bd71ee8d94ac/Monitor_27GR95QE-B_gallery_02_5000x5000",
                "https://media.us.lg.com/transform/09cf7baa-1482-45d3-ba9e-65bba31d4560/Monitor_27GR95QE-B_gallery_03_5000x5000",
                "https://media.us.lg.com/transform/271736b1-c256-45d6-9e92-335de8d610d4/Monitor_27GR95QE-B_gallery_06_3000x3000",
                "https://media.us.lg.com/transform/e2a2934e-233b-454f-a8a6-6f959cc98004/Monitor_27GR95QE-B_gallery_04_3000x3000",
            ],
            ["ASUS ProArt PA278CV"] =
            [
                "https://dlcdnwebimgs.asus.com/gain/f0752c79-06cf-459c-af9c-ca36c0ebd392/w1200",
                "https://dlcdnwebimgs.asus.com/gain/fb3dd1bf-e59d-4381-8084-9cad23043d1a/w1200",
                "https://dlcdnwebimgs.asus.com/gain/d2779d23-e2ae-44fa-8d11-2d95d774c8f2/w1200",
                "https://dlcdnwebimgs.asus.com/files/media/c809b1a3-02c4-4a50-a1d1-a537d84998cf/images/pd_PA278CV.png",
            ],
            ["ASUS TUF Gaming VG249Q1A"] =
            [
                "https://dlcdnwebimgs.asus.com/gain/a7362b7f-5e02-45f1-af32-7f3e950aa45e/w1200",
                "https://dlcdnwebimgs.asus.com/gain/c3b569b5-5985-4d3c-81a1-7a18a9eeef6b/w1200",
                "https://dlcdnwebimgs.asus.com/gain/0f372e3e-f38e-4a9b-824a-978bc7689a99/w1200",
                "https://dlcdnwebimgs.asus.com/gain/1a057d5c-89a1-45c8-8fd2-26847c8061a6/w1200",
                "https://dlcdnwebimgs.asus.com/gain/4ae49d0d-8860-4083-9571-baa52d727d70/w1200",
            ],
            ["Gigabyte M27Q"] =
            [
                "https://static.gigabyte.com/StaticFile/Image/Global/bcccc80496581f937b24350550f4039d/ProductRemoveBg/34243/webp/origin",
                "https://static.gigabyte.com/StaticFile/Image/Global/b348d8d6fbfd8f12c9d0e3c540b36fb5/ProductRemoveBg/34244/webp/origin",
                "https://static.gigabyte.com/StaticFile/Image/Global/edf9f9c88ba863fe7e2fb09a50376a8c/ProductRemoveBg/34245/webp/origin",
            ],
            ["Logitech MX Master 3S"] =
            [
                "https://resource.logitech.com/content/dam/logitech/en/products/mice/mx-master-3s/2025-update/mx-master-3s-bluetooth-edition-top-view-graphite-new-1.png",
                "https://resource.logitech.com/content/dam/logitech/en/products/mice/mx-master-3s/2025-update/mx-master-3s-bluetooth-edition-left-front-view-graphite-new-8.png",
                "https://resource.logitech.com/content/dam/logitech/en/products/mice/mx-master-3s/2025-update/mx-master-3s-bluetooth-edition-left-profile-view-graphite-new-4.png",
                "https://resource.logitech.com/content/dam/logitech/en/products/mice/mx-master-3s/2025-update/mx-master-3s-bluetooth-edition-bottom-view-graphite-new-5.png",
                "https://resource.logitech.com/content/dam/logitech/en/products/mice/mx-master-3s/2025-update/mx-master-3s-bluetooth-edition-rear-view-graphite-new-7.png",
            ],
            ["Logitech MX Keys S"] =
            [
                "https://resource.logitech.com/content/dam/logitech/en/products/keyboards/mx-keys-s/product-gallery/graphite/mx-keys-s-keyboard-3qtr-graphite-us.png",
                "https://resource.logitech.com/content/dam/logitech/en/products/keyboards/mx-keys-s/product-gallery/graphite/mx-keys-s-keyboard-side-graphite-us.png",
                "https://resource.logitech.com/content/dam/logitech/en/products/keyboards/mx-keys-s/product-gallery/graphite/mx-keys-s-keyboard-bottom-graphite-us.png",
            ],
            ["Anker 735 Charger (GaNPrime 65W)"] =
            [
                "https://cdn.shopify.com/s/files/1/0489/1980/6120/products/A2668311_TD01_V1.jpg",
                "https://cdn.shopify.com/s/files/1/0493/9834/9974/files/A2668111_TD01_V1.jpg",
                "https://cdn.shopify.com/s/files/1/0765/2137/3999/files/A2668T21_TD01_V1.jpg",
            ],
            ["Anker PowerLine III USB-C to USB-C 2.0 Cable (1.8 m)"] =
            [
                "https://cdn.shopify.com/s/files/1/0765/2137/3999/files/black-01_06b39643-e948-495d-ba00-c79e104ee4e7_2878x_1.png",
                "https://cdn.shopify.com/s/files/1/0508/1815/4652/products/1_1cecb0d1-6d26-4f65-ae66-c23502e51f83.jpg",
                "https://cdn.shopify.com/s/files/1/0508/1815/4652/products/black-07.jpg",
            ],
            ["Razer DeathAdder V3"] =
            [
                "https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/h72/h48/9481236086814/230221-da-v3-1500X1000-2.jpg",
                "https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/h19/h4b/9481236119582/230221-da-v3-1500X1000-4.jpg",
                "https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/h81/h4e/9481236217886/230221-da-v3-1500X1000-3.jpg",
                "https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/hb5/h26/9481442721822/230221-da-v3-2-1500X1000-5.jpg",
                "https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/h42/h4f/9481236283422/230221-da-v3-1500X1000-6.jpg",
                "https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/hc4/h47/9481236021278/230221-da-v3-1500X1000-1.jpg",
            ],
            ["Keychron K2 V2"] =
            [
                "https://cdn.shopify.com/s/files/1/0059/0630/1017/products/Keychron-K2-wireless-mechanical-keyboard-for-Mac-Windows-iOS-Gateron-switch-red-with-type-C-RGB-white-backlight-exclusive-color.jpg?v=1650445595",
                "https://cdn.shopify.com/s/files/1/0059/0630/1017/products/Keychron-K2-wireless-mechanical-keyboard-for-Mac-Windows-iOS-Gateron-switch-red-with-type-C-RGB-white-backlight_9cb9d3e6-a5ac-4ac5-becb-079c7103ed2f.jpg?v=1650445595",
                "https://cdn.shopify.com/s/files/1/0059/0630/1017/products/Keychron-K2-wireless-mechanical-keyboard-for-Mac-Windows-iOS-Gateron-switch-red-with-type-C-RGB-white-backlight-aluminum-frame.jpg?v=1650445549",
            ],
            ["Logitech G Pro X Superlight 2"] =
            [
                "https://resource.logitechg.com/content/dam/gaming/en/products/pro-x-superlight-2/new-gallery-assets-2025/pro-x-superlight-2-mice-top-angle-black-gallery-1.png",
                "https://resource.logitechg.com/content/dam/gaming/en/products/pro-x-superlight-2/new-gallery-assets-2025/pro-x-superlight-2-mice-profile-left-angle-black-gallery-4-2-award.png",
                "https://resource.logitechg.com/content/dam/gaming/en/products/pro-x-superlight-2/new-gallery-assets-2025/pro-x-superlight-2-mice-profile-right-angle-black-gallery-5.png",
                "https://resource.logitechg.com/content/dam/gaming/en/products/pro-x-superlight-2/new-gallery-assets-2025/pro-x-superlight-2-mice-top-angle-white-gallery-1.png",
            ],
            ["Apple Magic Keyboard (USB-C)"] =
            [
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/MJLX4?wid=1000&hei=1000&fmt=jpeg&qlt=95&.v=1785993102206",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/MJLX4_AV1?wid=1000&hei=1000&fmt=jpeg&qlt=95&.v=1785993070617",
                "https://store.storeimages.cdn-apple.com/1/as-images.apple.com/is/MJLX4_AV2?wid=1000&hei=1000&fmt=jpeg&qlt=95&.v=1785993076712",
            ],
            ["Samsung 45W Power Adapter EP-T4510"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/africa_en/ep-t4510xbegww/gallery/africa-en-45w-power-adapter-ep-t4510-ep-t4510xbegww-536810073?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/africa_en/ep-t4510xbegww/gallery/africa-en-45w-power-adapter-ep-t4510-ep-t4510xbegww-536810077?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/africa_en/ep-t4510xbegww/gallery/africa-en-45w-power-adapter-ep-t4510-ep-t4510xbegww-536810074?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/africa_en/ep-t4510xbegww/gallery/africa-en-45w-power-adapter-ep-t4510-ep-t4510xbegww-536810078?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/africa_en/ep-t4510xbegww/gallery/africa-en-45w-power-adapter-ep-t4510-ep-t4510xbegww-536810072?$Q90_1368_1094_F_JPG$",
            ],
            ["Samsung USB-C to USB-C Cable 5A (1.8 m)"] =
            [
                "https://images.samsung.com/is/image/samsung/p6pim/uk/ep-dx510jbegeu/gallery/uk-usb-cable-5a-ep-dx510jbegeu-532724892?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/uk/ep-dx510jbegeu/gallery/uk-usb-cable-5a-ep-dx510jbegeu-532724886?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/uk/ep-dx510jbegeu/gallery/uk-usb-cable-5a-ep-dx510jbegeu-532724887?$Q90_1368_1094_F_JPG$",
                "https://images.samsung.com/is/image/samsung/p6pim/uk/ep-dx510jbegeu/gallery/uk-usb-cable-5a-ep-dx510jbegeu-532724885?$Q90_1368_1094_F_JPG$",
            ],
        };
}
