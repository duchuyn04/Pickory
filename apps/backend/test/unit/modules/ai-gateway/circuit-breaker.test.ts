import { describe, expect, it } from 'vitest';
import { CircuitBreaker } from '../../../../src/modules/ai-gateway/circuit-breaker';
import { AiUnavailableError } from '../../../../src/modules/ai-gateway/errors';

describe('CircuitBreaker', () => {
  it('kết quả thành công cũ không đóng mạch vừa mở bởi năm request lỗi', () => {
    const circuit = new CircuitBreaker(() => 1000);
    const oldSuccess = circuit.acquire('deepseek');
    const failures = Array.from({ length: 5 }, () => circuit.acquire('deepseek'));
    for (const failure of failures) circuit.failure('deepseek', failure);
    circuit.success('deepseek', oldSuccess);
    expect(() => circuit.acquire('deepseek')).toThrow(AiUnavailableError);
    const embeddingLease = circuit.acquire('openai');
    circuit.success('openai', embeddingLease);
    expect(circuit.acquire('openai').probe).toBe(false);
  });
  it('probe thất bại mở lại đủ 60 giây, người gọi hủy probe thì nhả quyền thử', () => {
    let now = 0;
    const circuit = new CircuitBreaker(() => now);
    for (let failure = 0; failure < 5; failure += 1)
      circuit.failure('deepseek', circuit.acquire('deepseek'));
    now = 60000;
    const probe = circuit.acquire('deepseek');
    expect(() => circuit.acquire('deepseek')).toThrow(AiUnavailableError);
    circuit.release('deepseek', probe);
    const retryProbe = circuit.acquire('deepseek');
    circuit.failure('deepseek', retryProbe);
    now = 119999;
    expect(() => circuit.acquire('deepseek')).toThrow(AiUnavailableError);
    now = 120000;
    const recovered = circuit.acquire('deepseek');
    circuit.success('deepseek', recovered);
    expect(circuit.acquire('deepseek').probe).toBe(false);
  });
  it('lease probe cũ không nhả hoặc ghi kết quả thay cho probe mới cùng thế hệ', () => {
    let now = 0;
    const circuit = new CircuitBreaker(() => now);
    for (let failure = 0; failure < 5; failure += 1)
      circuit.failure('deepseek', circuit.acquire('deepseek'));
    now = 60000;
    const oldProbe = circuit.acquire('deepseek');
    circuit.release('deepseek', oldProbe);
    const currentProbe = circuit.acquire('deepseek');
    circuit.release('deepseek', oldProbe);
    expect(() => circuit.acquire('deepseek')).toThrow(AiUnavailableError);
    circuit.success('deepseek', oldProbe);
    expect(() => circuit.acquire('deepseek')).toThrow(AiUnavailableError);
    circuit.failure('deepseek', oldProbe);
    circuit.success('deepseek', currentProbe);
    expect(circuit.acquire('deepseek').probe).toBe(false);
  });
});
