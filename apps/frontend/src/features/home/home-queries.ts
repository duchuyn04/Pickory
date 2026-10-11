import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../shared/api';
import type {
  CategoryNode,
  ProductListItem,
  ProductListResponse,
} from '../catalog/customer/catalog-api';

// Số thẻ trong một dải sản phẩm. Đủ cho hai lần cuộn ở màn rộng mà response vẫn nhỏ.
export const RAIL_PRODUCT_LIMIT = 10;

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
}

export function buildCategorySummaryPath(categorySlug: string): string {
  // Sắp giá tăng và lấy một sản phẩm. Một request cho cả tổng số sản phẩm và giá thấp nhất.
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
