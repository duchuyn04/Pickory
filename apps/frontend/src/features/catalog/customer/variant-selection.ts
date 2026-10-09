import type { ProductSkuInfo } from './catalog-api';

// Lựa chọn của khách: tên tùy chọn trỏ tới giá trị đang chọn, ví dụ `{ Màu: 'Đen' }`.
export type OptionSelection = Record<string, string>;

export function pickInitialSku(
  skus: ProductSkuInfo[],
  requestedSkuId: string | null,
): ProductSkuInfo | null {
  // Link chia sẻ phải mở đúng biến thể, kể cả biến thể hết hàng. Id lạ thì rơi xuống quy tắc mặc định.
  const requestedSku = skus.find((sku) => sku.id === requestedSkuId);
  if (requestedSku) return requestedSku;

  // Mặc định ưu tiên SKU còn hàng, để khách thấy ngay biến thể mua được.
  const firstInStock = skus.find((sku) => sku.inStock);
  return firstInStock ?? skus[0] ?? null;
}

export function findSkuBySelection(
  skus: ProductSkuInfo[],
  selection: OptionSelection,
): ProductSkuInfo | null {
  const selectedEntries = Object.entries(selection);
  const matchedSku = skus.find((sku) =>
    selectedEntries.every(([optionName, value]) => sku.optionValues[optionName] === value),
  );
  return matchedSku ?? null;
}

// Một giá trị chỉ bị tắt khi không SKU đang bật nào mang giá trị đó. Tắt thêm sẽ chặn khách tới SKU hợp lệ.
export function isOptionValueAvailable(
  skus: ProductSkuInfo[],
  optionName: string,
  value: string,
): boolean {
  return skus.some((sku) => sku.optionValues[optionName] === value);
}

// Giá trị ghép được với lựa chọn hiện tại thành một SKU. Nếu không, bấm vào sẽ đổi cả lựa chọn khác.
export function isOptionValueCombinable(
  skus: ProductSkuInfo[],
  selection: OptionSelection,
  optionName: string,
  value: string,
): boolean {
  const candidateSelection = selectOptionValue(selection, optionName, value);
  return findSkuBySelection(skus, candidateSelection) !== null;
}

// Đếm số giá trị của tùy chọn khác mà SKU vẫn giữ giống lựa chọn hiện tại.
function countKeptValues(
  sku: ProductSkuInfo,
  selection: OptionSelection,
  changedOptionName: string,
): number {
  return Object.entries(selection).filter(
    ([optionName, value]) =>
      optionName !== changedOptionName && sku.optionValues[optionName] === value,
  ).length;
}

// Chọn SKU khi khách bấm một giá trị: giữ được nhiều giá trị đang chọn nhất, hòa thì ưu tiên còn hàng.
export function chooseSkuForOptionValue(
  skus: ProductSkuInfo[],
  selection: OptionSelection,
  optionName: string,
  value: string,
): ProductSkuInfo | null {
  let bestSku: ProductSkuInfo | null = null;
  let bestKept = -1;
  for (const sku of skus) {
    if (sku.optionValues[optionName] !== value) continue;
    const kept = countKeptValues(sku, selection, optionName);
    const isBetterByKept = kept > bestKept;
    // Hòa số giá trị giữ được thì SKU còn hàng thắng. Hòa tiếp thì SKU đứng trước giữ chỗ, vì chỉ thay khi tốt hơn hẳn.
    const isBetterByStock =
      kept === bestKept && sku.inStock && bestSku !== null && !bestSku.inStock;
    if (isBetterByKept || isBetterByStock) {
      bestSku = sku;
      bestKept = kept;
    }
  }
  return bestSku;
}

export function selectOptionValue(
  selection: OptionSelection,
  optionName: string,
  value: string,
): OptionSelection {
  return { ...selection, [optionName]: value };
}
