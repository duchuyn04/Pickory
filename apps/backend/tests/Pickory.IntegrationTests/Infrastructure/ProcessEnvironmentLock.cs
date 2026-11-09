namespace Pickory.IntegrationTests.Infrastructure;

// Khóa đồng bộ tiến trình để ngăn race condition khi test thay đổi biến môi trường.
internal static class ProcessEnvironmentLock
{
    // Đối tượng khóa dùng chung cho các test cần thay đổi biến môi trường tiến trình.
    internal static readonly object Gate = new();
}
