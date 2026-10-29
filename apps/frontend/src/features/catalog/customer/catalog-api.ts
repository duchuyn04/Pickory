// Kiểu dữ liệu của `GET /api/products` và `GET /api/categories`. Tên trường khớp response của backend.

export interface ProductThumbnail {
  publicId: string;
  alt: string;
  // URL gốc trên Cloudinary, chưa có tham số biến đổi. Giao diện thêm chiều rộng cho từng vị trí.
  url: string;
}

export interface ProductBrandSummary {
  id: string;
  name: string;
  slug: string;
}

export interface ProductListItem {
  id: string;
  slug: string;
  name: string;
  brand: ProductBrandSummary;
  minPrice: number | null;
  inStock: boolean;
  status: string;
  image: ProductThumbnail | null;
}

export interface BrandFacet {
  slug: string;
  name: string;
  count: number;
}

export type AttributeFacetType = 'enum' | 'text' | 'boolean' | 'number';

export interface AttributeFacetValue {
  // Chuỗi với `enum` và `text`, boolean với `boolean`.
  value: string | boolean;
  count: number;
}

export interface AttributeFacet {
  key: string;
  label: string;
  type: AttributeFacetType;
  unit?: string | null;
  // Có giá trị với `enum`, `text` và `boolean`.
  values?: AttributeFacetValue[] | null;
  // Có giá trị với `number`. Null khi không còn sản phẩm nào có thuộc tính này.
  min?: number | null;
  max?: number | null;
}

export interface ProductFacets {
  brands: BrandFacet[];
  attributes: AttributeFacet[];
}

export interface ProductListResponse {
  items: ProductListItem[];
  page: number;
  pageSize: number;
  total: number;
  facets: ProductFacets;
}

// Nút của cây danh mục công khai. Cây có tối đa hai cấp.
export interface CategoryNode {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  children: CategoryNode[];
}

// Thương hiệu trong `GET /api/brands`. Trang danh mục chỉ cần tên theo slug.
export interface BrandSummary {
  id: string;
  name: string;
  slug: string;
}

export interface ProductCategoryRef {
  id: string;
  name: string;
  slug: string;
}

// Một thông số kèm định nghĩa thuộc tính. `value` là null khi sản phẩm chưa có giá trị.
export interface ProductSpecEntry {
  key: string;
  label: string;
  group: string;
  type: AttributeFacetType;
  unit: string | null;
  value: string | number | boolean | null;
}

export interface ProductOptionInfo {
  name: string;
  values: string[];
}

// SKU đang bật. `stock` là số bán được còn lại để trang sản phẩm hiện số lượng.
export interface ProductSkuInfo {
  id: string;
  code: string;
  optionValues: Record<string, string>;
  price: number;
  compareAtPrice: number | null;
  inStock: boolean;
  stock: number;
}

// `skuId` khác null khi ảnh gắn riêng cho một SKU.
export interface ProductImageInfo {
  url: string;
  alt: string;
  skuId: string | null;
}

// Response của `GET /api/products/{slug}`. `status` là `active` hoặc `discontinued`.
export interface ProductDetail {
  id: string;
  slug: string;
  name: string;
  status: string;
  shortDesc: string;
  // Tài liệu JSON của trình soạn mô tả. Kiểu `unknown` vì nội dung phải được kiểm khi vẽ.
  description: unknown;
  warrantyMonths: number;
  brand: ProductBrandSummary;
  category: ProductCategoryRef;
  parentCategory: ProductCategoryRef | null;
  specs: ProductSpecEntry[];
  options: ProductOptionInfo[];
  skus: ProductSkuInfo[];
  images: ProductImageInfo[];
  minPrice: number | null;
  inStock: boolean;
}

// Định nghĩa một thuộc tính hiệu lực của danh mục trong `GET /api/products/compare`.
export interface CompareAttribute {
  key: string;
  label: string;
  group: string;
  type: AttributeFacetType;
  unit: string | null;
  options: string[] | null;
  required: boolean;
  filterable: boolean;
}

export interface CompareAttributeGroup {
  name: string;
  attributes: CompareAttribute[];
}

// Tóm tắt một sản phẩm trong bảng so sánh. `image` là ảnh ở vị trí đầu tiên.
export interface ComparedProductSummary {
  id: string;
  slug: string;
  name: string;
  brand: ProductBrandSummary;
  minPrice: number | null;
  inStock: boolean;
  status: string;
  image: ProductImageInfo | null;
}

export type CompareSpecValue = string | number | boolean | null;

export interface ComparedProduct {
  summary: ComparedProductSummary;
  // Có đủ khóa của mọi thuộc tính hiệu lực. Giá trị là null khi sản phẩm chưa nhập.
  specs: Record<string, CompareSpecValue>;
}

// Response của `GET /api/products/compare`. `category` là null khi không còn sản phẩm nào khách thấy được.
export interface ProductCompareResponse {
  category: ProductCategoryRef | null;
  attributeGroups: CompareAttributeGroup[];
  products: ComparedProduct[];
}
