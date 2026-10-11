import type { BannerIconName } from './banner-icons';

// Mỗi banner có bộ màu và họa tiết riêng trong `home-page.css`. Màu nằm trong `tokens.css`.
export type BannerTheme =
  'iphone' | 'duo' | 's26' | 'fold' | 'flip' | 'rog' | 'sony' | 'xiaomi' | 'dell';

// Hãng có logo dạng mask trong CSS. Logo lấy từ `public/media/brands/`.
export type BannerBrand = 'apple' | 'samsung' | 'asus' | 'sony' | 'xiaomi' | 'dell';

export interface BannerSpec {
  icon: BannerIconName;
  label: string;
  value: string;
}

export interface BannerContent {
  id: string;
  theme: BannerTheme;
  // Banner nền tối dùng chữ trắng.
  isDark: boolean;
  // Tên và loại máy trên tab chọn banner.
  tabName: string;
  tabNote: string;
  // Máy vừa ra mắt có nhãn "Mới" trên tab và dòng ngày mở bán trên banner.
  launchNote: string | null;
  brand: BannerBrand;
  brandName: string;
  title: string;
  // Tên máy thứ hai của cùng dòng, in nhỏ dưới tiêu đề.
  subtitle: string | null;
  tagline: string;
  specs: BannerSpec[];
  // Lớp CSS của chấm màu máy. Mảng rỗng thì banner không có dòng màu.
  swatches: string[];
  colorNote: string | null;
  priceLabel: string;
  // Slug trong catalog Pickory. Banner lấy giá và bảo hành từ API theo slug này.
  // Null khi máy chưa bán tại Pickory: banner hiện giá niêm yết của hãng và nhãn "Sắp có".
  productSlug: string | null;
  // Giá niêm yết của hãng tại Việt Nam, chỉ dùng khi máy chưa có trong catalog.
  listedPrice: number | null;
  // Ngày hãng nhận đặt trước. Banner đếm ngược tới ngày này và ẩn đồng hồ khi đã qua.
  preorderAt: string | null;
  // Ảnh cảnh nền phủ nửa phải banner. Dùng cho ảnh quảng cáo có sẵn bối cảnh của hãng.
  scenePhotoSrc: string | null;
  // Ảnh máy đã tách nền, xếp chồng từ sau ra trước. Rê chuột thì các máy tách ra.
  cutoutSrcs: string[];
  tabThumbSrc: string;
}

// Ảnh gốc trong catalog Pickory. Nơi dùng tự thêm chiều rộng theo vị trí.
const SEED_IMAGE_BASE = 'https://res.cloudinary.com/srof524r/image/upload/pickory/products/seed';

function seedImage(productSlug: string, imageNumber: number): string {
  return `${SEED_IMAGE_BASE}/${productSlug}-${imageNumber}`;
}

/*
 * Bốn banner đầu là máy mới ra mắt, chưa có trong catalog.
 * Giá, thông số và màu lấy từ trang Apple Việt Nam và Samsung Việt Nam, tháng 10/2026.
 * Ảnh trong `public/media/banners/` là ảnh quảng cáo của hãng. Trước khi chạy thật, thay bằng ảnh có quyền dùng.
 */
export const HOME_BANNERS: BannerContent[] = [
  {
    id: 'iphone-18-pro',
    theme: 'iphone',
    isDark: true,
    tabName: 'iPhone 18 Pro | Pro Max',
    tabNote: 'Điện thoại',
    launchNote: 'Mới ra mắt · Mở bán từ 19/9/2026',
    brand: 'apple',
    brandName: 'Apple',
    title: 'iPhone 18 Pro',
    subtitle: 'iPhone 18 Pro Max',
    tagline: 'Chip A20 Pro · Camera khẩu độ biến thiên',
    specs: [
      { icon: 'phone', label: 'Màn hình ProMotion 120 Hz', value: '6,3 inch | 6,9 inch' },
      { icon: 'camera', label: 'Camera chính', value: '48 MP, ƒ/1.48 đến ƒ/4.0' },
      { icon: 'chip', label: 'Vi xử lý 2 nm', value: 'A20 Pro' },
      { icon: 'battery', label: 'Xem video (Pro Max)', value: 'Tới 43 giờ' },
    ],
    swatches: ['iphone-black', 'iphone-silver', 'iphone-blue', 'iphone-burgundy'],
    colorNote: 'Đen, Bạc, Băng Thanh, Đỏ Burgundy',
    priceLabel: 'Giá hãng từ (256 GB)',
    productSlug: null,
    listedPrice: 38_999_000,
    preorderAt: null,
    scenePhotoSrc: '/media/banners/iphone-18-pro-colors.jpg',
    cutoutSrcs: [],
    tabThumbSrc: '/media/banners/iphone-18-pro-burgundy.jpg',
  },
  {
    id: 'iphone-duo',
    theme: 'duo',
    isDark: false,
    tabName: 'iPhone Duo',
    tabNote: 'Sắp mở bán',
    launchNote: 'Sắp mở bán · Đặt trước từ 16/10/2026',
    brand: 'apple',
    brandName: 'Apple',
    title: 'iPhone Duo',
    subtitle: null,
    tagline: 'iPhone gập · Vỏ titan cấp 5',
    specs: [
      { icon: 'fold', label: 'Màn hình trong', value: 'Gần 8 inch' },
      { icon: 'shield', label: 'Vỏ máy', value: 'Titan cấp 5' },
      { icon: 'battery', label: 'Pin', value: 'Hệ thống pin kép' },
      { icon: 'touch', label: 'Bảo mật', value: 'Touch ID cạnh bên' },
    ],
    swatches: [],
    colorNote: null,
    priceLabel: 'Giá hãng từ (256 GB)',
    productSlug: null,
    listedPrice: 64_999_000,
    preorderAt: '2026-10-16T00:00:00+07:00',
    scenePhotoSrc: null,
    cutoutSrcs: ['/media/banners/iphone-duo.jpg'],
    tabThumbSrc: '/media/banners/iphone-duo.jpg',
  },
  {
    id: 'galaxy-s26-ultra',
    theme: 's26',
    isDark: true,
    tabName: 'Galaxy S26 Ultra',
    tabNote: 'Điện thoại',
    launchNote: 'Mới ra mắt · 2026',
    brand: 'samsung',
    brandName: 'Samsung',
    title: 'Galaxy S26 Ultra',
    subtitle: null,
    tagline: 'Camera 200 MP · Privacy Display',
    specs: [
      { icon: 'cpu', label: 'Vi xử lý', value: 'Snapdragon 8 Elite Gen 5 for Galaxy' },
      { icon: 'phone', label: 'Màn hình', value: '6,9 inch' },
      { icon: 'camera', label: 'Camera góc rộng', value: '200 MP, f/1.4' },
      { icon: 'battery', label: 'Pin', value: '5.000 mAh' },
    ],
    swatches: ['s26-black', 's26-white', 's26-violet', 's26-blue', 's26-pink'],
    colorNote: '5 màu',
    priceLabel: 'Giá hãng từ (256 GB)',
    productSlug: null,
    listedPrice: 40_990_000,
    preorderAt: null,
    scenePhotoSrc: null,
    cutoutSrcs: ['/media/banners/galaxy-s26-ultra-sky-blue.jpg'],
    tabThumbSrc: '/media/banners/galaxy-s26-ultra-sky-blue.jpg',
  },
  {
    id: 'galaxy-z-fold8',
    theme: 'fold',
    isDark: true,
    tabName: 'Galaxy Z Fold8',
    tabNote: 'Điện thoại gập',
    launchNote: 'Mới ra mắt · Mở bán từ 8/2026',
    brand: 'samsung',
    brandName: 'Samsung',
    title: 'Galaxy Z Fold8',
    subtitle: null,
    tagline: 'Mỏng 4,5 mm · Nặng 201 g',
    specs: [
      { icon: 'fold', label: 'Màn hình', value: '7,6 inch mở, 5,5 inch gập' },
      { icon: 'cpu', label: 'Vi xử lý', value: 'Snapdragon 8 Elite Gen 5' },
      { icon: 'camera', label: 'Camera góc rộng', value: '50 MP' },
      { icon: 'battery', label: 'Pin', value: '4.800 mAh' },
    ],
    swatches: ['fold-mint', 'fold-lavender', 'fold-graphite', 'fold-cream'],
    colorNote: '4 màu',
    priceLabel: 'Giá hãng từ (256 GB)',
    productSlug: null,
    listedPrice: 46_990_000,
    preorderAt: null,
    scenePhotoSrc: '/media/banners/galaxy-z-fold8-design.jpg',
    cutoutSrcs: [],
    tabThumbSrc: '/media/banners/galaxy-z-fold8-design.jpg',
  },
  {
    id: 'galaxy-z-flip6',
    theme: 'flip',
    isDark: false,
    tabName: 'Galaxy Z Flip6',
    tabNote: 'Điện thoại',
    launchNote: null,
    brand: 'samsung',
    brandName: 'Samsung',
    title: 'Galaxy Z Flip6',
    subtitle: null,
    tagline: 'Gập vỏ sò · Snapdragon 8 Gen 3',
    specs: [
      { icon: 'phone', label: 'Màn hình chính', value: '6,7 inch' },
      { icon: 'memory', label: 'RAM', value: '12 GB' },
      { icon: 'storage', label: 'Bộ nhớ trong', value: '256 GB' },
      { icon: 'battery', label: 'Pin', value: '4.000 mAh' },
    ],
    swatches: ['flip-silver', 'flip-yellow', 'flip-mint'],
    colorNote: 'Bạc, Vàng, Xanh bạc hà',
    priceLabel: 'Giá từ',
    productSlug: 'samsung-galaxy-z-flip6',
    listedPrice: null,
    preorderAt: null,
    scenePhotoSrc: null,
    cutoutSrcs: [
      seedImage('samsung-galaxy-z-flip6', 3),
      seedImage('samsung-galaxy-z-flip6', 4),
      seedImage('samsung-galaxy-z-flip6', 2),
    ],
    tabThumbSrc: seedImage('samsung-galaxy-z-flip6', 1),
  },
  {
    id: 'rog-zephyrus-g14',
    theme: 'rog',
    isDark: true,
    tabName: 'ROG Zephyrus G14',
    tabNote: 'Laptop',
    launchNote: null,
    brand: 'asus',
    brandName: 'ASUS',
    title: 'ROG Zephyrus G14',
    subtitle: null,
    tagline: 'Laptop gaming · OLED 3K 120 Hz',
    specs: [
      { icon: 'cpu', label: 'Vi xử lý', value: 'AMD Ryzen 9 8945HS' },
      { icon: 'memory', label: 'RAM', value: '32 GB' },
      { icon: 'screen', label: 'Màn hình', value: '14 inch OLED' },
      { icon: 'weight', label: 'Khối lượng', value: '1,5 kg' },
    ],
    swatches: ['rog-white', 'rog-grey'],
    colorNote: 'Trắng Platinum, Xám Eclipse',
    priceLabel: 'Giá từ',
    productSlug: 'asus-rog-zephyrus-g14-ga403',
    listedPrice: null,
    preorderAt: null,
    scenePhotoSrc: null,
    cutoutSrcs: [
      seedImage('asus-rog-zephyrus-g14-ga403', 2),
      seedImage('asus-rog-zephyrus-g14-ga403', 1),
    ],
    tabThumbSrc: seedImage('asus-rog-zephyrus-g14-ga403', 1),
  },
  {
    id: 'sony-wh-1000xm5',
    theme: 'sony',
    isDark: false,
    tabName: 'Sony WH-1000XM5',
    tabNote: 'Tai nghe',
    launchNote: null,
    brand: 'sony',
    brandName: 'Sony',
    title: 'WH-1000XM5',
    subtitle: null,
    tagline: 'Chống ồn chủ động · Pin 30 giờ',
    specs: [
      { icon: 'noise', label: 'Chống ồn', value: 'Chủ động (ANC)' },
      { icon: 'clock', label: 'Thời lượng pin', value: 'Tới 30 giờ' },
      { icon: 'bluetooth', label: 'Kết nối', value: 'Bluetooth 5.2' },
      { icon: 'headphones', label: 'Kiểu tai nghe', value: 'Chụp tai' },
    ],
    swatches: [],
    colorNote: null,
    priceLabel: 'Giá từ',
    productSlug: 'sony-wh-1000xm5',
    listedPrice: null,
    preorderAt: null,
    scenePhotoSrc: null,
    cutoutSrcs: [seedImage('sony-wh-1000xm5', 3), seedImage('sony-wh-1000xm5', 1)],
    tabThumbSrc: seedImage('sony-wh-1000xm5', 1),
  },
  {
    id: 'xiaomi-14t-pro',
    theme: 'xiaomi',
    isDark: true,
    tabName: 'Xiaomi 14T Pro',
    tabNote: 'Điện thoại',
    launchNote: null,
    brand: 'xiaomi',
    brandName: 'Xiaomi',
    title: 'Xiaomi 14T Pro',
    subtitle: null,
    tagline: 'Camera Leica · Sạc 120 W',
    specs: [
      { icon: 'phone', label: 'Màn hình AMOLED', value: '6,67 inch · 144 Hz' },
      { icon: 'camera', label: 'Camera', value: 'Ống kính Leica' },
      { icon: 'battery', label: 'Pin', value: '5.000 mAh' },
      { icon: 'bolt', label: 'Sạc nhanh', value: '120 W' },
    ],
    swatches: [],
    colorNote: null,
    priceLabel: 'Giá từ',
    productSlug: 'xiaomi-14t-pro',
    listedPrice: null,
    preorderAt: null,
    scenePhotoSrc: null,
    cutoutSrcs: [seedImage('xiaomi-14t-pro', 1)],
    tabThumbSrc: seedImage('xiaomi-14t-pro', 1),
  },
  {
    id: 'dell-xps-13',
    theme: 'dell',
    isDark: false,
    tabName: 'Dell XPS 13 9340',
    tabNote: 'Laptop',
    launchNote: null,
    brand: 'dell',
    brandName: 'Dell',
    title: 'XPS 13 9340',
    subtitle: null,
    tagline: 'Vỏ nhôm nguyên khối · Core Ultra 7',
    specs: [
      { icon: 'cpu', label: 'Vi xử lý', value: 'Intel Core Ultra 7 155H' },
      { icon: 'memory', label: 'RAM', value: '16 GB' },
      { icon: 'screen', label: 'Màn hình', value: '13,4 inch' },
      { icon: 'weight', label: 'Khối lượng', value: '1,2 kg' },
    ],
    swatches: [],
    colorNote: null,
    priceLabel: 'Giá từ',
    productSlug: 'dell-xps-13-9340',
    listedPrice: null,
    preorderAt: null,
    scenePhotoSrc: null,
    cutoutSrcs: [seedImage('dell-xps-13-9340', 3), seedImage('dell-xps-13-9340', 2)],
    tabThumbSrc: seedImage('dell-xps-13-9340', 1),
  },
];
