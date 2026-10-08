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

// SKU đang bật. Khách chỉ biết còn hàng hay không, không biết số tồn.
export interface ProductSkuInfo {
  id: string;
  code: string;
  optionValues: Record<string, string>;
  price: number;
  compareAtPrice: number | null;
  inStock: boolean;
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
