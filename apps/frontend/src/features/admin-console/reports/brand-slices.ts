import type { SalesReportBrand } from './reports-api';

export const OTHER_BRAND_KEY = 'other';
export const OTHER_BRAND_LABEL = 'Khác';

// Số lát thương hiệu tối đa, không tính lát "Khác".
export const MAX_BRAND_SLICES = 6;

// Một lát của biểu đồ tròn doanh thu theo thương hiệu.
export interface BrandSlice {
  key: string;
  label: string;
  revenue: number;
}

// Gộp thương hiệu của tháng theo doanh thu. Quá sáu thương hiệu thì phần còn lại vào lát "Khác".
export function buildBrandSlices(brands: readonly SalesReportBrand[]): BrandSlice[] {
  const namedBrands: SalesReportBrand[] = [];
  const unbrandedGroups: SalesReportBrand[] = [];
  for (const brand of brands) {
    // Doanh thu bằng 0 không tạo lát vẽ được. Bỏ để chú giải không có mục vô nghĩa.
    if (brand.revenue <= 0) continue;
    if (brand.brandId === null) {
      unbrandedGroups.push(brand);
    } else {
      namedBrands.push(brand);
    }
  }

  // API đã sắp giảm dần, nhưng sắp lại để hàm không phụ thuộc thứ tự của nguồn.
  namedBrands.sort((left, right) => right.revenue - left.revenue);
  const visibleBrands = namedBrands.slice(0, MAX_BRAND_SLICES);
  // Sản phẩm không có thương hiệu cũng vào "Khác", nên mọi doanh thu đều có lát đại diện.
  const hiddenGroups = [...namedBrands.slice(MAX_BRAND_SLICES), ...unbrandedGroups];

  const slices: BrandSlice[] = visibleBrands.map((brand) => ({
    key: brand.brandId ?? '',
    label: brand.brandName?.trim() || 'Không rõ thương hiệu',
    revenue: brand.revenue,
  }));

  if (hiddenGroups.length > 0) {
    slices.push({
      key: OTHER_BRAND_KEY,
      label: OTHER_BRAND_LABEL,
      revenue: hiddenGroups.reduce((sum, brand) => sum + brand.revenue, 0),
    });
  }

  return slices;
}

// Tổng doanh thu của các lát. Dùng làm mẫu số của phần trăm.
export function totalBrandRevenue(slices: readonly BrandSlice[]): number {
  return slices.reduce((sum, slice) => sum + slice.revenue, 0);
}
