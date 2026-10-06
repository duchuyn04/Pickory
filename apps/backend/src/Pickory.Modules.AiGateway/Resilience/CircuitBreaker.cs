using Pickory.Modules.AiGateway.Contracts.Errors;

namespace Pickory.Modules.AiGateway.Resilience;

// Circuit breaker theo từng provider. 5 lỗi liên tiếp mở mạch 60 giây, sau đó chỉ cho một probe chạy.
public sealed class CircuitBreaker(TimeProvider timeProvider)
{
    private const int FailureThreshold = 5;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, CircuitState> _states = new(StringComparer.Ordinal);
    // Nhiều request cùng đọc và sửa trạng thái mạch. Khóa giữ trạng thái nhất quán.
    private readonly Lock _gate = new();

    public void AssertAvailable(string provider)
    {
        lock (_gate)
        {
            AssertAvailableLocked(provider);
        }
    }

    public CircuitLease Acquire(string provider)
    {
        lock (_gate)
        {
            AssertAvailableLocked(provider);
            // Mỗi provider có bộ đếm riêng. Lỗi của chat không chặn embedding.
            if (!_states.TryGetValue(provider, out var state))
            {
                state = new CircuitState();
                _states[provider] = state;
            }

            var lease = new CircuitLease(state.Generation, isProbe: state.OpenedAt is not null);
            // Giữ đúng lease của probe trước khi trả về. Probe cũ không thể nhả quyền của probe mới cùng thế hệ.
            if (lease.IsProbe)
            {
                state.ProbeLease = lease;
            }

            return lease;
        }
    }

    public void Success(string provider, CircuitLease lease)
    {
        lock (_gate)
        {
            // Kết quả của thế hệ cũ không được đóng mạch đã mở bởi các request lỗi mới hơn.
            if (!_states.TryGetValue(provider, out var state) || !BelongsToCurrentGeneration(state, lease))
            {
                return;
            }

            state.Failures = 0;
            state.OpenedAt = null;
            state.ProbeLease = null;
        }
    }

    public void Failure(string provider, CircuitLease lease)
    {
        lock (_gate)
        {
            // Bỏ kết quả của thế hệ cũ. Một đợt request song song không kéo dài thời gian mở mạch.
            if (!_states.TryGetValue(provider, out var state) || !BelongsToCurrentGeneration(state, lease))
            {
                return;
            }

            state.Failures++;
            // Probe lỗi thì mở lại ngay, không chờ thêm 4 lỗi nữa.
            if (lease.IsProbe || state.Failures >= FailureThreshold)
            {
                state.OpenedAt = timeProvider.GetUtcNow();
                state.ProbeLease = null;
                state.Generation++;
            }
        }
    }

    // Nhả probe mà không ghi kết quả, ví dụ khi người gọi hủy hoặc ngừng đọc stream.
    public void Release(string provider, CircuitLease lease)
    {
        lock (_gate)
        {
            // Hai nơi có thể cùng nhả một stream. Lần nhả cũ không được chạm vào probe đang chạy.
            if (_states.TryGetValue(provider, out var state) && ReferenceEquals(state.ProbeLease, lease))
            {
                state.ProbeLease = null;
            }
        }
    }

    private void AssertAvailableLocked(string provider)
    {
        if (!_states.TryGetValue(provider, out var state))
        {
            return;
        }

        // Thử lại trong cửa sổ mở hoặc khi đã có probe sẽ tăng tải lên provider đang lỗi.
        var isOpen = state.OpenedAt is { } openedAt && timeProvider.GetUtcNow() - openedAt < OpenDuration;
        if (state.ProbeLease is not null || isOpen)
        {
            throw new AiUnavailableException();
        }
    }

    private static bool BelongsToCurrentGeneration(CircuitState state, CircuitLease lease)
    {
        return state.Generation == lease.Generation
            && (!lease.IsProbe || ReferenceEquals(state.ProbeLease, lease));
    }

    private sealed class CircuitState
    {
        public int Failures { get; set; }
        public DateTimeOffset? OpenedAt { get; set; }
        public CircuitLease? ProbeLease { get; set; }
        public int Generation { get; set; }
    }
}
