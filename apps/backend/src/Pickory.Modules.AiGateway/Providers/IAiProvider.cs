namespace Pickory.Modules.AiGateway.Providers;

// Định danh chung của provider. Usage log, bảng giá và circuit breaker dùng `Name` và `Model`.
public interface IAiProvider
{
    string Name { get; }
    string Model { get; }
}
