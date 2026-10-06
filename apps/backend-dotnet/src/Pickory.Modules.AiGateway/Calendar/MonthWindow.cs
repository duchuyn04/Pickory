namespace Pickory.Modules.AiGateway.Calendar;

// Khoảng thời gian của một tháng theo giờ Việt Nam, đổi sang UTC. `NextStartUtc` không thuộc tháng.
public sealed record MonthWindow(DateTime StartUtc, DateTime NextStartUtc);
