import type { AttributeFacet, BrandFacet } from './catalog-api';

// Một lựa chọn trong nhóm bộ lọc. `value` là chuỗi gửi lên URL, `count` là số sản phẩm khớp.
export interface FilterOption {
  value: string;
  label: string;
  count: number;
}

export function formatOptionLabel(option: FilterOption): string {
  return `${option.label} (${option.count})`;
}

function formatAttributeValue(facet: AttributeFacet, value: string): string {
  if (facet.type === 'boolean') return value === 'true' ? 'Có' : 'Không';
  return facet.unit ? `${value} ${facet.unit}` : value;
}

// Giá trị đang chọn mà facets không còn vẫn hiện với số đếm 0, để khách bỏ chọn được.
function appendMissingSelections(
  options: FilterOption[],
  selectedValues: string[],
  labelOf: (value: string) => string,
): FilterOption[] {
  const knownValues = new Set(options.map((option) => option.value));
  const missingOptions = selectedValues
    .filter((value) => !knownValues.has(value))
    .map((value) => ({ value, label: labelOf(value), count: 0 }));
  return [...options, ...missingOptions];
}

export function buildAttributeOptions(
  facet: AttributeFacet,
  selectedValues: string[],
): FilterOption[] {
  const options = (facet.values ?? []).map((facetValue) => {
    // Giá trị boolean đổi thành chuỗi để cùng dạng với giá trị trên URL.
    const value = String(facetValue.value);
    return { value, label: formatAttributeValue(facet, value), count: facetValue.count };
  });
  return appendMissingSelections(options, selectedValues, (value) =>
    formatAttributeValue(facet, value),
  );
}

export function buildBrandOptions(
  brands: BrandFacet[],
  selectedSlugs: string[],
  brandNamesBySlug: Record<string, string> = {},
): FilterOption[] {
  const options = brands.map((brand) => ({
    value: brand.slug,
    label: brand.name,
    count: brand.count,
  }));
  // Thương hiệu đã chọn mà facets không còn thì lấy tên từ bảng tra. Không có tên mới hiện slug.
  return appendMissingSelections(options, selectedSlugs, (slug) => brandNamesBySlug[slug] ?? slug);
}

export function attributeLegend(facet: AttributeFacet): string {
  return facet.unit ? `${facet.label} (${facet.unit})` : facet.label;
}
