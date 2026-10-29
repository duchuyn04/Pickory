namespace Pickory.Modules.SupportDesk.Realtime;

// Một sự kiện chờ gửi qua SSE. `Data` được tuần tự hóa thành JSON camelCase khi ghi ra luồng.
internal sealed record SupportEvent(string Name, object Data);
