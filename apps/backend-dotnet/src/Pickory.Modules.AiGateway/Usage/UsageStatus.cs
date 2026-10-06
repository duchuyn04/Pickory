namespace Pickory.Modules.AiGateway.Usage;

// Trạng thái của một bước gọi AI. `Blocked` là bước gateway chặn trước khi gọi provider.
public enum UsageStatus
{
    Ok,
    Error,
    Blocked,
}
