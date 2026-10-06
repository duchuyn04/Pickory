namespace Pickory.Modules.AiGateway.Resilience;

// Quyền gọi provider của một lần thử. Circuit breaker so sánh theo tham chiếu để phân biệt hai probe cùng thế hệ.
public sealed class CircuitLease(int generation, bool isProbe)
{
    public int Generation { get; } = generation;

    // Probe là request duy nhất được chạy sau khi cửa sổ mở mạch kết thúc.
    public bool IsProbe { get; } = isProbe;
}
