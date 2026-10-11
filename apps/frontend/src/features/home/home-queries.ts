import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../shared/api';
import type {
  BrandFacet,
  CategoryNode,
  ProductDetail,
  ProductListItem,
  ProductListResponse,
} from '../catalog/customer/catalog-api';
import type { ProductSort } from '../catalog/customer/category-query';

// Số thẻ của một lưới sản phẩm: hai hàng sáu thẻ ở màn rộng 1200px, giống lưới của thegioididong.com.
export const RAIL_PRODUCT_LIMIT = 12;
// Lưới "Tất cả sản phẩm" tải thêm từng trang bằng một lưới đầy.
export const CATALOG_PAGE_SIZE = 12;

// Dùng chung khóa với trang danh mục. Khách đi từ trang chủ sang danh mục không phải tải lại cây.
export function useCategoryTree() {
  return useQuery<CategoryNode[]>({
    queryKey: ['categories'],
    queryFn: () => apiRequest('/api/categories'),
  });
}

export interface CategorySummary {
  productCount: number;
  // Giá thấp nhất trong danh mục. Null khi danh mục rỗng hoặc sản phẩm rẻ nhất chưa có giá.
  fromPrice: number | null;
  // Các hãng trong danh mục, hãng nhiều sản phẩm đứng trước. Banner danh mục lật lần lượt tên các hãng này.
  brands: BrandFacet[];
}

export function buildCategorySummaryPath(categorySlug: string): string {
  // Sắp giá tăng và lấy một sản phẩm. Một request cho cả tổng số sản phẩm, giá thấp nhất và các hãng.
  const params = new URLSearchParams({ category: categorySlug, sort: 'price_asc', pageSize: '1' });
  return `/api/products?${params.toString()}`;
}

export function useCategorySummary(categorySlug: string) {
  return useQuery<ProductListResponse, Error, CategorySummary>({
    queryKey: ['home', 'category-summary', categorySlug],
    queryFn: ({ signal }) => apiRequest(buildCategorySummaryPath(categorySlug), { signal }),
    select: (response) => ({
      productCount: response.total,
      fromPrice: response.items[0]?.minPrice ?? null,
      brands: [...response.facets.brands].sort((first, second) => second.count - first.count),
    }),
  });
}

export function buildRailProductsPath(categorySlug: string): string {
  // Sản phẩm mới nhất đứng đầu, cùng thứ tự mặc định của trang danh mục mà nút "Xem tất cả" mở ra.
  const params = new URLSearchParams({
    category: categorySlug,
    sort: 'newest',
    pageSize: String(RAIL_PRODUCT_LIMIT),
  });
  return `/api/products?${params.toString()}`;
}

export function useRailProducts(categorySlug: string) {
  return useQuery<ProductListResponse, Error, ProductListItem[]>({
    queryKey: ['home', 'rail', categorySlug],
    queryFn: ({ signal }) => apiRequest(buildRailProductsPath(categorySlug), { signal }),
    select: (response) => response.items,
  });
}

export function buildCatalogOverviewPath(): string {
  // Không lọc danh mục và lấy một sản phẩm. Response vẫn mang tổng số sản phẩm và facet thương hiệu của cả catalog.
  const params = new URLSearchParams({ pageSize: '1' });
  return `/api/products?${params.toString()}`;
}

export interface CatalogOverview {
  productCount: number;
  // Hãng có nhiều sản phẩm đứng trước, để khách thấy các hãng quen thuộc ngay đầu dải.
  brands: BrandFacet[];
}

export function useCatalogOverview() {
  return useQuery<ProductListResponse, Error, CatalogOverview>({
    queryKey: ['home', 'catalog-overview'],
    queryFn: ({ signal }) => apiRequest(buildCatalogOverviewPath(), { signal }),
    select: (response) => ({
      productCount: response.total,
      brands: [...response.facets.brands].sort((first, second) => second.count - first.count),
    }),
  });
}

export function buildBrandProductsPath(brandSlug: string): string {
  const params = new URLSearchParams({
    brand: brandSlug,
    sort: 'newest',
    pageSize: String(RAIL_PRODUCT_LIMIT),
  });
  return `/api/products?${params.toString()}`;
}

export function useBrandProducts(brandSlug: string | null) {
  return useQuery<ProductListResponse>({
    queryKey: ['home', 'brand-products', brandSlug],
    queryFn: ({ signal }) => apiRequest(buildBrandProductsPath(brandSlug ?? ''), { signal }),
    // Chờ danh sách hãng tải xong mới biết hãng đầu tiên để hỏi sản phẩm.
    enabled: brandSlug !== null,
  });
}

export function buildCatalogPagePath(sort: ProductSort, page: number): string {
  const params = new URLSearchParams({
    sort,
    page: String(page),
    pageSize: String(CATALOG_PAGE_SIZE),
  });
  return `/api/products?${params.toString()}`;
}

export function useCatalogPages(sort: ProductSort) {
  return useInfiniteQuery({
    queryKey: ['home', 'catalog-pages', sort],
    queryFn: ({ pageParam, signal }) =>
      apiRequest<ProductListResponse>(buildCatalogPagePath(sort, pageParam), { signal }),
    initialPageParam: 1,
    // Trang cuối là trang đã chứa sản phẩm thứ `total`. Sau đó không còn trang để tải.
    getNextPageParam: (lastPage) =>
      lastPage.page * lastPage.pageSize < lastPage.total ? lastPage.page + 1 : undefined,
  });
}

// Dùng chung khóa với trang chi tiết. Khách bấm vào sản phẩm từ banner thì trang chi tiết có dữ liệu ngay.
export function productDetailQueryOptions(productSlug: string) {
  return {
    queryKey: ['product', productSlug],
    queryFn: () => apiRequest<ProductDetail>(`/api/products/${encodeURIComponent(productSlug)}`),
  };
}

export function useProductDetail(productSlug: string) {
  return useQuery(productDetailQueryOptions(productSlug));
}
