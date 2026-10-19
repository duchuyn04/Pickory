// Luật tính phí dự phòng cho ví dụ trên màn cấu hình. Cùng công thức với `FallbackFeeCalculator.cs` của backend.

export interface Parcel {
  weightGrams: number;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
}

// Giá của một tuyến: giá gốc đã gồm `baseWeightGrams` đầu tiên, rồi `stepFee` cho mỗi 500 g tiếp theo.
export interface FeeTier {
  baseFee: number;
  baseWeightGrams: number;
  stepFee: number;
}

// GHN tính cước theo nấc 500 g.
export const FEE_STEP_GRAMS = 500;

// Cân quy đổi (gram) bằng dài x rộng x cao (cm) chia 5. Công thức này tương đương chia 5000 rồi đổi kg sang gram.
const VOLUMETRIC_DIVISOR = 5;

// Gói mẫu của ví dụ: cân thật 1.200 g, hộp 30 x 20 x 10 cm. Cân quy đổi cũng là 1.200 g, nên khối lượng tính cước là 1.500 g.
export const SAMPLE_PARCEL: Parcel = { weightGrams: 1200, lengthCm: 30, widthCm: 20, heightCm: 10 };

// Chia số nguyên không âm và làm tròn lên. Dùng phép lấy dư thay cho số thực để không sai số ở giá trị biên.
function ceilingDivide(value: number, divisor: number): number {
  const quotient = Math.floor(value / divisor);
  return value % divisor === 0 ? quotient : quotient + 1;
}

export function chargeableWeightGrams(parcel: Parcel): number {
  const volumeCubicCm = parcel.lengthCm * parcel.widthCm * parcel.heightCm;
  // Làm tròn lên khi chia 5. Phần lẻ của cân quy đổi không được mất, vì sau đó cả số còn được làm tròn lên nấc 500 g.
  const volumetricGrams = ceilingDivide(volumeCubicCm, VOLUMETRIC_DIVISOR);
  // GHN lấy số lớn hơn giữa cân thật và cân quy đổi. Hàng nhẹ mà cồng kềnh vẫn bị tính theo thể tích.
  const heavierGrams = Math.max(parcel.weightGrams, volumetricGrams);
  // Làm tròn lên bội số của 500 g, giống bảng giá GHN.
  return ceilingDivide(heavierGrams, FEE_STEP_GRAMS) * FEE_STEP_GRAMS;
}

export function calculateFee(tier: FeeTier, chargeableGrams: number): number {
  // Khối lượng không vượt phần đã gồm trong giá gốc thì chỉ trả giá gốc.
  if (chargeableGrams <= tier.baseWeightGrams) return tier.baseFee;
  // Mỗi 500 g vượt, kể cả phần lẻ, cộng một lần giá bước.
  const extraSteps = ceilingDivide(chargeableGrams - tier.baseWeightGrams, FEE_STEP_GRAMS);
  return tier.baseFee + extraSteps * tier.stepFee;
}
