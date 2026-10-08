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
