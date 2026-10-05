import { AiUnavailableError } from './errors';

type CircuitState = {
  failures: number;
  openedAt?: number;
  probeLease?: CircuitLease;
  generation: number;
};
export type CircuitLease = { generation: number; probe: boolean };

export class CircuitBreaker {
  private readonly states = new Map<string, CircuitState>();
  constructor(private readonly now: () => number = Date.now) {}

  assertAvailable(provider: string): void {
    const state = this.states.get(provider);
    // Thử lại trong cửa sổ mở hoặc khi đã có probe sẽ gây thêm tải lên provider đang lỗi.
    if (
      state &&
      (state.probeLease || (state.openedAt !== undefined && this.now() - state.openedAt < 60000))
    ) {
      throw new AiUnavailableError();
    }
  }

  acquire(provider: string): CircuitLease {
    this.assertAvailable(provider);
    let state = this.states.get(provider);
    // Mỗi provider có bộ đếm riêng để lỗi chat không vô hiệu hóa embedding.
    if (!state) {
      state = { failures: 0, generation: 0 };
      this.states.set(provider, state);
    }
    const lease = { generation: state.generation, probe: state.openedAt !== undefined };
    // Giữ đúng lease trước await để probe cũ không thể nhả quyền của probe mới cùng thế hệ.
    if (lease.probe) state.probeLease = lease;
    return lease;
  }

  success(provider: string, lease: CircuitLease): void {
    const state = this.states.get(provider);
    // Kết quả cũ không được đóng mạch đã mở bởi các request lỗi mới hơn.
    if (
      !state ||
      state.generation !== lease.generation ||
      (lease.probe && state.probeLease !== lease)
    )
      return;
    state.failures = 0;
    state.openedAt = undefined;
    state.probeLease = undefined;
  }

  failure(provider: string, lease: CircuitLease): void {
    const state = this.states.get(provider);
    // Bỏ kết quả thuộc thế hệ cũ để một đợt request song song không kéo dài thời gian mở.
    if (
      !state ||
      state.generation !== lease.generation ||
      (lease.probe && state.probeLease !== lease)
    )
      return;
    state.failures += 1;
    // Probe thất bại phải mở lại ngay, không chờ thêm bốn request thất bại.
    if (lease.probe || state.failures >= 5) {
      state.openedAt = this.now();
      state.probeLease = undefined;
      state.generation += 1;
    }
  }

  release(provider: string, lease: CircuitLease): void {
    const state = this.states.get(provider);
    // Reaper và finally có thể cùng nhả một stream; lần nhả cũ không được chạm probe đang chạy.
    if (state && state.probeLease === lease) state.probeLease = undefined;
  }
}
