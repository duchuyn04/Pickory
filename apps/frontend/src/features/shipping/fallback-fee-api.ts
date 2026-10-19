import { apiRequest } from '../../shared/api';

// Ba tuyến có giá riêng và ba giá trị của mỗi tuyến. Tên ô ghép từ hai phần, đúng tên trường JSON của backend.
export const FALLBACK_FEE_TIERS = ['sameProvince', 'sameRegion', 'crossRegion'] as const;
export const FALLBACK_FEE_KINDS = ['BaseFee', 'BaseWeightGrams', 'StepFee'] as const;

export type FallbackFeeTier = (typeof FALLBACK_FEE_TIERS)[number];
export type FallbackFeeKind = (typeof FALLBACK_FEE_KINDS)[number];
export type FallbackFeeFieldName = `${FallbackFeeTier}${FallbackFeeKind}`;

// Chín ô giá theo thứ tự hiện trên màn: từng tuyến một, mỗi tuyến ba giá trị.
export const FALLBACK_FEE_FIELDS: readonly FallbackFeeFieldName[] = FALLBACK_FEE_TIERS.flatMap(
  (tier) => FALLBACK_FEE_KINDS.map((kind): FallbackFeeFieldName => `${tier}${kind}`),
);

// Chín giá. `null` là admin chưa nhập, và chỉ hợp lệ khi phí dự phòng đang tắt.
export type FallbackFeePrices = Record<FallbackFeeFieldName, number | null>;

// Cấu hình đã lưu. Khớp `FallbackFeeView` của backend.
export interface FallbackFee extends FallbackFeePrices {
  enabled: boolean;
  // `null` khi shop chưa lưu lần nào và backend trả giá trị mặc định.
  updatedAt: string | null;
}

// Thân của PUT. Backend từ chối mọi trường khác, kể cả `updatedAt`.
export interface FallbackFeePayload extends FallbackFeePrices {
  enabled: boolean;
}

const FALLBACK_FEE_PATH = '/api/admin/shipping/fallback-fee';

export const fallbackFeeQueryKey = ['admin-shipping-fallback-fee'] as const;

export function fetchFallbackFee(): Promise<FallbackFee> {
  return apiRequest<FallbackFee>(FALLBACK_FEE_PATH);
}

export function saveFallbackFee(payload: FallbackFeePayload): Promise<FallbackFee> {
  return apiRequest<FallbackFee>(FALLBACK_FEE_PATH, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}
