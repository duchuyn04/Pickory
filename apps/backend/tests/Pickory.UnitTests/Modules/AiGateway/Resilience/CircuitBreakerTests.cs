using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Resilience;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Resilience;

// Port từ `circuit-breaker.test.ts` của bản Node.
public sealed class CircuitBreakerTests
{
    private static FakeTimeProvider CreateTime()
    {
        return new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
    }

    private static void OpenCircuit(CircuitBreaker circuit, string provider)
    {
        for (var failure = 0; failure < 5; failure++)
        {
            circuit.Failure(provider, circuit.Acquire(provider));
        }
    }

    [Fact]
    // Kết quả thành công cũ không đóng mạch vừa mở. Lỗi của provider chat không làm mở mạch của embedding.
    public void Success_FromOlderGenerationDoesNotCloseCircuit()
    {
        var circuit = new CircuitBreaker(CreateTime());
        var oldSuccess = circuit.Acquire("deepseek");
        var failures = Enumerable.Range(0, 5).Select(_ => circuit.Acquire("deepseek")).ToList();
        foreach (var failure in failures)
        {
            circuit.Failure("deepseek", failure);
        }

        circuit.Success("deepseek", oldSuccess);

        Assert.Throws<AiUnavailableException>(() => circuit.Acquire("deepseek"));
        var embeddingLease = circuit.Acquire("openai");
        circuit.Success("openai", embeddingLease);
        Assert.False(circuit.Acquire("openai").IsProbe);
    }

    [Fact]
    // Sau 60 giây chỉ có một probe. Probe lỗi mở lại mạch thêm 60 giây, người gọi hủy probe thì nhả quyền thử.
    public void Probe_FailureReopensForFullWindowAndReleaseFreesProbe()
    {
        var time = CreateTime();
        var circuit = new CircuitBreaker(time);
        OpenCircuit(circuit, "deepseek");
        time.Advance(TimeSpan.FromSeconds(60));

        var probe = circuit.Acquire("deepseek");
        Assert.Throws<AiUnavailableException>(() => circuit.Acquire("deepseek"));
        circuit.Release("deepseek", probe);
        var retryProbe = circuit.Acquire("deepseek");
        circuit.Failure("deepseek", retryProbe);
        time.Advance(TimeSpan.FromMilliseconds(59_999));

        Assert.Throws<AiUnavailableException>(() => circuit.Acquire("deepseek"));
        time.Advance(TimeSpan.FromMilliseconds(1));
        var recovered = circuit.Acquire("deepseek");
        circuit.Success("deepseek", recovered);
        Assert.False(circuit.Acquire("deepseek").IsProbe);
    }

    [Fact]
    // Lease probe cũ không được nhả hoặc ghi kết quả thay cho probe mới cùng thế hệ.
    public void Probe_OldLeaseCannotAffectCurrentProbe()
    {
        var time = CreateTime();
        var circuit = new CircuitBreaker(time);
        OpenCircuit(circuit, "deepseek");
        time.Advance(TimeSpan.FromSeconds(60));
        var oldProbe = circuit.Acquire("deepseek");
        circuit.Release("deepseek", oldProbe);
        var currentProbe = circuit.Acquire("deepseek");

        circuit.Release("deepseek", oldProbe);
        Assert.Throws<AiUnavailableException>(() => circuit.Acquire("deepseek"));
        circuit.Success("deepseek", oldProbe);
        Assert.Throws<AiUnavailableException>(() => circuit.Acquire("deepseek"));
        circuit.Failure("deepseek", oldProbe);

        circuit.Success("deepseek", currentProbe);
        Assert.False(circuit.Acquire("deepseek").IsProbe);
    }
}
