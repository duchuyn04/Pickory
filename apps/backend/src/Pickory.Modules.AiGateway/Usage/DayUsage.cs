namespace Pickory.Modules.AiGateway.Usage;

// Chi phí và số bước của một ngày theo giờ Việt Nam. `Day` có dạng `yyyy-MM-dd`.
public sealed record DayUsage(string Day, double CostUsd, int Calls);
