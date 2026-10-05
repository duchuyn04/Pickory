import { describe, expect, it } from 'vitest';
import { calculateTokenCostUsd } from '../../../../src/modules/ai-gateway/pricing';

describe('Bảng giá và tính chi phí token AI', () => {
  it('tính đúng chi phí DeepSeek Flash theo biểu giá conservative peak', () => {
    // 1 triệu token miss, 500k token cache hit, 200k token output.
    const usage = {
      inputTokens: 1_500_000,
      cachedTokens: 500_000,
      outputTokens: 200_000,
    };

    const cost = calculateTokenCostUsd('deepseek-flash', usage);

    // nonCached = 1M * 0.3 = 0.3 USD.
    // cached = 0.5M * 0.006 = 0.003 USD.
    // output = 0.2M * 1.2 = 0.24 USD.
    // Tổng = 0.543 USD.
    expect(cost).toBeCloseTo(0.543, 5);
  });

  it('áp dụng đúng đơn giá cho model DeepSeek Pro chính thức', () => {
    const cost = calculateTokenCostUsd('deepseek-v4-pro', {
      inputTokens: 1_500_000,
      cachedTokens: 500_000,
      outputTokens: 200_000,
    });
    expect(cost).toBeCloseTo(2.134, 5);
  });

  it('tính đúng chi phí OpenAI text-embedding-3-small với 0.02 USD / 1M token', () => {
    const usage = {
      inputTokens: 1_000_000,
      outputTokens: 0,
      cachedTokens: 0,
    };

    const cost = calculateTokenCostUsd('text-embedding-3-small', usage);
    expect(cost).toBeCloseTo(0.02, 6);
  });

  it('trả về chi phí 0 USD cho các model giả lập được định danh cụ thể', () => {
    const chatCost = calculateTokenCostUsd('fake-chat', {
      inputTokens: 10_000,
      outputTokens: 5_000,
      cachedTokens: 0,
    });
    expect(chatCost).toBe(0);

    const embedCost = calculateTokenCostUsd('fake-embedding', {
      inputTokens: 20_000,
      outputTokens: 0,
      cachedTokens: 0,
    });
    expect(embedCost).toBe(0);
  });

  it('từ chối an toàn các model đã ngưng hỗ trợ hoặc chưa thẩm định giá', () => {
    // deepseek-chat đã ngưng hỗ trợ từ tháng 7/2026 nên bị từ chối thay vì tự gán bừa đơn giá.
    expect(() => {
      calculateTokenCostUsd('deepseek-chat', {
        inputTokens: 100,
        outputTokens: 50,
        cachedTokens: 0,
      });
    }).toThrow();
  });
});
