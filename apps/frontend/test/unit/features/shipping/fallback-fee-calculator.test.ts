import { describe, expect, it } from 'vitest';
import {
  SAMPLE_PARCEL,
  calculateFee,
  chargeableWeightGrams,
} from '../../../../src/features/shipping/fallback-fee-calculator';

// Cùng giá trị biên với `FallbackFeeCalculatorTests.cs` của backend, để hai luật tính không lệch nhau.
describe('chargeableWeightGrams', () => {
  it.each([
    // Cân thật đúng bội số của 500 thì giữ nguyên. Lệch 1 g thì lên nấc tiếp theo.
    { weight: 500, length: 1, width: 1, height: 1, expected: 500 },
    { weight: 501, length: 1, width: 1, height: 1, expected: 1000 },
    { weight: 499, length: 1, width: 1, height: 1, expected: 500 },
    { weight: 1, length: 1, width: 1, height: 1, expected: 500 },
    // Hộp 30 x 20 x 10 cm có cân quy đổi 1200 g, lớn hơn cân thật 300 g, làm tròn lên thành 1500.
    { weight: 300, length: 30, width: 20, height: 10, expected: 1500 },
    // Cân quy đổi đúng 1000 g thì giữ 1000.
    { weight: 300, length: 25, width: 20, height: 10, expected: 1000 },
    // Cân thật lớn hơn cân quy đổi thì lấy cân thật.
    { weight: 3000, length: 10, width: 10, height: 10, expected: 3000 },
    // Cân thật 0 g vẫn có cân quy đổi làm tròn lên, nên tính một nấc 500 g.
    { weight: 0, length: 1, width: 1, height: 1, expected: 500 },
  ])(
    'cân thật $weight g, hộp $length x $width x $height cm ra $expected g',
    ({ weight, length, width, height, expected }) => {
      expect(
        chargeableWeightGrams({
          weightGrams: weight,
          lengthCm: length,
          widthCm: width,
          heightCm: height,
        }),
      ).toBe(expected);
    },
  );

  it('làm tròn lên phần lẻ của cân quy đổi trước khi làm tròn lên nấc 500 g', () => {
    // 41 x 61 x 1 = 2501 cm3, chia 5 được 500,2 g, vượt 500 một chút nên lên 1000.
    expect(
      chargeableWeightGrams({ weightGrams: 100, lengthCm: 41, widthCm: 61, heightCm: 1 }),
    ).toBe(1000);
    // 11 x 10 x 10 = 1100 cm3, chia 5 được 220 g, vẫn dưới 500.
    expect(
      chargeableWeightGrams({ weightGrams: 100, lengthCm: 11, widthCm: 10, heightCm: 10 }),
    ).toBe(500);
  });

  it('gói mẫu cân thật 1200 g, hộp 30 x 20 x 10 cm ra 1500 g', () => {
    expect(chargeableWeightGrams(SAMPLE_PARCEL)).toBe(1500);
  });
});

describe('calculateFee', () => {
  it.each([
    // Khối lượng gốc 1000 g, tính cước 1000 g: chỉ giá gốc.
    { baseWeight: 1000, chargeable: 1000, baseFee: 10_000, stepFee: 3_000, expected: 10_000 },
    // Tính cước 1500 g: giá gốc cộng 1 lần giá bước.
    { baseWeight: 1000, chargeable: 1500, baseFee: 10_000, stepFee: 3_000, expected: 13_000 },
    // Khối lượng gốc 1200 g, tính cước 1500 g: phần lẻ 300 g tính một bước.
    { baseWeight: 1200, chargeable: 1500, baseFee: 10_000, stepFee: 3_000, expected: 13_000 },
    // Tính cước 3000 g: 2000 g vượt là 4 bước.
    { baseWeight: 1000, chargeable: 3000, baseFee: 10_000, stepFee: 3_000, expected: 22_000 },
    // Dưới khối lượng gốc vẫn chỉ trả giá gốc.
    { baseWeight: 2000, chargeable: 500, baseFee: 10_000, stepFee: 3_000, expected: 10_000 },
    // Vượt 1 g cũng tính một bước.
    { baseWeight: 1000, chargeable: 1001, baseFee: 10_000, stepFee: 3_000, expected: 13_000 },
    // Giá bước bằng 0 thì phí luôn là giá gốc.
    { baseWeight: 1000, chargeable: 5000, baseFee: 10_000, stepFee: 0, expected: 10_000 },
  ])(
    'khối lượng gốc $baseWeight g, tính cước $chargeable g ra $expected đ',
    ({ baseWeight, chargeable, baseFee, stepFee, expected }) => {
      expect(calculateFee({ baseFee, baseWeightGrams: baseWeight, stepFee }, chargeable)).toBe(
        expected,
      );
    },
  );

  it('giá lớn nhất cho kiện 30 kg vẫn là số nguyên chính xác', () => {
    // (30000 - 1) / 500 làm tròn lên là 60 bước.
    expect(
      calculateFee({ baseFee: 5_000_000, baseWeightGrams: 1, stepFee: 5_000_000 }, 30_000),
    ).toBe(5_000_000 + 60 * 5_000_000);
  });
});
